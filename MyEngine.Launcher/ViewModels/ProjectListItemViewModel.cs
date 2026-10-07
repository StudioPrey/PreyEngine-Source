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

    /// <summary>First letter of the name, shown on the project's tile.</summary>
    public string Monogram { get; }

    /// <summary>When the project was created (or an em dash if it can't be determined).</summary>
    public string CreatedDisplay { get; }

    public string LastOpenedDisplay => Model.LastOpened.ToString("MMM d, yyyy");

    /// <summary>"Just now", "3 h ago", "Yesterday" ... — a friendlier second line under the last-opened date.</summary>
    public string LastOpenedRelative => DescribeAge(Model.LastOpened);

    /// <summary>Full date and time, for the tooltip.</summary>
    public string LastOpenedFull => Model.LastOpened.ToString("dddd, MMM d, yyyy — HH:mm");

    /// <summary>True if the project folder no longer exists on disk (moved/deleted outside the launcher).</summary>
    public bool IsMissing { get; }

    public bool IsPinned => Model.IsPinned;
    public string PinMenuText => Model.IsPinned ? "Unpin from top" : "Pin to top";

    public ObservableCollection<EditorVersionInfo> AvailableVersions { get; }
    public ObservableCollection<VersionChoiceViewModel> VersionChoices { get; } = new();

    private EditorVersionInfo? _selectedVersion;
    public EditorVersionInfo? SelectedVersion
    {
        get => _selectedVersion;
        set
        {
            if (!SetProperty(ref _selectedVersion, value)) return;
            Model.EditorVersionId = value?.Id;
            OnPropertyChanged(nameof(VersionLabel));
            OnPropertyChanged(nameof(VersionIsProblem));
            RefreshChoices();
            VersionChanged?.Invoke();
        }
    }

    /// <summary>Text of the "Editor" column.</summary>
    public string VersionLabel => _selectedVersion?.Label ?? "No editor";

    /// <summary>True when there is no usable editor for this project (none installed, or its folder is gone).</summary>
    public bool VersionIsProblem => _selectedVersion == null || !_selectedVersion.IsValid;

    public RelayCommand OpenCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand TogglePinCommand { get; }

    public ProjectListItemViewModel(
        ProjectInfo model,
        IEnumerable<EditorVersionInfo> availableVersions,
        EditorVersionInfo? currentVersion,
        Action<ProjectListItemViewModel> onOpen,
        Action<ProjectListItemViewModel> onRemove,
        Action<ProjectListItemViewModel> onTogglePin)
    {
        Model = model;
        AvailableVersions = new ObservableCollection<EditorVersionInfo>(availableVersions);
        _selectedVersion = currentVersion;

        Monogram = model.Name.Trim().Length > 0 ? model.Name.Trim().Substring(0, 1).ToUpperInvariant() : "?";
        IsMissing = !Directory.Exists(model.Path);
        CreatedDisplay = ResolveCreated(model);

        OpenCommand = new RelayCommand(() => onOpen(this));
        RemoveCommand = new RelayCommand(() => onRemove(this));
        TogglePinCommand = new RelayCommand(() => onTogglePin(this));

        RefreshChoices();
    }

    private void RefreshChoices()
    {
        VersionChoices.Clear();
        foreach (var version in AvailableVersions)
        {
            var isCurrent = _selectedVersion != null && _selectedVersion.Id == version.Id;
            VersionChoices.Add(new VersionChoiceViewModel(version, isCurrent, OnChoiceSelected));
        }
    }

    private void OnChoiceSelected(VersionChoiceViewModel choice)
    {
        SelectedVersion = choice.Version;
    }

    private static string ResolveCreated(ProjectInfo model)
    {
        DateTime? created = model.CreatedAt;

        // Projects registered before CreatedAt existed: use the folder's own creation time.
        if (created == null && Directory.Exists(model.Path))
        {
            try { created = Directory.GetCreationTime(model.Path); }
            catch { created = null; }
        }

        return created.HasValue ? created.Value.ToString("MMM d, yyyy") : "—";
    }

    private static string DescribeAge(DateTime when)
    {
        var age = DateTime.Now - when;

        if (age.TotalSeconds < 60) return "Just now";
        if (age.TotalMinutes < 60) return $"{(int)age.TotalMinutes} min ago";
        if (age.TotalHours < 24) return $"{(int)age.TotalHours} h ago";
        if (age.TotalDays < 2) return "Yesterday";
        if (age.TotalDays < 7) return $"{(int)age.TotalDays} days ago";
        return when.ToString("HH:mm");
    }
}
