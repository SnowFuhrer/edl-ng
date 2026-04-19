using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Threading;
using QCEDL.CLI.Core;
using QCEDL.CLI.Helpers;
using QCEDL.GUI.Services;
using QCEDL.GUI.Views;
using ReactiveUI;

namespace QCEDL.GUI.ViewModels;

public sealed class RawProgramViewModel : ViewModelBase
{
    private readonly EdlService _service;

    // Execute card
    private string _execStatus = string.Empty;
    private bool _isExecBusy;
    private long _execBytesDone;
    private long _execBytesTotal;
    private string _execProgressLabel = string.Empty;
    private int _execProgramIndex;
    private int _execProgramCount;

    // Dump card
    private string _dumpLun = "0";
    private string? _dumpOutputDir;
    private bool _dumpGenXmlOnly;
    private string? _dumpSkip;
    private string _dumpStatus = string.Empty;
    private bool _isDumpBusy;
    private long _dumpBytesDone;
    private long _dumpBytesTotal;
    private string _dumpProgressLabel = string.Empty;
    private int _dumpPartitionIndex;
    private int _dumpPartitionCount;

    public RawProgramViewModel(EdlService service)
    {
        _service = service;
        Localizer.Instance.CultureChanged += (_, _) =>
        {
            this.RaisePropertyChanged(nameof(ExecProgressText));
            this.RaisePropertyChanged(nameof(DumpProgressText));
        };
        XmlFiles.CollectionChanged += (_, _) => this.RaisePropertyChanged(nameof(HasXmlFiles));
    }

    public ObservableCollection<string> XmlFiles { get; } = [];

    public bool HasXmlFiles => XmlFiles.Count > 0;

    // Execute bindings
    public string ExecStatus
    {
        get => _execStatus;
        private set => this.RaiseAndSetIfChanged(ref _execStatus, value);
    }

