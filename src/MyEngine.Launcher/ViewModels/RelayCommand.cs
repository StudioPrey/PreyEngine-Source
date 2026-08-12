using System.Windows.Input;

namespace MyEngine.Launcher.ViewModels;

/// <summary>Simple synchronous command. For async work, call an async method inside the Action
/// and let it run as a fire-and-forget Task — fine for this app's scale (no long-running work
/// besides process-launch and file IO, which are all near-instant).</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute == null ? null : _ => canExecute())
    {
    }

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);

    /// <summary>Call this after something that might affect CanExecute changes (e.g. selection changed).</summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
