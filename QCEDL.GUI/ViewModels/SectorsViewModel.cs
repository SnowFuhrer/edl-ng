using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;
using QCEDL.CLI.Helpers;
using QCEDL.GUI.Services;
using QCEDL.GUI.Views;
using ReactiveUI;

namespace QCEDL.GUI.ViewModels;

public sealed class SectorsViewModel : ViewModelBase
{
    private readonly EdlService _service;
    private uint _lun;
    private ulong _startLba;
    private ulong _sectorCount = 1;
    private string _status = string.Empty;
    private long _bytesDone;
    private long _bytesTotal;
    private bool _isBusy;

    public SectorsViewModel(EdlService service)
    {
        _service = service;
        Localizer.Instance.CultureChanged += (_, _) =>
            this.RaisePropertyChanged(nameof(ProgressText));
    }

    public uint Lun
    {
        get => _lun;
        set => this.RaiseAndSetIfChanged(ref _lun, value);
    }

    public ulong StartLba
    {
        get => _startLba;
        set => this.RaiseAndSetIfChanged(ref _startLba, value);
    }

    public ulong SectorCount
    {
        get => _sectorCount;
        set => this.RaiseAndSetIfChanged(ref _sectorCount, value);
    }

    public string Status
    {
        get => _status;
        private set => this.RaiseAndSetIfChanged(ref _status, value);
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

    public bool CanInteract => !_isBusy;

    public long BytesDone
    {
        get => _bytesDone;
        private set
        {
            this.RaiseAndSetIfChanged(ref _bytesDone, value);
            this.RaisePropertyChanged(nameof(ProgressPercent));
            this.RaisePropertyChanged(nameof(ProgressText));
        }
    }

    public long BytesTotal
    {
        get => _bytesTotal;
        private set
        {
            this.RaiseAndSetIfChanged(ref _bytesTotal, value);
            this.RaisePropertyChanged(nameof(ProgressPercent));
            this.RaisePropertyChanged(nameof(ProgressText));
        }
    }

    public double ProgressPercent => _bytesTotal > 0
        ? Math.Min(100.0, 100.0 * _bytesDone / _bytesTotal)
        : 0;

    public string ProgressText => _bytesTotal <= 0
        ? string.Empty
        : Localizer.Instance.Format(
            "Progress_Format",
            _bytesDone / (1024.0 * 1024.0),
            _bytesTotal / (1024.0 * 1024.0),
            ProgressPercent);

    private static long ClampToLong(ulong value) =>
        value > long.MaxValue ? long.MaxValue : (long)value;

    public async Task ReadSectorsAsync(string savePath)
    {
        await RunAsync("Sectors_StatusReadingFormat", async () =>
        {
            var lun = Lun;
            var start = StartLba;
            var count = SectorCount;

            var geometry = await _service.GetGeometryAsync(lun).ConfigureAwait(false);
            ResetProgress(ClampToLong(count * geometry.SectorSize));

            var dir = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await using var fs = File.Open(savePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await _service.RunExclusiveAsync(m => m.ReadSectorsToStreamAsync(
                lun, start, count, fs, Report)).ConfigureAwait(false);

            return count;
        });
    }

    public async Task ReadLunAsync(string savePath)
    {
        await RunAsync("Sectors_StatusReadingFormat", async () =>
        {
            var lun = Lun;
            var geometry = await _service.GetGeometryAsync(lun).ConfigureAwait(false);
            if (geometry.TotalSectors is null or 0)
            {
                throw new InvalidOperationException("Device did not report a total block count for this LUN.");
            }

            var count = geometry.TotalSectors.Value;
            ResetProgress(ClampToLong(count * geometry.SectorSize));

            var dir = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await using var fs = File.Open(savePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await _service.RunExclusiveAsync(m => m.ReadSectorsToStreamAsync(
                lun, 0, count, fs, Report)).ConfigureAwait(false);

            return count;
        });
    }

    public async Task WriteSectorsAsync(Window owner, string inputPath)
    {
        if (!File.Exists(inputPath))
        {
            return;
        }

        var info = new FileInfo(inputPath);
        var confirmed = await ConfirmDialog.ShowAsync(
            owner,
            Localizer.Instance["Sectors_ConfirmWriteTitle"],
            Localizer.Instance.Format("Sectors_ConfirmWriteMessageFormat", info.FullName, info.Length, Lun, StartLba),
            danger: true,
            requiredConfirmation: "WRITE").ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        await RunAsync("Sectors_StatusWritingFormat", async () =>
        {
            var lun = Lun;
            var start = StartLba;
            ResetProgress(info.Length);

            await _service.RunExclusiveAsync(async m =>
            {
                await using var stream = info.OpenRead();
                await m.WriteSectorsFromStreamAsync(
                    lun, start, stream, stream.Length, padToSector: true, info.Name, Report)
                    .ConfigureAwait(false);
            }).ConfigureAwait(false);

            return (ulong)info.Length;
        });
    }

    public async Task EraseSectorsAsync(Window owner)
    {
        var confirmed = await ConfirmDialog.ShowAsync(
            owner,
            Localizer.Instance["Sectors_ConfirmEraseTitle"],
            Localizer.Instance.Format("Sectors_ConfirmEraseMessageFormat", Lun, StartLba, SectorCount),
            danger: true,
            requiredConfirmation: "ERASE").ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        await RunAsync("Sectors_StatusErasingFormat", async () =>
        {
            var lun = Lun;
            var start = StartLba;
            var count = SectorCount;

            var geometry = await _service.GetGeometryAsync(lun).ConfigureAwait(false);
            ResetProgress(ClampToLong(count * geometry.SectorSize));

            await _service.RunExclusiveAsync(m => m.EraseSectorsAsync(lun, start, count, Report))
                .ConfigureAwait(false);

            return count;
        });
    }

    private void ResetProgress(long total) => Dispatcher.UIThread.Post(() =>
    {
        BytesDone = 0;
        BytesTotal = total;
    });

    private void Report(long done, long total) => Dispatcher.UIThread.Post(() =>
    {
        BytesDone = done;
        if (total > 0)
        {
            BytesTotal = total;
        }
    });

    private async Task RunAsync(string statusKey, Func<Task<ulong>> body)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        Status = Localizer.Instance.Format(statusKey, Lun, StartLba, SectorCount);
        var sw = Stopwatch.StartNew();
        try
        {
            var count = await body().ConfigureAwait(true);
            sw.Stop();
            Status = Localizer.Instance.Format("Sectors_StatusDoneFormat", count, sw.Elapsed.TotalSeconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            Status = Localizer.Instance.Format("Sectors_ErrorFormat", ex.Message);
            Logging.Log($"Sectors op failed: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }
}