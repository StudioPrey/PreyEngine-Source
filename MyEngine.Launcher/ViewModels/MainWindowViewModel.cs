using System.Collections.ObjectModel;
using System.Diagnostics;
using MyEngine.Launcher.Models;
using MyEngine.Launcher.Services;

namespace MyEngine.Launcher.ViewModels;

public enum LauncherPage
{
    Projects,
    EditorVersions,
    Docs,
    Settings,
}

public sealed class MainWindowViewModel : ViewModelBase
{
    /// <summary>How long a launch step stays on screen at minimum, so a fast step doesn't flicker past unseen.</summary>
    private const int StepPauseMs = 220;

    /// <summary>How long to wait for the editor window before treating it as "still starting" and moving on.</summary>
    private static readonly TimeSpan EditorWindowTimeout = TimeSpan.FromSeconds(30);

    private readonly ProjectRegistry _projects;
    private readonly EditorVersionRegistry _editorVersions;
    private readonly ProjectLauncher _launcher;
    private readonly SettingsService _settings;

    /// <summary>True while a create/open run is in progress — stops a double click from starting two editors.</summary>
    private bool _isBusy;

    public ObservableCollection<ProjectListItemViewModel> Projects { get; } = new();

    // ------------------------------------------------------------ child view models

    public EditorVersionManagerViewModel EditorVersions { get; }
    public NewProjectViewModel NewProject { get; }
    public LaunchSessionViewModel Launch { get; }
    public SettingsViewModel Settings { get; }

    // ------------------------------------------------------------ navigation

    private LauncherPage _currentPage = LauncherPage.Projects;
    public LauncherPage CurrentPage => _currentPage;

    public bool IsProjectsPage => _currentPage == LauncherPage.Projects;
    public bool IsEditorVersionsPage => _currentPage == LauncherPage.EditorVersions;
    public bool IsDocsPage => _currentPage == LauncherPage.Docs;
    public bool IsSettingsPage => _currentPage == LauncherPage.Settings;

    public RelayCommand ShowProjectsCommand { get; }
    public RelayCommand ShowEditorVersionsCommand { get; }
    public RelayCommand ShowDocsCommand { get; }
    public RelayCommand ShowSettingsCommand { get; }

    /// <summary>True while the New project wizard or the launch progress card is on screen.</summary>
    public bool IsOverlayVisible => NewProject.IsOpen || Launch.IsActive;

    // ------------------------------------------------------------ projects page

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
    public bool HasProjects => Projects.Count > 0;
    public string DefaultVersionLabel => _editorVersions.GetDefault()?.Label ?? "none yet";

    public string ProjectCountText =>
        Projects.Count == 0 ? "No projects yet" : Projects.Count == 1 ? "1 project" : $"{Projects.Count} projects";

    /// <summary>Raised after an editor window has opened for a project (the view may minimise the launcher).</summary>
    public event Action? ProjectOpened;

    public RelayCommand NewProjectCommand { get; }
    public RelayCommand OpenExistingCommand { get; }

