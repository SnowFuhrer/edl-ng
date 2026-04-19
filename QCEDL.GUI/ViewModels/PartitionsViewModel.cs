using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Threading;
using QCEDL.CLI.Helpers;
using QCEDL.GUI.Services;
using QCEDL.GUI.Views;
using QCEDL.NET.PartitionTable;
using ReactiveUI;

namespace QCEDL.GUI.ViewModels;

public sealed class PartitionsViewModel : ViewModelBase
{
    private readonly EdlService _service;
    private string? _lunFilter;
    private bool _isBusy;
    private string _statusKey = "Parts_StatusNotScanned";
    private object?[] _statusArgs = [];

    // Operation state
    private string? _opPartitionName;
    private string? _opLun;
    private string _opStatus = string.Empty;
    private long _opBytesDone;
    private long _opBytesTotal;
    private bool _isOpRunning;
    private PartitionRow? _selectedRow;

    public PartitionsViewModel(EdlService service)
    {
        _service = service;

        Localizer.Instance.CultureChanged += (_, _) =>
        {
            this.RaisePropertyChanged(nameof(StatusText));
            this.RaisePropertyChanged(nameof(ProgressText));
        };
    }

    public ObservableCollection<PartitionRow> Partitions { get; } = [];

    public string? LunFilter
    {
        get => _lunFilter;
        set => this.RaiseAndSetIfChanged(ref _lunFilter, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isBusy, value);
            this.RaisePropertyChanged(nameof(CanInteract));
        }
    }

    public bool IsOpRunning
    {
        get => _isOpRunning;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isOpRunning, value);
            this.RaisePropertyChanged(nameof(CanInteract));
        }
    }

    public bool CanInteract => !_isBusy && !_isOpRunning;

    public string StatusText => _statusArgs.Length == 0
        ? Localizer.Instance[_statusKey]
        : Localizer.Instance.Format(_statusKey, _statusArgs);

    public string? OpPartitionName
    {
        get => _opPartitionName;
        set => this.RaiseAndSetIfChanged(ref _opPartitionName, value);
    }

    public string? OpLun
    {
        get => _opLun;
        set => this.RaiseAndSetIfChanged(ref _opLun, value);
    }

    public string OpStatus
    {
        get => _opStatus;
        private set => this.RaiseAndSetIfChanged(ref _opStatus, value);
    }

    public long OpBytesDone
    {
        get => _opBytesDone;
        private set
        {
            this.RaiseAndSetIfChanged(ref _opBytesDone, value);
            this.RaisePropertyChanged(nameof(ProgressPercent));
            this.RaisePropertyChanged(nameof(ProgressText));
        }
    }

    public long OpBytesTotal
    {
        get => _opBytesTotal;
        private set
        {
            this.RaiseAndSetIfChanged(ref _opBytesTotal, value);
            this.RaisePropertyChanged(nameof(ProgressPercent));
            this.RaisePropertyChanged(nameof(ProgressText));
        }
    }

    public double ProgressPercent => _opBytesTotal > 0
        ? Math.Min(100.0, 100.0 * _opBytesDone / _opBytesTotal)
        : 0;

    public string ProgressText => _opBytesTotal <= 0
        ? string.Empty
        : Localizer.Instance.Format(
            "Progress_Format",
            _opBytesDone / (1024.0 * 1024.0),
            _opBytesTotal / (1024.0 * 1024.0),
            ProgressPercent);

    public PartitionRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedRow, value);
            if (value is not null)
            {
                OpPartitionName = value.Name;
                OpLun = value.Lun.ToString(CultureInfo.InvariantCulture);
            }
        }
    }

    private void SetStatus(string key, params object?[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        this.RaisePropertyChanged(nameof(StatusText));
    }

    private static uint? ParseLun(string? s) =>
        !string.IsNullOrWhiteSpace(s) && uint.TryParse(s.Trim(), out var v) ? v : null;

    public async Task ScanAsync()
    {
        if (IsBusy || IsOpRunning)
        {
            return;
        }

        IsBusy = true;
        Partitions.Clear();
        try
        {
            var lunParam = ParseLun(LunFilter);

            await _service.RunExclusiveAsync(async m =>
            {
                var luns = await m.DetermineLunsToScanAsync(lunParam).ConfigureAwait(false);
                foreach (var lun in luns)
                {
                    try
                    {
                        var effectiveLun = m.IsDirectMode ? 0u : lun;
                        var geometry = await m.GetStorageGeometryAsync(effectiveLun).ConfigureAwait(false);
                        var data = await m.ReadSectorsAsync(effectiveLun, 0, 64).ConfigureAwait(false);
                        using var stream = new MemoryStream(data);
                        var gpt = Gpt.ReadFromStream(stream, (int)geometry.SectorSize);
                        if (gpt is null)
                        {
                            Logging.Log(Localizer.Instance.Format("Parts_LogNoGptFormat", lun), LogLevel.Warning);
                            continue;
                        }

                        for (var i = 0; i < gpt.Partitions.Count; i++)
                        {
                            var p = gpt.Partitions[i];
                            if (p.FirstLBA == 0 && p.LastLBA == 0)
                            {
                                continue;
                            }
                            var row = PartitionRow.From(p, effectiveLun, i, geometry.SectorSize);
                            await Dispatcher.UIThread.InvokeAsync(() => Partitions.Add(row));
                        }
                    }
                    catch (Exception ex)
                    {
                        Logging.Log(Localizer.Instance.Format("Parts_LogScanErrorFormat", lun, ex.Message), LogLevel.Warning);
                    }

                    if (m.IsDirectMode)
                    {
                        break;
                    }
                }
            }).ConfigureAwait(false);

            SetStatus("Parts_StatusFoundFormat", Partitions.Count);
        }
        catch (Exception ex)
        {
            Logging.Log(Localizer.Instance.Format("Parts_LogScanFailedFormat", ex.Message), LogLevel.Error);
            SetStatus("Parts_ErrorFormat", ex.Message);
        }
        finally { IsBusy = false; }
    }

    // ─── Operations ───────────────────────────────────────────────────────────

    private static long ClampToLong(ulong value) => value > long.MaxValue ? long.MaxValue : (long)value;

    public async Task ReadPartitionAsync(string savePath)
    {
        var name = OpPartitionName?.Trim();
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(savePath))
        {
            return;
        }

        await RunOpAsync("Parts_Ops_StatusReadingFormat", name, async () =>
        {
            var lun = ParseLun(OpLun);
            var found = await _service.RunExclusiveAsync(m => m.FindPartitionWithLunAsync(name, lun))
                .ConfigureAwait(false) ?? throw new InvalidOperationException(
                    Localizer.Instance.Format("Parts_Ops_PartitionNotFoundFormat", name));

            var (partition, actualLun) = found;
            var sectorCount = partition.LastLBA - partition.FirstLBA + 1;
            var geometry = await _service.GetGeometryAsync(actualLun).ConfigureAwait(false);
            ResetProgress(ClampToLong(sectorCount * geometry.SectorSize));

            var dir = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await using var file = File.Open(savePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await _service.RunExclusiveAsync(m => m.ReadSectorsToStreamAsync(
                actualLun, partition.FirstLBA, sectorCount, file, ReportProgress))
                .ConfigureAwait(false);
        });
    }

    public async Task WritePartitionAsync(Window owner, string inputPath)
    {
        var name = OpPartitionName?.Trim();
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(inputPath) || !File.Exists(inputPath))
        {
            return;
        }

        var info = new FileInfo(inputPath);
        var confirmed = await ConfirmDialog.ShowAsync(
            owner,
            Localizer.Instance["Parts_Ops_ConfirmWriteTitle"],
            Localizer.Instance.Format("Parts_Ops_ConfirmWriteMessageFormat", name, info.FullName, info.Length),
            danger: true).ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        await RunOpAsync("Parts_Ops_StatusWritingFormat", name, async () =>
        {
            var lun = ParseLun(OpLun);
            var found = await _service.RunExclusiveAsync(m => m.FindPartitionWithLunAsync(name, lun))
                .ConfigureAwait(false) ?? throw new InvalidOperationException(
                    Localizer.Instance.Format("Parts_Ops_PartitionNotFoundFormat", name));

            var (partition, actualLun) = found;
            var geometry = await _service.GetGeometryAsync(actualLun).ConfigureAwait(false);
            var partitionBytes = (partition.LastLBA - partition.FirstLBA + 1) * geometry.SectorSize;
            if ((ulong)info.Length > partitionBytes)
            {
                throw new InvalidOperationException(
                    $"Input file ({info.Length} bytes) exceeds partition size ({partitionBytes} bytes).");
            }

            ResetProgress(info.Length);

            await _service.RunExclusiveAsync(async m =>
            {
                await using var stream = info.OpenRead();
                await m.WriteSectorsFromStreamAsync(
                    actualLun,
                    partition.FirstLBA,
                    stream,
                    stream.Length,
                    padToSector: true,
                    info.Name,
                    ReportProgress).ConfigureAwait(false);
            }).ConfigureAwait(false);
        });
    }

    public async Task ErasePartitionAsync(Window owner)
    {
        var name = OpPartitionName?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        var confirmed = await ConfirmDialog.ShowAsync(
            owner,
            Localizer.Instance["Parts_Ops_ConfirmEraseTitle"],
            Localizer.Instance.Format("Parts_Ops_ConfirmEraseMessageFormat", name),
            danger: true,
            requiredConfirmation: name).ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        await RunOpAsync("Parts_Ops_StatusErasingFormat", name, async () =>
        {
            var lun = ParseLun(OpLun);
            var found = await _service.RunExclusiveAsync(m => m.FindPartitionWithLunAsync(name, lun))
                .ConfigureAwait(false) ?? throw new InvalidOperationException(
                    Localizer.Instance.Format("Parts_Ops_PartitionNotFoundFormat", name));

            var (partition, actualLun) = found;
            var sectorCount = partition.LastLBA - partition.FirstLBA + 1;
            var geometry = await _service.GetGeometryAsync(actualLun).ConfigureAwait(false);
            ResetProgress(ClampToLong(sectorCount * geometry.SectorSize));

            await _service.RunExclusiveAsync(m => m.EraseSectorsAsync(
                actualLun, partition.FirstLBA, sectorCount, ReportProgress))
                .ConfigureAwait(false);
        });
    }

    private void ResetProgress(long total)
    {
        Dispatcher.UIThread.Post(() =>
        {
            OpBytesDone = 0;
            OpBytesTotal = total;
        });
    }

    private void ReportProgress(long done, long total)
    {
        Dispatcher.UIThread.Post(() =>
        {
            OpBytesDone = done;
            if (total > 0)
            {
                OpBytesTotal = total;
            }
        });
    }

    private async Task RunOpAsync(string statusFormatKey, string label, Func<Task> body)
    {
        if (IsBusy || IsOpRunning)
        {
            return;
        }

        IsOpRunning = true;
        OpStatus = Localizer.Instance.Format(statusFormatKey, label);
        var sw = Stopwatch.StartNew();
        try
        {
            await body().ConfigureAwait(true);
            sw.Stop();
            OpStatus = Localizer.Instance.Format("Parts_Ops_StatusDoneFormat", label, sw.Elapsed.TotalSeconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            OpStatus = Localizer.Instance.Format("Parts_Ops_StatusFailedFormat", ex.Message);
            Logging.Log($"Partition op failed: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            IsOpRunning = false;
        }
    }
}