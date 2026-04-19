using System.Reactive.Linq;
using QCEDL.NET.PartitionTable;
using Qualcomm.EmergencyDownload.Core;

namespace QCEDL.GUI.Services;

/// <summary>
/// Thin service surface over <see cref="EdlManager"/>. Holds the current session so views
/// share a single connection, and serialises destructive operations behind a lock so the
/// UI cannot issue conflicting commands.
/// </summary>
public sealed class EdlService : IDisposable
{
    private readonly SemaphoreSlim _opLock = new(1, 1);
    private EdlManager? _manager;

    public EdlOptions Options { get; set; } = new();

    public bool IsConnected => _manager?.IsFirehoseMode == true || _manager?.IsDirectMode == true;

    public DeviceMode CurrentMode => _manager?.CurrentMode ?? DeviceMode.Unknown;

    /// <summary>Fires whenever session state may have changed (post-op or after reset).</summary>
    public event EventHandler? StateChanged;

    /// <summary>Observable stream of <see cref="IsConnected"/>, seeded with the current value.</summary>
    public IObservable<bool> WhenConnectedChanged =>
        Observable.FromEventPattern(h => StateChanged += h, h => StateChanged -= h)
            .Select(_ => IsConnected)
            .StartWith(IsConnected)
            .DistinctUntilChanged();

    /// <summary>Prepare a manager from the current <see cref="Options"/> snapshot.</summary>
    private EdlManager EnsureManager()
    {
        _manager ??= new EdlManager(CloneOptions(Options));
        return _manager;
    }

    /// <summary>Run an operation under the single-op lock so clicks can't overlap.</summary>
    public async Task<T> RunExclusiveAsync<T>(Func<EdlManager, Task<T>> action, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        await _opLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await action(EnsureManager()).ConfigureAwait(false);
        }
        finally
        {
            _opLock.Release();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public Task RunExclusiveAsync(Func<EdlManager, Task> action, CancellationToken ct = default)
    {
        return RunExclusiveAsync(async m =>
        {
            await action(m).ConfigureAwait(false);
            return 0;
        }, ct);
    }

    public async Task<DeviceMode> ProbeAsync(CancellationToken ct = default)
    {
        return await RunExclusiveAsync(m => m.DetectCurrentModeAsync(forceReconnect: true), ct).ConfigureAwait(false);
    }

    public async Task EnsureFirehoseAsync(CancellationToken ct = default)
    {
        await RunExclusiveAsync(m => m.EnsureFirehoseModeAsync(), ct).ConfigureAwait(false);
    }

    public async Task<StorageGeometry> GetGeometryAsync(uint lun, CancellationToken ct = default)
    {
        return await RunExclusiveAsync(m => m.GetStorageGeometryAsync(lun), ct).ConfigureAwait(false);
    }

    public async Task<byte[]> ReadSectorsAsync(uint lun, ulong start, uint count, CancellationToken ct = default)
    {
        return await RunExclusiveAsync(m => m.ReadSectorsAsync(lun, start, count), ct).ConfigureAwait(false);
    }

    public void ResetSession()
    {
        _manager?.Dispose();
        _manager = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _manager?.Dispose();
        _opLock.Dispose();
    }

    private static EdlOptions CloneOptions(EdlOptions src)
    {
        return new()
        {
            LoaderPath = src.LoaderPath,
            Vid = src.Vid,
            Pid = src.Pid,
            MemoryType = src.MemoryType,
            LogLevel = src.LogLevel,
            MaxPayloadSize = src.MaxPayloadSize,
            Slot = src.Slot,
            HostDevAsTarget = src.HostDevAsTarget,
            ImgSize = src.ImgSize,
            RadxaWosPlatform = src.RadxaWosPlatform,
        };
    }
}

/// <summary>Helper type used by the partitions view to render GPT entries.</summary>
public sealed record PartitionRow(
    uint Lun,
    int Index,
    string Name,
    ulong FirstLba,
    ulong LastLba,
    ulong SizeBytes,
    string TypeGuid)
{
    public string SizeText => FormatSize(SizeBytes);

    private static string FormatSize(ulong bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        var i = 0;
        double value = bytes;
        while (value >= 1024 && i < units.Length - 1)
        {
            value /= 1024;
            i++;
        }
        return $"{value:0.##} {units[i]}";
    }

    public static PartitionRow From(GptPartition p, uint lun, int index, uint sectorSize)
    {
        var size = (p.LastLBA - p.FirstLBA + 1) * sectorSize;
        return new PartitionRow(
            lun,
            index,
            p.GetName().TrimEnd('\0'),
            p.FirstLBA,
            p.LastLBA,
            size,
            p.TypeGUID.ToString());
    }
}