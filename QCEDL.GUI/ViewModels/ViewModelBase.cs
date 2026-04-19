using ReactiveUI;

namespace QCEDL.GUI.ViewModels;

/// <summary>
/// Minimal non-reactive VM base so views that don't need the full ReactiveObject machinery
/// can still participate in property-change notifications.
/// </summary>
public abstract class ViewModelBase : ReactiveObject;