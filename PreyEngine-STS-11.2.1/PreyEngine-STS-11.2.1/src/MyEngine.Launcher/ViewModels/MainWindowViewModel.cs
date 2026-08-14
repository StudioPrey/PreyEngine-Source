using System.Collections.ObjectModel;
using MyEngine.Launcher.Models;
using MyEngine.Launcher.Services;

namespace MyEngine.Launcher.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly ProjectRegistry _projects;
    private readonly EditorVersionRegistry _editorVersions;
    private readonly ProjectLauncher _launcher;

    public ObservableCollection<ProjectListItemViewModel> Projects { get; } = new();

    private string _newProjectName = "MyGame";
    public string NewProjectName
    {
        get => _newProjectName;
        set => SetProperty(ref _newProjectName, value);
    }

    private string _newProjectLocation;
    public string NewProjectLocation
    {
        get => _newProjectLocation;
        set => SetProperty(ref _newProjectLocation, value);
    }

    private string _openExistingPath = "";
    public string OpenExistingPath
    {
        get => _openExistingPath;
        set => SetProperty(ref _openExistingPath, value);
    }

    private string _statusMessage = "";
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool HasNoEditorVersions => _editorVersions.Versions.Count == 0;
    public bool HasNoProjects => Projects.Count == 0;
    public string DefaultVersionLabel => _editorVersions.GetDefault()?.Label ?? "none — click \"Manage Versions\"";

    /// <summary>The View subscribes to this to open the Editor Version Manager window.</summary>
    public event Action? RequestManageVersions;

    public RelayCommand CreateProjectCommand { get; }
    public RelayCommand OpenExistingCommand { get; }
    public RelayCommand ManageVersionsCommand { get; }

    public MainWindowViewModel(ProjectRegistry projects, EditorVersionRegistry editorVersions, ProjectLauncher launcher)
    {
        _projects = projects;
        _editorVersions = editorVersions;
        _launcher = launcher;

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _newProjectLocation = documents.Length > 0 ? Path.Combine(documents, "MyEngine Projects") : "";

        ReloadProjectList();

        CreateProjectCommand = new RelayCommand(CreateProject);
        OpenExistingCommand = new RelayCommand(OpenExisting);
        ManageVersionsCommand = new RelayCommand(() => RequestManageVersions?.Invoke());
    }

    /// <summary>Rebuilds the project list from the registry. Call after any change to projects OR editor versions.</summary>
    public void ReloadProjectList()
    {
        Projects.Clear();
        foreach (var p in _projects.Projects.OrderByDescending(p => p.LastOpened))
        {
            var currentVersion = _editorVersions.Find(p.EditorVersionId) ?? _editorVersions.GetDefault();
            var item = new ProjectListItemViewModel(p, _editorVersions.Versions, currentVersion, OpenProject, RemoveProject);
            item.VersionChanged += () => _projects.Save();
            Projects.Add(item);
        }

        OnPropertyChanged(nameof(HasNoEditorVersions));
        OnPropertyChanged(nameof(HasNoProjects));
        OnPropertyChanged(nameof(DefaultVersionLabel));
    }

    private void CreateProject()
    {
        var name = NewProjectName.Trim();
        if (name.Length == 0) { StatusMessage = "Give the project a name first."; return; }
        if (!Directory.Exists(NewProjectLocation)) { StatusMessage = "Pick a valid location for the project."; return; }
        if (Directory.Exists(Path.Combine(NewProjectLocation, name)))
        {
            StatusMessage = $"A folder named '{name}' already exists there.";
            return;
        }

        try
        {
            var path = ProjectFactory.CreateProjectFolders(NewProjectLocation, name);
            var defaultVersion = _editorVersions.GetDefault();
            var project = _projects.AddOrUpdate(name, path, defaultVersion?.Id);
            StatusMessage = $"Created '{name}'.";
            OpenProject(project);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not create project: {ex.Message}";
        }
    }

    private void OpenExisting()
    {
        var path = OpenExistingPath.Trim();
        if (!Directory.Exists(path)) { StatusMessage = $"Folder not found: {path}"; return; }

        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (name.Length == 0) name = path;

        var defaultVersion = _editorVersions.GetDefault();
        var project = _projects.AddOrUpdate(name, path, defaultVersion?.Id);
        OpenExistingPath = "";
        OpenProject(project);
    }

    public void OpenProject(ProjectListItemViewModel item) => OpenProject(item.Model);

    private void OpenProject(ProjectInfo project)
    {
        var version = _editorVersions.Find(project.EditorVersionId) ?? _editorVersions.GetDefault();
        if (version == null)
        {
            StatusMessage = "No Editor version installed yet — click \"Manage Versions\" to add one.";
            ReloadProjectList();
            return;
        }

        try
        {
            _launcher.Launch(project, version);
            project.LastOpened = DateTime.Now;
            _projects.Save();
            StatusMessage = $"Launched '{project.Name}' with {version.Label}.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }

        ReloadProjectList();
    }

    public void RemoveProject(ProjectListItemViewModel item)
    {
        _projects.Remove(item.Model);
        StatusMessage = $"Removed '{item.Name}' from the list (files were not deleted).";
        ReloadProjectList();
    }
}
