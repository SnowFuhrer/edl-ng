using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using QCEDL.GUI.Services;
using QCEDL.GUI.ViewModels;

namespace QCEDL.GUI.Views;

public partial class SectorsView : UserControl
{
    public SectorsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private Window? GetOwner() => TopLevel.GetTopLevel(this) as Window;

    private async void OnBrowseFile(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SectorsViewModel vm)
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
            Title = Localizer.Instance["Sectors_OpenTitle"],
            AllowMultiple = false,
        });
        var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (!string.IsNullOrEmpty(path))
        {
            vm.FilePath = path;
        }
    }

    private async Task<string?> SavePickerAsync(string suggestedName)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null)
        {
            return null;
        }
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Localizer.Instance["Sectors_SaveTitle"],
            SuggestedFileName = suggestedName,
            DefaultExtension = "img",
        });
        return file?.TryGetLocalPath();
    }

    private async void OnRead(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SectorsViewModel vm || GetOwner() is not { })
        {
            return;
        }

        var path = vm.FilePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = await SavePickerAsync($"lun{vm.Lun}_lba{vm.StartLba}.img");
        }
        if (string.IsNullOrEmpty(path))
        {
            return;
        }
        await vm.ReadSectorsAsync(path);
    }

    private async void OnReadLun(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SectorsViewModel vm || GetOwner() is not { })
        {
            return;
        }
        var path = vm.FilePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = await SavePickerAsync($"lun{vm.Lun}.img");
        }
        if (string.IsNullOrEmpty(path))
        {
            return;
        }
        await vm.ReadLunAsync(path);
    }

    private async void OnWrite(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SectorsViewModel vm || GetOwner() is not { } owner)
        {
            return;
        }
        var path = vm.FilePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null)
            {
                return;
            }
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Localizer.Instance["Sectors_OpenTitle"],
                AllowMultiple = false,
            });
            path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        }
        if (string.IsNullOrEmpty(path))
        {
            return;
        }
        await vm.WriteSectorsAsync(owner, path);
    }

    private async void OnErase(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SectorsViewModel vm || GetOwner() is not { } owner)
        {
            return;
        }
        await vm.EraseSectorsAsync(owner);
    }
}