using System.Diagnostics;
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

    private double _progress;
    /// <summary>0..1 — advances as each real startup step finishes.</summary>
    public double Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, value);
    }

    /// <summary>Raised when startup checks finish. The View owns opening MainWindow and closing itself.</summary>
    public event Action? LoadingFinished;

    public SettingsService Settings { get; }
    public ProjectRegistry Projects { get; } = new();
    public EditorVersionRegistry EditorVersions { get; } = new();
    public ProjectLauncher Launcher { get; } = new();

    public SplashWindowViewModel(SettingsService settings)
    {
        Settings = settings;
    }

    /// <summary>Runs the startup sequence. The real work (reading the project list and the editor versions)
    /// takes a few milliseconds, so each stage is held until a fixed point on the clock — that gives the logo
    /// animation time to play and keeps the splash from flashing past.</summary>
    public async Task RunStartupChecksAsync()
    {
        var clock = Stopwatch.StartNew();

        Progress = 0.12;
        StatusText = "Loading recent projects…";
        await Task.Run(() => Projects.Load());
        await HoldUntilAsync(clock, 850);

        Progress = 0.52;
        StatusText = "Checking installed editor versions…";
        await Task.Run(() =>
        {
            EditorVersions.Load();
            EditorVersions.EnsureDefaultDiscovered();
        });
        await HoldUntilAsync(clock, 1700);

        Progress = 0.86;
        StatusText = "Getting the launcher ready…";
        await HoldUntilAsync(clock, 2350);

        Progress = 1;
        StatusText = "Ready.";
        await HoldUntilAsync(clock, 2850);

        LoadingFinished?.Invoke();
    }

    private static async Task HoldUntilAsync(Stopwatch clock, int milliseconds)
    {
        var remaining = milliseconds - (int)clock.ElapsedMilliseconds;
        if (remaining > 0) await Task.Delay(remaining);
    }
}
