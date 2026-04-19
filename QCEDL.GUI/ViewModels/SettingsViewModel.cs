using System.Globalization;
using QCEDL.GUI.Services;
using Qualcomm.EmergencyDownload.Helpers;
using ReactiveUI;

namespace QCEDL.GUI.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private CultureInfo _selectedLanguage;
    private LogLevel _selectedLogLevel;

    public SettingsViewModel()
    {
        _selectedLanguage = Localizer.Instance.Culture;
        _selectedLogLevel = Logging.CurrentLogLevel;

        // Keep selection synced if culture is changed elsewhere (e.g. from startup defaults).
        Localizer.Instance.CultureChanged += (_, _) =>
        {
            if (!Equals(_selectedLanguage, Localizer.Instance.Culture))
            {
                _selectedLanguage = Localizer.Instance.Culture;
                this.RaisePropertyChanged(nameof(SelectedLanguage));
            }
        };
    }

    public IReadOnlyList<CultureInfo> Languages { get; } = Localizer.SupportedCultures;
    public IReadOnlyList<LogLevel> LogLevels { get; } = Enum.GetValues<LogLevel>();

    public CultureInfo SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (value is null || Equals(_selectedLanguage, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedLanguage, value);
            Localizer.Instance.Culture = value;
            GuiSettings.Current.Culture = value.Name;
            GuiSettings.Save();
        }
    }

    public LogLevel SelectedLogLevel
    {
        get => _selectedLogLevel;
        set
        {
            if (_selectedLogLevel == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedLogLevel, value);
            Logging.CurrentLogLevel = value;
            GuiSettings.Current.LogLevel = value.ToString();
            GuiSettings.Save();
        }
    }
}