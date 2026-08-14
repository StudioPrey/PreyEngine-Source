using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MyEngine.Launcher.Services;
using MyEngine.Launcher.ViewModels;

namespace MyEngine.Launcher.Views;

public partial class EditorVersionManagerWindow : Window
{
    private readonly EditorVersionManagerViewModel _viewModel;

    /// <summary>Raised whenever a version is added/removed/changed, so MainWindow can refresh its per-project pickers.</summary>
    public event Action? VersionsChanged;

    public EditorVersionManagerWindow(EditorVersionRegistry registry)
    {
        InitializeComponent();

        _viewModel = new EditorVersionManagerViewModel(registry);
        _viewModel.VersionsChanged += () => VersionsChanged?.Invoke();
        DataContext = _viewModel;
    }

    private async void OnBrowseNewVersionFolder(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose the Editor's build output folder",
            AllowMultiple = false,
        });

        var folder = folders.Count > 0 ? folders[0] : null;
        var path = folder?.TryGetLocalPath();
        if (path != null) _viewModel.NewFolderPath = path;
    }
}
