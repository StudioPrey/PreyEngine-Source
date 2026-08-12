using System.Collections.ObjectModel;
using MyEngine.Launcher.Models;

namespace MyEngine.Launcher.ViewModels;

public sealed class ProjectListItemViewModel : ViewModelBase
{
    public ProjectInfo Model { get; }

    /// <summary>Raised when the user picks a different Editor version for this project — the owner
    /// (MainWindowViewModel) is responsible for persisting the registry when this fires.</summary>
    public event Action? VersionChanged;

    public string Name => Model.Name;
    public string Path => Model.Path;
    public string LastOpenedDisplay => Model.LastOpened.ToString("MMM d, yyyy — HH:mm");

    /// <summary>True if the project folder no longer exists on disk (moved/deleted outside the launcher).</summary>
    public bool IsMissing => !Directory.Exists(Model.Path);

    public ObservableCollection<EditorVersionInfo> AvailableVersions { get; }

    private EditorVersionInfo? _selectedVersion;
    public EditorVersionInfo? SelectedVersion
    {
        get => _selectedVersion;
        set
        {
            if (!SetProperty(ref _selectedVersion, value)) return;
            Model.EditorVersionId = value?.Id;
            VersionChanged?.Invoke();
        }
    }

    public RelayCommand OpenCommand { get; }
    public RelayCommand RemoveCommand { get; }

    public ProjectListItemViewModel(
        ProjectInfo model,
        IEnumerable<EditorVersionInfo> availableVersions,
        EditorVersionInfo? currentVersion,
        Action<ProjectListItemViewModel> onOpen,
        Action<ProjectListItemViewModel> onRemove)
    {
        Model = model;
        AvailableVersions = new ObservableCollection<EditorVersionInfo>(availableVersions);
        _selectedVersion = currentVersion;

        OpenCommand = new RelayCommand(() => onOpen(this));
        RemoveCommand = new RelayCommand(() => onRemove(this));
    }
}
