using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using QCEDL.CLI.Helpers;
using QCEDL.GUI.Services;
using QCEDL.NET.PartitionTable;
using ReactiveUI;

namespace QCEDL.GUI.ViewModels;

public sealed class PartitionsViewModel : ViewModelBase
{
    private readonly EdlService _service;
    private string? _lunFilter;
    private bool _isBusy;
    private string _statusText = "Not scanned.";

    public PartitionsViewModel(EdlService service)
    {
        _service = service;
        var canRun = this.WhenAnyValue(x => x.IsBusy).Select(b => !b);
        ScanCommand = ReactiveCommand.CreateFromTask(ScanAsync, canRun);
        ScanCommand.ThrownExceptions.Subscribe(ex =>
        {
            Logging.Log($"GPT scan failed: {ex.Message}", LogLevel.Error);
            StatusText = $"Error: {ex.Message}";
        });
    }

    public ObservableCollection<PartitionRow> Partitions { get; } = [];

    public string? LunFilter
    {
        get => _lunFilter;
        set => this.RaiseAndSetIfChanged(ref _lunFilter, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    public ReactiveCommand<Unit, Unit> ScanCommand { get; }

    private async Task ScanAsync()
    {
        IsBusy = true;
        Partitions.Clear();
        try
        {
            uint? lunParam = null;
            if (!string.IsNullOrWhiteSpace(LunFilter) && uint.TryParse(LunFilter.Trim(), out var parsed))
            {
                lunParam = parsed;
            }

            await _service.RunExclusiveAsync(async m =>
            {
                var luns = await m.DetermineLunsToScanAsync(lunParam).ConfigureAwait(false);
                foreach (var lun in luns)
                {
                    try
                    {
                        var effectiveLun = m.IsDirectMode ? 0u : lun;
                        var geometry = await m.GetStorageGeometryAsync(effectiveLun).ConfigureAwait(false);
                        var data = await m.ReadSectorsAsync(effectiveLun, 0, 64).ConfigureAwait(false);
                        using var stream = new MemoryStream(data);
                        var gpt = Gpt.ReadFromStream(stream, (int)geometry.SectorSize);
                        if (gpt is null)
                        {
                            Logging.Log($"LUN {lun}: no GPT found.", LogLevel.Warning);
                            continue;
                        }

                        for (var i = 0; i < gpt.Partitions.Count; i++)
                        {
                            var p = gpt.Partitions[i];
                            if (p.FirstLBA == 0 && p.LastLBA == 0)
                            {
                                continue;
                            }
                            var row = PartitionRow.From(p, effectiveLun, i, geometry.SectorSize);
                            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => Partitions.Add(row));
                        }
                    }
                    catch (Exception ex)
                    {
                        Logging.Log($"LUN {lun} scan error: {ex.Message}", LogLevel.Warning);
                    }

                    if (m.IsDirectMode)
                    {
                        break;
                    }
                }
            }).ConfigureAwait(false);

            StatusText = $"Found {Partitions.Count} partition(s).";
        }
        finally { IsBusy = false; }
    }
}