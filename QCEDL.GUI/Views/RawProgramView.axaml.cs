using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using QCEDL.GUI.Services;
using QCEDL.GUI.ViewModels;

namespace QCEDL.GUI.Views;

public partial class RawProgramView : UserControl
{
    public RawProgramView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private Window? GetOwner() => TopLevel.GetTopLevel(this) as Window;

    private async void OnAddXml(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not RawProgramViewModel vm)
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
            Title = Localizer.Instance["Raw_PickXmlTitle"],
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("XML") { Patterns = ["*.xml"] }
            ],
        });
        var paths = files.Select(f => f.TryGetLocalPath()).Where(p => !string.IsNullOrEmpty(p)).Cast<string>().ToList();
        vm.AddXmlFiles(paths);
    }

    private void OnClearXml(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RawProgramViewModel vm)
        {
            vm.ClearXmlFiles();
        }
    }

    private void OnRemoveXml(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RawProgramViewModel vm && sender is Button b && b.Tag is string path)
        {
            vm.RemoveXmlFile(path);
        }
    }

    private async void OnExec(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not RawProgramViewModel vm || GetOwner() is not { } owner)
        {
            return;
        }
        await vm.RunRawProgramAsync(owner);
    }

    private async void OnBrowseDir(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not RawProgramViewModel vm)
        {
            return;
        }
        var top = TopLevel.GetTopLevel(this);
        if (top is null)
        {
            return;
        }
        var dirs = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Localizer.Instance["Raw_PickDirTitle"],
            AllowMultiple = false,
        });
        var path = dirs.Count > 0 ? dirs[0].TryGetLocalPath() : null;
        if (!string.IsNullOrEmpty(path))
        {
            vm.DumpOutputDir = path;
        }
    }

    private async void OnDump(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not RawProgramViewModel vm)
        {
            return;
        }
        await vm.RunDumpAsync();
    }
}