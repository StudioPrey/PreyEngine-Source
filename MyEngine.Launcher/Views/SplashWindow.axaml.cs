using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using MyEngine.Launcher.Services;
using MyEngine.Launcher.ViewModels;

namespace MyEngine.Launcher.Views;

public partial class SplashWindow : Window
{
    private readonly SplashWindowViewModel _viewModel;

    // Looked up by name so this file doesn't depend on generated x:Name fields.
    private readonly Control? _root;
    private readonly Control? _mark;
    private readonly Control? _wordmark;
    private readonly Control? _tagline;
    private readonly Control? _sweep;

    public SplashWindow(SettingsService settings)
    {
        InitializeComponent();

        _viewModel = new SplashWindowViewModel(settings);
        DataContext = _viewModel;

        _root = this.FindControl<Control>("Root");
        _mark = this.FindControl<Control>("Mark");
        _wordmark = this.FindControl<Control>("Wordmark");
        _tagline = this.FindControl<Control>("Tagline");
        _sweep = this.FindControl<Control>("Sweep");

        _viewModel.LoadingFinished += OnLoadingFinished;
        Opened += async (_, _) =>
        {
            _ = PlayIntroAsync();
            await _viewModel.RunStartupChecksAsync();
        };
    }

    /// <summary>Adds the "in" / "go" classes in sequence; the transitions in SplashWindow.axaml do the animating.</summary>
    private async Task PlayIntroAsync()
    {
        await Task.Delay(150);
        _mark?.Classes.Add("in");

        await Task.Delay(550);
        _wordmark?.Classes.Add("in");

        await Task.Delay(220);
        _tagline?.Classes.Add("in");

        await Task.Delay(380);
        _sweep?.Classes.Add("go");
    }

    private async void OnLoadingFinished()
    {
        // Fade the splash out, then swap to the main window.
        _root?.Classes.Add("leaving");
        await Task.Delay(420);

        var mainWindow = new MainWindow(_viewModel.Projects, _viewModel.EditorVersions, _viewModel.Launcher, _viewModel.Settings);

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = mainWindow;

        mainWindow.Show();
        Close();
    }
}
