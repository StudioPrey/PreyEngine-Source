using MyEngine.Launcher.Models;

namespace MyEngine.Launcher.ViewModels;

public sealed class EditorVersionRowViewModel : ViewModelBase
{
    public EditorVersionInfo Model { get; }

    public string Label => Model.Label;
    public string FolderPath => Model.FolderPath;
    public bool IsDefault => Model.IsDefault;
    public bool IsValid => Model.IsValid;
    public string StatusText => IsValid ? "OK" : "Executable not found";

    public EditorVersionRowViewModel(EditorVersionInfo model)
    {
        Model = model;
    }
}
