using System.Reactive.Linq;
using System.Runtime.InteropServices;
using QCEDL.CLI.Core;
using QCEDL.CLI.Helpers;
using QCEDL.GUI.Services;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace QCEDL.GUI.ViewModels;

public sealed partial class ConnectionViewModel : ViewModelBase
{
    private readonly EdlService _service;
    private readonly IObservable<bool> _canRun;

    [Reactive] private string? _loaderPath;
    [Reactive] private string _vidHex;
    [Reactive] private string _pidHex;
    [Reactive] private StorageType _memoryType;
    [Reactive] private uint _slot;
    [Reactive] private string? _hostDevTarget;
    [Reactive] private string? _imgSize;
    [Reactive] private bool _radxaWos;
    [Reactive] private string? _serialDevicePath;
    [Reactive] private bool _isBusy;

    private TransportBackend _backend;
    private string _statusKey = "Conn_StatusNotConnected";
    private object?[] _statusArgs = [];
    private string _modeKey = "Conn_ModeUnknown";
    private string? _modeLiteral;

    public ConnectionViewModel(EdlService service)
    {
        _service = service;
        _canRun = this.WhenAnyValue(x => x.IsBusy).Select(b => !b);

        MemoryTypes = Enum.GetValues<StorageType>();
        Backends = Enum.GetValues<TransportBackend>();

        // Seed from persisted values so the user's last target stays around across launches.
        var settings = GuiSettings.Current;
        _loaderPath = settings.LoaderPath;
        _vidHex = string.IsNullOrWhiteSpace(settings.VidHex) ? "05C6" : settings.VidHex;
        _pidHex = string.IsNullOrWhiteSpace(settings.PidHex) ? "9008" : settings.PidHex;
        _memoryType = Enum.TryParse<StorageType>(settings.MemoryType, ignoreCase: true, out var mt) ? mt : StorageType.Ufs;
        _backend = Enum.TryParse<TransportBackend>(settings.Backend, ignoreCase: true, out var be) ? be : TransportBackend.Auto;

        // Localized wording for the two hot paths: keep the explicit hook so the Conn_* keys drive the message.
        ConnectCommand.ThrownExceptions.Subscribe(ex =>
            Logging.Log(Localizer.Instance.Format("Conn_ConnectFailedFormat", ex.Message), LogLevel.Error));
        ProbeCommand.ThrownExceptions.Subscribe(ex =>
            Logging.Log(Localizer.Instance.Format("Conn_ProbeFailedFormat", ex.Message), LogLevel.Error));

        LogCommandErrors();

        Localizer.Instance.CultureChanged += (_, _) =>
        {
            this.RaisePropertyChanged(nameof(StatusText));
            this.RaisePropertyChanged(nameof(ModeText));
        };
    }

    public IReadOnlyList<StorageType> MemoryTypes { get; }
    public IReadOnlyList<TransportBackend> Backends { get; }

