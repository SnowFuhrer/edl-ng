using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using QCEDL.CLI.Helpers;
using QCEDL.GUI.Services;
using QCEDL.GUI.ViewModels;
using QCEDL.GUI.Views;

namespace QCEDL.GUI;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Route all CLI Logging output into the live log sink the GUI binds to, and
            // silence the console sink by default (the GUI owns the surface now).
            Logging.ConsoleSinkEnabled = false;
            var logSink = new ObservableLogSink();
            Logging.LogEmitted += logSink.Emit;

            var edlService = new EdlService();
            var shell = new ShellViewModel(edlService, logSink);

            desktop.MainWindow = new MainWindow
            {
                DataContext = shell,
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}