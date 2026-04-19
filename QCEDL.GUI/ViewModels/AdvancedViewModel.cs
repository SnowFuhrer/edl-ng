using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using QCEDL.CLI.Core;
using QCEDL.CLI.Helpers;
using QCEDL.GUI.Services;
using QCEDL.GUI.Views;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;
using ReactiveUI;

namespace QCEDL.GUI.ViewModels;

public sealed class AdvancedViewModel : ViewModelBase
{
    private readonly EdlService _service;

    // Provision
    private string? _provisionXmlPath;
    private string _provisionStatus = string.Empty;
    private bool _isProvisionBusy;

    // Upload loader
    private string _uploadStatus = string.Empty;
    private bool _isUploadBusy;

    // Reset
    private PowerValue _resetMode = PowerValue.Reset;
    private string _resetDelay = "1";
    private string _resetStatus = string.Empty;
    private bool _isResetBusy;

    public AdvancedViewModel(EdlService service)
    {
        _service = service;
        ResetModes = Enum.GetValues<PowerValue>();
    }

    public IReadOnlyList<PowerValue> ResetModes { get; }

    public bool CanInteract => !_isProvisionBusy && !_isUploadBusy && !_isResetBusy;

    public string? ProvisionXmlPath
    {
        get => _provisionXmlPath;
        set => this.RaiseAndSetIfChanged(ref _provisionXmlPath, value);
    }

    public string ProvisionStatus
    {
        get => _provisionStatus;
        private set => this.RaiseAndSetIfChanged(ref _provisionStatus, value);
    }

    public bool IsProvisionBusy
    {
        get => _isProvisionBusy;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isProvisionBusy, value);
            this.RaisePropertyChanged(nameof(CanInteract));
        }
    }

    public string UploadStatus
    {
        get => _uploadStatus;
        private set => this.RaiseAndSetIfChanged(ref _uploadStatus, value);
    }

    public bool IsUploadBusy
    {
        get => _isUploadBusy;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isUploadBusy, value);
            this.RaisePropertyChanged(nameof(CanInteract));
        }
    }

    public PowerValue ResetMode
    {
        get => _resetMode;
        set => this.RaiseAndSetIfChanged(ref _resetMode, value);
    }

    public string ResetDelay
    {
        get => _resetDelay;
        set => this.RaiseAndSetIfChanged(ref _resetDelay, value);
    }

    public string ResetStatus
    {
        get => _resetStatus;
        private set => this.RaiseAndSetIfChanged(ref _resetStatus, value);
    }

    public bool IsResetBusy
    {
        get => _isResetBusy;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isResetBusy, value);
            this.RaisePropertyChanged(nameof(CanInteract));
        }
    }

    public async Task RunProvisionAsync(Window owner)
    {
        if (string.IsNullOrWhiteSpace(ProvisionXmlPath) || !File.Exists(ProvisionXmlPath))
        {
            ProvisionStatus = Localizer.Instance["Adv_ProvisionNoFile"];
            return;
        }

        var confirmed = await ConfirmDialog.ShowAsync(
            owner,
            Localizer.Instance["Adv_ConfirmProvisionTitle"],
            Localizer.Instance.Format("Adv_ConfirmProvisionMessageFormat", ProvisionXmlPath),
            danger: true,
            requiredConfirmation: "PROVISION").ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        await RunAsync(
            v => IsProvisionBusy = v,
            s => ProvisionStatus = s,
            "Adv_ProvisionRunning",
            "Adv_ProvisionDoneFormat",
            async () =>
            {
                var file = new FileInfo(ProvisionXmlPath);
                await _service.RunExclusiveAsync(m => ProvisionRunner.RunAsync(m, file)).ConfigureAwait(false);
            });
    }

    public async Task RunUploadLoaderAsync()
    {
        if (string.IsNullOrWhiteSpace(_service.Options.LoaderPath))
        {
            UploadStatus = Localizer.Instance["Adv_UploadNoLoader"];
            return;
        }

        await RunAsync(
            v => IsUploadBusy = v,
            s => UploadStatus = s,
            "Adv_UploadRunning",
            "Adv_UploadDoneFormat",
            () => _service.RunExclusiveAsync(async m =>
            {
                var mode = await m.DetectCurrentModeAsync().ConfigureAwait(false);
                if (mode != DeviceMode.Sahara)
                {
                    throw new InvalidOperationException(
                        Localizer.Instance.Format("Adv_UploadWrongModeFormat", mode));
                }
                await m.UploadLoaderViaSaharaAsync().ConfigureAwait(false);
            }));
    }

    public async Task RunResetAsync(Window owner)
    {
        if (!uint.TryParse(ResetDelay.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var delay))
        {
            ResetStatus = Localizer.Instance.Format("Adv_ResetBadDelayFormat", ResetDelay);
            return;
        }

        var confirmed = await ConfirmDialog.ShowAsync(
            owner,
            Localizer.Instance["Adv_ConfirmResetTitle"],
            Localizer.Instance.Format("Adv_ConfirmResetMessageFormat", ResetMode, delay),
            danger: true).ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        await RunAsync(
            v => IsResetBusy = v,
            s => ResetStatus = s,
            "Adv_ResetRunning",
            "Adv_ResetDoneFormat",
            () => _service.RunExclusiveAsync(async m =>
            {
                await m.EnsureFirehoseModeAsync().ConfigureAwait(false);
                var ok = await Task.Run(() => m.Firehose.Reset(ResetMode, delay)).ConfigureAwait(false);
                if (!ok)
                {
                    throw new InvalidOperationException(
                        Localizer.Instance.Format("Adv_ResetFailedFormat", ResetMode));
                }
            }));
    }

    private async Task RunAsync(
        Action<bool> setBusy,
        Action<string> setStatus,
        string runningKey,
        string doneFormat,
        Func<Task> body)
    {
        if (!CanInteract)
        {
            return;
        }
        setBusy(true);
        setStatus(Localizer.Instance[runningKey]);
        var sw = Stopwatch.StartNew();
        try
        {
            await body().ConfigureAwait(true);
            sw.Stop();
            setStatus(Localizer.Instance.Format(doneFormat, sw.Elapsed.TotalSeconds));
        }
        catch (Exception ex)
        {
            sw.Stop();
            setStatus(Localizer.Instance.Format("Adv_ErrorFormat", ex.Message));
            Logging.Log($"Advanced op failed: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            setBusy(false);
        }
    }
}