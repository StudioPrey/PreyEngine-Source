using MyEngine.Launcher.Models;

namespace MyEngine.Launcher.ViewModels;

/// <summary>One entry in a project's "Editor version" submenu.</summary>
public sealed class VersionChoiceViewModel : ViewModelBase
{
    public EditorVersionInfo Version { get; }

    public string Label => Version.Label;

    private bool _isCurrent;
    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (!SetProperty(ref _isCurrent, value)) return;
            OnPropertyChanged(nameof(Display));
        }
    }

    /// <summary>Menu text: a check mark in front of the version the project currently uses.</summary>
    public string Display => IsCurrent ? $"✓  {Label}" : $"     {Label}";

    public RelayCommand SelectCommand { get; }

    public VersionChoiceViewModel(EditorVersionInfo version, bool isCurrent, Action<VersionChoiceViewModel> onSelect)
    {
        Version = version;
        _isCurrent = isCurrent;
        SelectCommand = new RelayCommand(() => onSelect(this));
    }
}
