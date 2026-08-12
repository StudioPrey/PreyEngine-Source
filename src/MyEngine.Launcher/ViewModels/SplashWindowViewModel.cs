using MyEngine.Launcher.Services;

namespace MyEngine.Launcher.ViewModels;

public sealed class SplashWindowViewModel : ViewModelBase
{
    private string _statusText = "Starting…";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    /// <summary>Raised when startup checks finish. The View owns opening MainWindow and closing itself.</summary>
    public event Action? LoadingFinished;

    public ProjectRegistry Projects { get; } = new();
    public EditorVersionRegistry EditorVersions { get; } = new();
    public ProjectLauncher Launcher { get; } = new();

    /// <summary>Runs the startup sequence. Each step has a short artificial minimum-display time so the
    /// splash doesn't just flash and disappear on a fast machine — a moment of "yes, it's actually doing
    /// something" reads as more trustworthy than an instant jump-cut to the main window.</summary>
    public async Task RunStartupChecksAsync()
    {
        StatusText = "Loading recent projects…";
        await Task.Run(() => Projects.Load());
        await Task.Delay(150);

        StatusText = "Checking installed Editor versions…";
        await Task.Run(() =>
        {
            EditorVersions.Load();
            EditorVersions.EnsureDefaultDiscovered();
        });
        await Task.Delay(150);

        StatusText = "Ready.";
        await Task.Delay(200);

        LoadingFinished?.Invoke();
    }
}
