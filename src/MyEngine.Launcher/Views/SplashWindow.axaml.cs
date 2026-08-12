using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using MyEngine.Launcher.ViewModels;

namespace MyEngine.Launcher.Views;

public partial class SplashWindow : Window
{
    private readonly SplashWindowViewModel _viewModel = new();

    public SplashWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;

        _viewModel.LoadingFinished += OnLoadingFinished;
        Opened += async (_, _) => await _viewModel.RunStartupChecksAsync();
    }

    private void OnLoadingFinished()
    {
        // Already on the UI thread at this point (RunStartupChecksAsync's continuations resume there),
        // but posting keeps this robust even if that ever changes.
        Dispatcher.UIThread.Post(() =>
        {
            var mainWindow = new MainWindow(_viewModel.Projects, _viewModel.EditorVersions, _viewModel.Launcher);

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.MainWindow = mainWindow;

            mainWindow.Show();
            Close();
        });
    }
}
