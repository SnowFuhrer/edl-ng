using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using QCEDL.GUI.Services;
using QCEDL.GUI.ViewModels;

namespace QCEDL.GUI.Views;

public partial class AdvancedView : UserControl
{
    public AdvancedView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private Window? GetOwner() => TopLevel.GetTopLevel(this) as Window;

    private async void OnBrowseProvision(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AdvancedViewModel vm)
        {
            return;
        }
        var top = TopLevel.GetTopLevel(this);
        if (top is null)
        {
            return;
        }
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Localizer.Instance["Adv_ProvisionPickTitle"],
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("XML") { Patterns = ["*.xml"] }
            ],
        });
        var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (!string.IsNullOrEmpty(path))
        {
            vm.ProvisionXmlPath = path;
        }
    }

    private async void OnProvision(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AdvancedViewModel vm || GetOwner() is not { } owner)
        {
            return;
        }
        await vm.RunProvisionAsync(owner);
    }

    private async void OnUpload(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AdvancedViewModel vm)
        {
            return;
        }
        await vm.RunUploadLoaderAsync();
    }

    private async void OnReset(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AdvancedViewModel vm || GetOwner() is not { } owner)
        {
            return;
        }
        await vm.RunResetAsync(owner);
    }
}