using System.Collections.ObjectModel;
using QCEDL.GUI.Services;
using ReactiveUI;

namespace QCEDL.GUI.ViewModels;

public sealed class ShellViewModel : ViewModelBase
{
    private NavigationItem _selected;

    public ShellViewModel(EdlService edlService, ObservableLogSink logs)
    {
        ArgumentNullException.ThrowIfNull(edlService);
        ArgumentNullException.ThrowIfNull(logs);

        Logs = new LogsViewModel(logs);
        Connection = new ConnectionViewModel(edlService);
        Overview = new OverviewViewModel(edlService, Connection);
        Partitions = new PartitionsViewModel(edlService);

        NavigationItems =
        [
            new NavigationItem("Overview", Overview),
            new NavigationItem("Connection", Connection),
            new NavigationItem("Partitions", Partitions),
            new NavigationItem("Sectors", new PlaceholderViewModel("Sectors", "Read / write / erase ranges of LBAs. Tracked in gui-todos.md Phase 2.")),
            new NavigationItem("RawProgram", new PlaceholderViewModel("RawProgram", "Execute rawprogramN.xml + patchN.xml, or dump a LUN to rawprogram files. Tracked in gui-todos.md Phase 3.")),
            new NavigationItem("Advanced", new PlaceholderViewModel("Advanced", "provision, upload-loader, reset. Tracked in gui-todos.md Phase 3.")),
            new NavigationItem("Logs", Logs),
        ];
        _selected = NavigationItems[0];
    }

    public ObservableCollection<NavigationItem> NavigationItems { get; }

    public NavigationItem Selected
    {
        get => _selected;
        set => this.RaiseAndSetIfChanged(ref _selected, value);
    }

    public OverviewViewModel Overview { get; }
    public ConnectionViewModel Connection { get; }
    public PartitionsViewModel Partitions { get; }
    public LogsViewModel Logs { get; }
}

public sealed record NavigationItem(string Title, ViewModelBase Content);

public sealed class PlaceholderViewModel : ViewModelBase
{
    public PlaceholderViewModel(string title, string description)
    {
        Title = title;
        Description = description;
    }

    public string Title { get; }
    public string Description { get; }
}