    public bool IsWindows { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public bool IsUnix { get; } = !RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public TransportBackend Backend
    {
        get => _backend;
        set
        {
            this.RaiseAndSetIfChanged(ref _backend, value);
            this.RaisePropertyChanged(nameof(IsSerialBackend));
            this.RaisePropertyChanged(nameof(IsUsbBackend));
        }
    }

    public bool IsSerialBackend => _backend == TransportBackend.Serial;
    public bool IsUsbBackend => _backend != TransportBackend.Serial;

    public string StatusText => _statusArgs.Length == 0
        ? Localizer.Instance[_statusKey]
        : Localizer.Instance.Format(_statusKey, _statusArgs);

    public string ModeText => _modeLiteral ?? Localizer.Instance[_modeKey];

    public Interaction<OpenFileRequest, IReadOnlyList<string>> PickFile { get; } = new();

    private void SetStatus(string key, params object?[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        this.RaisePropertyChanged(nameof(StatusText));
    }

    private void SetModeKey(string key)
    {
        _modeKey = key;
        _modeLiteral = null;
        this.RaisePropertyChanged(nameof(ModeText));
    }

    private void SetModeLiteral(string literal)
    {
        _modeLiteral = literal;
        this.RaisePropertyChanged(nameof(ModeText));
    }

    private void ApplyOptions()
    {
        _service.Options = new EdlOptions
        {
            LoaderPath = LoaderPath,
            Vid = TryParseHex(VidHex),
            Pid = TryParseHex(PidHex),
            MemoryType = MemoryType,
            LogLevel = Logging.CurrentLogLevel,
            Slot = Slot,
            HostDevAsTarget = string.IsNullOrWhiteSpace(HostDevTarget) ? null : HostDevTarget,
            ImgSize = string.IsNullOrWhiteSpace(ImgSize) ? null : ImgSize,
            RadxaWosPlatform = RadxaWos && IsWindows,
            Backend = Backend,
            SerialDevicePath = Backend == TransportBackend.Serial && !string.IsNullOrWhiteSpace(SerialDevicePath)
                ? SerialDevicePath
                : null,
        };
        _service.ResetSession();

        // Persist last-used options so the next launch defaults to what was actually tried.
        var prefs = GuiSettings.Current;
        prefs.LoaderPath = LoaderPath;
        prefs.VidHex = VidHex;
        prefs.PidHex = PidHex;
        prefs.MemoryType = MemoryType.ToString();
        prefs.Backend = Backend.ToString();
        GuiSettings.Save();
    }

    [ReactiveCommand(CanExecute = nameof(_canRun))]
    private async Task ProbeAsync()
    {
        IsBusy = true;
        try
        {
            ApplyOptions();
            Logging.Log(Localizer.Instance["Conn_LogProbing"], LogLevel.Info);
            var mode = await _service.ProbeAsync().ConfigureAwait(false);
            SetModeLiteral(mode.ToString());
            SetStatus("Conn_StatusDetectedFormat", mode);
        }
        finally { IsBusy = false; }
    }

    [ReactiveCommand(CanExecute = nameof(_canRun))]
    private async Task ConnectAsync()
    {
        IsBusy = true;
        try
        {
            ApplyOptions();
            if (_service.Options.HostDevAsTarget != null || _service.Options.RadxaWosPlatform)
            {
                Logging.Log(Localizer.Instance["Conn_LogDirectSession"], LogLevel.Info);
                SetStatus("Conn_StatusDirectReady");
                SetModeKey("Conn_ModeDirect");
                return;
            }

            Logging.Log(Localizer.Instance["Conn_LogConnecting"], LogLevel.Info);
            await _service.EnsureFirehoseAsync().ConfigureAwait(false);
            SetModeLiteral(_service.CurrentMode.ToString());
            SetStatus("Conn_StatusFirehoseConnected");
        }
        finally { IsBusy = false; }
    }

    [ReactiveCommand(CanExecute = nameof(_canRun))]
    private void Disconnect()
    {
        _service.ResetSession();
        SetStatus("Conn_StatusDisconnected");
        SetModeKey("Conn_ModeUnknown");
        Logging.Log(Localizer.Instance["Conn_LogSessionClosed"], LogLevel.Info);
    }

    [ReactiveCommand]
    private async Task BrowseLoaderAsync()
    {
        var paths = await PickFile.Handle(new OpenFileRequest(
            Localizer.Instance["Conn_LoaderPickTitle"],
            [FilePickerTypes.FirehoseLoader, FilePickerTypes.AnyFile],
            AllowMultiple: false));
        if (paths.Count > 0 && !string.IsNullOrEmpty(paths[0]))
        {
            LoaderPath = paths[0];
        }
    }

    private static int? TryParseHex(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return null;
        }
        var trimmed = s.Trim().Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase);
        return int.TryParse(trimmed, System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : null;
    }
}