    public bool IsExecBusy
    {
        get => _isExecBusy;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isExecBusy, value);
            this.RaisePropertyChanged(nameof(CanInteract));
        }
    }

    public long ExecBytesDone
    {
        get => _execBytesDone;
        private set
        {
            this.RaiseAndSetIfChanged(ref _execBytesDone, value);
            this.RaisePropertyChanged(nameof(ExecProgressPercent));
            this.RaisePropertyChanged(nameof(ExecProgressText));
        }
    }

    public long ExecBytesTotal
    {
        get => _execBytesTotal;
        private set
        {
            this.RaiseAndSetIfChanged(ref _execBytesTotal, value);
            this.RaisePropertyChanged(nameof(ExecProgressPercent));
            this.RaisePropertyChanged(nameof(ExecProgressText));
        }
    }

    public double ExecProgressPercent => _execBytesTotal > 0
        ? Math.Min(100.0, 100.0 * _execBytesDone / _execBytesTotal)
        : 0;

    public string ExecProgressText
    {
        get
        {
            if (_execBytesTotal <= 0)
            {
                return string.Empty;
            }
            var formatted = Localizer.Instance.Format(
                "Progress_Format",
                _execBytesDone / (1024.0 * 1024.0),
                _execBytesTotal / (1024.0 * 1024.0),
                ExecProgressPercent);
            return string.IsNullOrEmpty(_execProgressLabel)
                ? formatted
                : $"[{_execProgramIndex}/{_execProgramCount}] {_execProgressLabel} — {formatted}";
        }
    }

    // Dump bindings
    public string DumpLun
    {
        get => _dumpLun;
        set => this.RaiseAndSetIfChanged(ref _dumpLun, value);
    }

    public string? DumpOutputDir
    {
        get => _dumpOutputDir;
        set => this.RaiseAndSetIfChanged(ref _dumpOutputDir, value);
    }

    public bool DumpGenXmlOnly
    {
        get => _dumpGenXmlOnly;
        set => this.RaiseAndSetIfChanged(ref _dumpGenXmlOnly, value);
    }

    public string? DumpSkip
    {
        get => _dumpSkip;
        set => this.RaiseAndSetIfChanged(ref _dumpSkip, value);
    }

    public string DumpStatus
    {
        get => _dumpStatus;
        private set => this.RaiseAndSetIfChanged(ref _dumpStatus, value);
    }

    public bool IsDumpBusy
    {
        get => _isDumpBusy;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isDumpBusy, value);
            this.RaisePropertyChanged(nameof(CanInteract));
        }
    }

    public long DumpBytesDone
    {
        get => _dumpBytesDone;
        private set
        {
            this.RaiseAndSetIfChanged(ref _dumpBytesDone, value);
            this.RaisePropertyChanged(nameof(DumpProgressPercent));
            this.RaisePropertyChanged(nameof(DumpProgressText));
        }
    }

    public long DumpBytesTotal
    {
        get => _dumpBytesTotal;
        private set
        {
            this.RaiseAndSetIfChanged(ref _dumpBytesTotal, value);
            this.RaisePropertyChanged(nameof(DumpProgressPercent));
            this.RaisePropertyChanged(nameof(DumpProgressText));
        }
    }

    public double DumpProgressPercent => _dumpBytesTotal > 0
        ? Math.Min(100.0, 100.0 * _dumpBytesDone / _dumpBytesTotal)
        : 0;

    public string DumpProgressText
    {
        get
        {
            if (_dumpBytesTotal <= 0)
            {
                return string.Empty;
            }
            var formatted = Localizer.Instance.Format(
                "Progress_Format",
                _dumpBytesDone / (1024.0 * 1024.0),
                _dumpBytesTotal / (1024.0 * 1024.0),
                DumpProgressPercent);
            return string.IsNullOrEmpty(_dumpProgressLabel)
                ? formatted
                : $"[{_dumpPartitionIndex}/{_dumpPartitionCount}] {_dumpProgressLabel} — {formatted}";
        }
    }

    public bool CanInteract => !_isExecBusy && !_isDumpBusy;

    public void AddXmlFiles(IEnumerable<string> paths)
    {
        foreach (var p in paths)
        {
            if (!XmlFiles.Contains(p))
            {
                XmlFiles.Add(p);
            }
        }
    }

    public void RemoveXmlFile(string path) => XmlFiles.Remove(path);

    public void ClearXmlFiles() => XmlFiles.Clear();

    public async Task RunRawProgramAsync(Window owner)
    {
        if (XmlFiles.Count == 0)
        {
            ExecStatus = Localizer.Instance["Raw_ExecNoFiles"];
            return;
        }

        var files = RawProgramRunner.ResolveXmlFiles(XmlFiles);
        if (files.Count == 0)
        {
            ExecStatus = Localizer.Instance["Raw_ExecNoFiles"];
            return;
        }

        var grouped = RawProgramRunner.GroupByLun(files);
        if (grouped.RawProgramByLun.Count == 0)
        {
            ExecStatus = Localizer.Instance["Raw_ExecNoRawFiles"];
            return;
        }

        var summary = string.Join(", ",
            grouped.RawProgramByLun.OrderBy(kv => kv.Key).Select(kv => $"LUN {kv.Key}: {kv.Value.Name}"));
        var confirmed = await ConfirmDialog.ShowAsync(
            owner,
            Localizer.Instance["Raw_ConfirmExecTitle"],
            Localizer.Instance.Format("Raw_ConfirmExecMessageFormat", summary),
            danger: true,
            requiredConfirmation: "FLASH").ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        await RunExecAsync(() =>
            _service.RunExclusiveAsync(m => RawProgramRunner.RunAsync(m, grouped, OnExecProgress)));
    }

    public async Task RunDumpAsync()
    {
        if (string.IsNullOrWhiteSpace(DumpOutputDir))
        {
            DumpStatus = Localizer.Instance["Raw_DumpNoDir"];
            return;
        }

        await RunDumpAsyncInternal(async () =>
        {
            if (!uint.TryParse(DumpLun.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var lun))
            {
                throw new FormatException($"Invalid LUN: '{DumpLun}'");
            }

            var skipSet = new HashSet<string>(
                (DumpSkip ?? string.Empty).Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.OrdinalIgnoreCase);

            var dir = new DirectoryInfo(DumpOutputDir);

            await _service.RunExclusiveAsync(m => DumpRawprogramRunner.RunAsync(
                m, dir, lun, DumpGenXmlOnly, skipSet, OnDumpProgress)).ConfigureAwait(false);
        });
    }

    private void OnExecProgress(RawProgramRunner.RawProgramProgress p) => Dispatcher.UIThread.Post(() =>
    {
        _execProgressLabel = $"{p.Label} ({p.Filename})";
        _execProgramIndex = p.ProgramIndex;
        _execProgramCount = p.ProgramCount;
        ExecBytesDone = p.BytesDone;
        ExecBytesTotal = p.BytesTotal;
    });

    private void OnDumpProgress(DumpRawprogramRunner.DumpProgress p) => Dispatcher.UIThread.Post(() =>
    {
        _dumpProgressLabel = p.PartitionName;
        _dumpPartitionIndex = p.PartitionIndex;
        _dumpPartitionCount = p.PartitionCount;
        DumpBytesDone = p.BytesDone;
        DumpBytesTotal = p.BytesTotal;
    });

    private async Task RunExecAsync(Func<Task> body)
    {
        if (IsExecBusy)
        {
            return;
        }
        IsExecBusy = true;
        ExecStatus = Localizer.Instance["Raw_ExecRunning"];
        Dispatcher.UIThread.Post(() =>
        {
            ExecBytesDone = 0;
            ExecBytesTotal = 0;
        });
        var sw = Stopwatch.StartNew();
        try
        {
            await body().ConfigureAwait(true);
            sw.Stop();
            ExecStatus = Localizer.Instance.Format("Raw_ExecDoneFormat", sw.Elapsed.TotalSeconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            ExecStatus = Localizer.Instance.Format("Raw_ErrorFormat", ex.Message);
            Logging.Log($"rawprogram failed: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            IsExecBusy = false;
        }
    }

    private async Task RunDumpAsyncInternal(Func<Task> body)
    {
        if (IsDumpBusy)
        {
            return;
        }
        IsDumpBusy = true;
        DumpStatus = Localizer.Instance["Raw_DumpRunning"];
        Dispatcher.UIThread.Post(() =>
        {
            DumpBytesDone = 0;
            DumpBytesTotal = 0;
        });
        var sw = Stopwatch.StartNew();
        try
        {
            await body().ConfigureAwait(true);
            sw.Stop();
            DumpStatus = Localizer.Instance.Format("Raw_DumpDoneFormat", sw.Elapsed.TotalSeconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            DumpStatus = Localizer.Instance.Format("Raw_ErrorFormat", ex.Message);
            Logging.Log($"dump-rawprogram failed: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            IsDumpBusy = false;
        }
    }
}