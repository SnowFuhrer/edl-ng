using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using QCEDL.GUI.ViewModels;

namespace QCEDL.GUI.Views;

public partial class ConnectionView : UserControl
{
    public ConnectionView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async void OnBrowseLoader(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ConnectionViewModel vm)
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
            Title = "Select Firehose loader (.elf / .melf / .xml)",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Firehose programmer")
                {
                    Patterns = ["*.elf", "*.melf", "*.mbn", "*.xml"],
                },
                new FilePickerFileType("All files") { Patterns = ["*"] },
            ],
        });

        var picked = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (!string.IsNullOrEmpty(picked))
        {
            vm.LoaderPath = picked;
        }
    }
}