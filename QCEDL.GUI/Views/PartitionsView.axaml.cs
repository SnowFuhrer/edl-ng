using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using QCEDL.GUI.Services;
using QCEDL.GUI.ViewModels;

namespace QCEDL.GUI.Views;

public partial class PartitionsView : UserControl
{
    public PartitionsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async void OnScan(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PartitionsViewModel vm)
        {
            await vm.ScanAsync();
        }
    }

    private async void OnReadPart(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PartitionsViewModel vm || GetOwner() is not { })
        {
            return;
        }

        var name = vm.OpPartitionName?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        var top = TopLevel.GetTopLevel(this);
        if (top is null)
        {
            return;
        }

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Localizer.Instance["Parts_Ops_SavePickerTitle"],
            SuggestedFileName = $"{name}.img",
            DefaultExtension = "img",
        });
        var path = file?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        await vm.ReadPartitionAsync(path);
    }

    private async void OnWritePart(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PartitionsViewModel vm || GetOwner() is not { } owner)
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
            Title = Localizer.Instance["Parts_Ops_OpenPickerTitle"],
            AllowMultiple = false,
        });
        var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        await vm.WritePartitionAsync(owner, path);
    }

    private async void OnErasePart(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PartitionsViewModel vm || GetOwner() is not { } owner)
        {
            return;
        }
        await vm.ErasePartitionAsync(owner);
    }

    private Window? GetOwner() => TopLevel.GetTopLevel(this) as Window;
}