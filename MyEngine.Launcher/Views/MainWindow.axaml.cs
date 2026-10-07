using Avalonia.Controls;
using Avalonia.Input;
using MyEngine.Launcher.Services;
using MyEngine.Launcher.ViewModels;

namespace MyEngine.Launcher.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow(ProjectRegistry projects, EditorVersionRegistry editorVersions, ProjectLauncher launcher, SettingsService settings)
    {
        InitializeComponent();

        _viewModel = new MainWindowViewModel(projects, editorVersions, launcher, settings);
        DataContext = _viewModel;

        _viewModel.ProjectOpened += OnProjectOpened;
        KeyDown += OnWindowKeyDown;
    }

    private void OnProjectOpened()
    {
        if (_viewModel.Settings.MinimizeOnOpen)
            WindowState = WindowState.Minimized;
    }

    /// <summary>Escape closes the New project wizard, or dismisses a failed launch.</summary>
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;

        if (_viewModel.NewProject.IsOpen)
        {
            _viewModel.NewProject.Close();
            e.Handled = true;
        }
        else if (_viewModel.Launch.IsActive && _viewModel.Launch.HasFailed)
        {
            _viewModel.Launch.Close();
            e.Handled = true;
        }
    }
}
