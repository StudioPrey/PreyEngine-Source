using System.Collections.ObjectModel;
using MyEngine.Launcher.Services;

namespace MyEngine.Launcher.ViewModels;

public sealed class EditorVersionManagerViewModel : ViewModelBase
{
    private readonly EditorVersionRegistry _registry;

    public ObservableCollection<EditorVersionRowViewModel> Versions { get; } = new();

    /// <summary>Raised after any change (add/remove/set default) so MainWindowViewModel can refresh
    /// the per-project version pickers.</summary>
    public event Action? VersionsChanged;

    private string _newLabel = "";
    public string NewLabel
    {
        get => _newLabel;
        set => SetProperty(ref _newLabel, value);
    }

    private string _newFolderPath = "";
    public string NewFolderPath
    {
        get => _newFolderPath;
        set => SetProperty(ref _newFolderPath, value);
    }

    private EditorVersionRowViewModel? _selectedRow;
    public EditorVersionRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (!SetProperty(ref _selectedRow, value)) return;
            OnPropertyChanged(nameof(CanRemove));
            OnPropertyChanged(nameof(CanSetDefault));
        }
    }

    public bool CanRemove => SelectedRow != null;
    public bool CanSetDefault => SelectedRow != null && !SelectedRow.IsDefault;

    private string _statusMessage = "";
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public RelayCommand AddCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand SetDefaultCommand { get; }

    public EditorVersionManagerViewModel(EditorVersionRegistry registry)
    {
        _registry = registry;
        ReloadRows();

        AddCommand = new RelayCommand(AddVersion);
        RemoveCommand = new RelayCommand(RemoveSelected);
        SetDefaultCommand = new RelayCommand(SetSelectedDefault);
    }

    private void ReloadRows()
    {
        Versions.Clear();
        foreach (var v in _registry.Versions)
            Versions.Add(new EditorVersionRowViewModel(v));
    }

    private void AddVersion()
    {
        var label = NewLabel.Trim();
        var folder = NewFolderPath.Trim();

        if (label.Length == 0) { StatusMessage = "Give this version a name first."; return; }
        if (!Directory.Exists(folder)) { StatusMessage = "That folder doesn't exist."; return; }

        string exeName = OperatingSystem.IsWindows() ? "MyEngine.Editor.exe" : "MyEngine.Editor";
        if (!File.Exists(Path.Combine(folder, exeName)))
        {
            StatusMessage = $"No {exeName} found in that folder — pick the Editor's build output folder.";
            return;
        }

        var added = _registry.Add(label, folder);
        NewLabel = "";
        NewFolderPath = "";
        StatusMessage = $"Added '{label}'.";
        ReloadRows();
        SelectedRow = Versions.FirstOrDefault(v => v.Model == added);
        VersionsChanged?.Invoke();
    }

    private void RemoveSelected()
    {
        if (SelectedRow == null) return;
        var label = SelectedRow.Label;
        _registry.Remove(SelectedRow.Model);
        SelectedRow = null;
        StatusMessage = $"Removed '{label}'.";
        ReloadRows();
        VersionsChanged?.Invoke();
    }

    private void SetSelectedDefault()
    {
        if (SelectedRow == null) return;
        var model = SelectedRow.Model;

        _registry.SetDefault(model);
        StatusMessage = $"'{model.Label}' is now the default.";
        ReloadRows();
        SelectedRow = Versions.FirstOrDefault(v => v.Model == model);
        VersionsChanged?.Invoke();
    }
}
