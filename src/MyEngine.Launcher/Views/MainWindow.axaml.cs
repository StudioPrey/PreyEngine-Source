using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MyEngine.Launcher.Services;
using MyEngine.Launcher.ViewModels;

namespace MyEngine.Launcher.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly EditorVersionRegistry _editorVersions;

    public MainWindow(ProjectRegistry projects, EditorVersionRegistry editorVersions, ProjectLauncher launcher)
    {
        InitializeComponent();

        _editorVersions = editorVersions;
        _viewModel = new MainWindowViewModel(projects, editorVersions, launcher);
        DataContext = _viewModel;

        _viewModel.RequestManageVersions += OnRequestManageVersions;
    }

    private async void OnBrowseNewProjectLocation(object? sender, RoutedEventArgs e)
    {
        var path = await PickFolderAsync("Choose a location for the new project");
        if (path != null) _viewModel.NewProjectLocation = path;
    }

    private async void OnBrowseOpenExisting(object? sender, RoutedEventArgs e)
    {
        var path = await PickFolderAsync("Choose an existing project folder");
        if (path != null) _viewModel.OpenExistingPath = path;
    }

    private async Task<string?> PickFolderAsync(string title)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return null;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });

        var folder = folders.Count > 0 ? folders[0] : null;
        return folder?.TryGetLocalPath();
    }

    private async void OnRequestManageVersions()
    {
        var window = new EditorVersionManagerWindow(_editorVersions);
        window.VersionsChanged += _viewModel.ReloadProjectList;
        await window.ShowDialog(this);
    }
}
