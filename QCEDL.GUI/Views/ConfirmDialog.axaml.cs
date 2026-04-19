using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using QCEDL.GUI.Services;
using QCEDL.GUI.ViewModels;

namespace QCEDL.GUI.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ConfirmDialogViewModel vm && !vm.CanConfirm)
        {
            return;
        }
        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    /// <summary>
    /// Show a confirmation dialog modally on <paramref name="owner"/>. Returns true iff the
    /// user clicked the confirm action (and typed the required confirmation string if one
    /// was supplied).
    /// </summary>
    public static async Task<bool> ShowAsync(
        Window owner,
        string title,
        string message,
        bool danger = false,
        string? requiredConfirmation = null,
        string? confirmLabel = null,
        string? cancelLabel = null)
    {
        var vm = new ConfirmDialogViewModel(
            title,
            message,
            confirmLabel ?? Localizer.Instance["Confirm_DefaultOk"],
            cancelLabel ?? Localizer.Instance["Confirm_DefaultCancel"],
            danger,
            requiredConfirmation);

        var dialog = new ConfirmDialog { DataContext = vm };
        var result = await dialog.ShowDialog<bool?>(owner);
        return result == true;
    }
}