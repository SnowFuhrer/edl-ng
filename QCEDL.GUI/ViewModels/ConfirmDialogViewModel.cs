using QCEDL.GUI.Services;
using ReactiveUI;

namespace QCEDL.GUI.ViewModels;

public sealed class ConfirmDialogViewModel : ViewModelBase
{
    private string _typedConfirmation = string.Empty;

    public ConfirmDialogViewModel(
        string title,
        string message,
        string confirmLabel,
        string cancelLabel,
        bool danger,
        string? requiredConfirmation)
    {
        Title = title;
        Message = message;
        ConfirmLabel = confirmLabel;
        CancelLabel = cancelLabel;
        Danger = danger;
        RequiredConfirmation = requiredConfirmation;
        RequiresTyping = !string.IsNullOrEmpty(requiredConfirmation);
        TypeToConfirmPrompt = RequiresTyping
            ? Localizer.Instance.Format("Confirm_TypeToConfirmFormat", requiredConfirmation)
            : string.Empty;
    }

    public string Title { get; }
    public string Message { get; }
    public string ConfirmLabel { get; }
    public string CancelLabel { get; }
    public bool Danger { get; }
    public string? RequiredConfirmation { get; }
    public bool RequiresTyping { get; }
    public string TypeToConfirmPrompt { get; }

    public string TypedConfirmation
    {
        get => _typedConfirmation;
        set
        {
            this.RaiseAndSetIfChanged(ref _typedConfirmation, value);
            this.RaisePropertyChanged(nameof(CanConfirm));
        }
    }

    public bool CanConfirm => !RequiresTyping ||
        string.Equals(_typedConfirmation, RequiredConfirmation, StringComparison.Ordinal);
}