    public MainWindowViewModel(
        ProjectRegistry projects,
        EditorVersionRegistry editorVersions,
        ProjectLauncher launcher,
        SettingsService settings)
    {
        _projects = projects;
        _editorVersions = editorVersions;
        _launcher = launcher;
        _settings = settings;

        Settings = new SettingsViewModel(settings);
        Launch = new LaunchSessionViewModel();
        NewProject = new NewProjectViewModel(StartCreate);

        EditorVersions = new EditorVersionManagerViewModel(editorVersions);
        EditorVersions.VersionsChanged += ReloadProjectList;

        NewProject.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NewProjectViewModel.IsOpen)) OnPropertyChanged(nameof(IsOverlayVisible));
        };
        Launch.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LaunchSessionViewModel.IsActive)) OnPropertyChanged(nameof(IsOverlayVisible));
        };

        ReloadProjectList();

        ShowProjectsCommand = new RelayCommand(() => ShowPage(LauncherPage.Projects));
        ShowEditorVersionsCommand = new RelayCommand(() => ShowPage(LauncherPage.EditorVersions));
        ShowDocsCommand = new RelayCommand(() => ShowPage(LauncherPage.Docs));
        ShowSettingsCommand = new RelayCommand(() => ShowPage(LauncherPage.Settings));

        NewProjectCommand = new RelayCommand(OpenNewProjectWizard);
        OpenExistingCommand = new RelayCommand(OpenExisting);
    }

    private void ShowPage(LauncherPage page)
    {
        if (_currentPage == page) return;

        _currentPage = page;
        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(IsProjectsPage));
        OnPropertyChanged(nameof(IsEditorVersionsPage));
        OnPropertyChanged(nameof(IsDocsPage));
        OnPropertyChanged(nameof(IsSettingsPage));
    }

    /// <summary>Rebuilds the project list from the registry. Call after any change to projects OR editor versions.
    /// Pinned projects come first, then the rest by most recently opened.</summary>
    public void ReloadProjectList()
    {
        Projects.Clear();

        var ordered = _projects.Projects
            .OrderByDescending(p => p.IsPinned)
            .ThenByDescending(p => p.LastOpened);

        foreach (var p in ordered)
        {
            var currentVersion = _editorVersions.Find(p.EditorVersionId) ?? _editorVersions.GetDefault();
            var item = new ProjectListItemViewModel(p, _editorVersions.Versions, currentVersion, OpenProject, RemoveProject, TogglePin);
            item.VersionChanged += () => _projects.Save();
            Projects.Add(item);
        }

        OnPropertyChanged(nameof(HasNoEditorVersions));
        OnPropertyChanged(nameof(HasNoProjects));
        OnPropertyChanged(nameof(HasProjects));
        OnPropertyChanged(nameof(ProjectCountText));
        OnPropertyChanged(nameof(DefaultVersionLabel));
    }

    // ------------------------------------------------------------ new project

    private void OpenNewProjectWizard()
    {
        if (_isBusy) return;
        NewProject.Open(GetDefaultProjectLocation());
    }

    private string GetDefaultProjectLocation()
    {
        var saved = _settings.Current.DefaultProjectLocation;
        if (!string.IsNullOrWhiteSpace(saved)) return saved;

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return documents.Length > 0 ? Path.Combine(documents, "MyEngine Projects") : "";
    }

    private void StartCreate(string name, string location) => _ = CreateAndOpenAsync(name, location);

    /// <summary>
    /// Creates the project and hands it to the editor, reporting every stage to the progress card.
    /// The project's files are still made by ProjectFactory.CreateProjectFolders — exactly as before;
    /// what's new is that the work runs off the UI thread, each result is verified before moving on, and
    /// the launcher only reports success once the editor's window has actually appeared.
    /// </summary>
    private async Task CreateAndOpenAsync(string name, string location)
    {
        if (_isBusy) return;
        _isBusy = true;

        NewProject.Close();
        Launch.Begin(
            $"Creating {name}",
            "The editor opens as soon as everything is ready.",
            new[]
            {
                "Check project details",
                "Create project folders",
                "Verify project structure",
                "Save to your project list",
                "Find the editor",
                "Start the editor",
                "Wait for the editor window",
            });

        try
        {
            // 0 — the same checks CreateProject always made
            Launch.StartStep(0);
            await Task.Delay(StepPauseMs);
            if (name.Length == 0) { Launch.FailStep(0, "Give the project a name first."); return; }
            if (!Directory.Exists(location)) { Launch.FailStep(0, "Pick a valid location for the project."); return; }
            if (Directory.Exists(Path.Combine(location, name)))
            {
                Launch.FailStep(0, $"A folder named '{name}' already exists there.");
                return;
            }
            Launch.CompleteStep(0);

            // 1 — create the folders (unchanged ProjectFactory, just off the UI thread)
            Launch.StartStep(1);
            var projectPath = await Task.Run(() => ProjectFactory.CreateProjectFolders(location, name));
            await Task.Delay(StepPauseMs);
            Launch.CompleteStep(1);

            // 2 — make sure what we just created is really there before the editor is pointed at it
            Launch.StartStep(2);
            var missing = await Task.Run(() => FindMissingFolder(projectPath));
            await Task.Delay(StepPauseMs);
            if (missing != null)
            {
                Launch.FailStep(2, $"The project folder was created but '{missing}' is missing. Check that you can write to {location}.");
                return;
            }
            Launch.CompleteStep(2);

            // 3 — remember it
            Launch.StartStep(3);
            var defaultVersion = _editorVersions.GetDefault();
            var project = _projects.AddOrUpdate(name, projectPath, defaultVersion?.Id);
            StatusMessage = $"Created '{name}'.";
            ReloadProjectList();
            await Task.Delay(StepPauseMs);
            Launch.CompleteStep(3);

            // 4..6 — hand over to the editor
            await RunLaunchStepsAsync(project, firstStepIndex: 4);
        }
        catch (Exception ex)
        {
            Launch.FailActiveStep($"Could not create the project: {ex.Message}");
        }
        finally
        {
            _isBusy = false;
        }
    }

    private static string? FindMissingFolder(string projectPath)
    {
        var expected = new[]
        {
            projectPath,
            Path.Combine(projectPath, "Assets"),
            Path.Combine(projectPath, "Assets", "Scenes"),
            Path.Combine(projectPath, "Assets", "Prefabs"),
        };

        foreach (var folder in expected)
        {
            if (!Directory.Exists(folder))
                return Path.GetRelativePath(Path.GetDirectoryName(projectPath) ?? projectPath, folder);
        }

        return null;
    }

    // ------------------------------------------------------------ open

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

    private void OpenProject(ProjectInfo project) => _ = OpenProjectAsync(project);

    private async Task OpenProjectAsync(ProjectInfo project)
    {
        if (_isBusy) return;
        _isBusy = true;

        Launch.Begin(
            $"Opening {project.Name}",
            "The editor window will appear in a moment.",
            new[]
            {
                "Check project folder",
                "Find the editor",
                "Start the editor",
                "Wait for the editor window",
            });

        try
        {
            Launch.StartStep(0);
            await Task.Delay(StepPauseMs);
            if (!Directory.Exists(project.Path))
            {
                // Without this the editor would quietly create an empty project at the old path.
                Launch.FailStep(0, $"This project's folder no longer exists:\n{project.Path}\nMove it back, or remove the project from the list.");
                return;
            }
            Launch.CompleteStep(0);

            await RunLaunchStepsAsync(project, firstStepIndex: 1);
        }
        catch (Exception ex)
        {
            Launch.FailActiveStep($"Could not open the project: {ex.Message}");
        }
        finally
        {
            _isBusy = false;
        }
    }

    /// <summary>
    /// The shared tail of creating and opening: find the editor, start it, wait for its window.
    /// Uses three consecutive steps of the progress card beginning at <paramref name="firstStepIndex"/>.
    /// Returns true when the editor is up.
    /// </summary>
    private async Task<bool> RunLaunchStepsAsync(ProjectInfo project, int firstStepIndex)
    {
        var step = firstStepIndex;

        // Find the editor (the project's own version, else the default — as before)
        Launch.StartStep(step);
        var version = _editorVersions.Find(project.EditorVersionId) ?? _editorVersions.GetDefault();
        await Task.Delay(StepPauseMs);

        if (version == null)
        {
            Launch.FailStep(step, "No editor version is installed yet. Open Editor versions in the sidebar, add one, then try again.");
            ReloadProjectList();
            return false;
        }

        if (!version.IsValid)
        {
            Launch.FailStep(step, $"The editor '{version.Label}' is missing its executable:\n{version.ExecutablePath}\nOpen Editor versions to point it at the right folder.");
            return false;
        }

        Launch.CompleteStep(step);
        step++;

        // Start the editor
        Launch.StartStep(step);
        Process process;
        try
        {
            process = await Task.Run(() => _launcher.Start(project, version));
        }
        catch (Exception ex)
        {
            Launch.FailStep(step, ex.Message);
            return false;
        }

        try
        {
            project.LastOpened = DateTime.Now;
            _projects.Save();
        }
        catch
        {
            // The editor is already running; failing to note the time isn't worth stopping for.
        }

        Launch.CompleteStep(step);
        step++;

        // Wait for its window — this is what makes "launched" mean the editor really came up
        Launch.StartStep(step);
        EditorStartResult result;
        try
        {
            result = await _launcher.WaitForEditorAsync(process, EditorWindowTimeout);
        }
        finally
        {
            process.Dispose();
        }

        if (result.Outcome == EditorStartOutcome.ExitedEarly)
        {
            Launch.FailStep(step, $"The editor closed right after starting (exit code {result.ExitCode}). Try another editor version from the project's menu, or check the project folder.");
            ReloadProjectList();
            return false;
        }

        Launch.CompleteStep(step);
        await Task.Delay(350);

        StatusMessage = $"Launched '{project.Name}' with {version.Label}.";
        Launch.Finish();
        ReloadProjectList();
        ProjectOpened?.Invoke();
        return true;
    }

    // ------------------------------------------------------------ list actions

    public void RemoveProject(ProjectListItemViewModel item)
    {
        _projects.Remove(item.Model);
        StatusMessage = $"Removed '{item.Name}' from the list (files were not deleted).";
        ReloadProjectList();
    }

    private void TogglePin(ProjectListItemViewModel item)
    {
        item.Model.IsPinned = !item.Model.IsPinned;

        try
        {
            _projects.Save();
            StatusMessage = item.Model.IsPinned
                ? $"Pinned '{item.Name}' to the top."
                : $"Unpinned '{item.Name}'.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't save the change: {ex.Message}";
        }

        ReloadProjectList();
    }
}
