namespace MyEngine.Launcher.ViewModels;

/// <summary>
/// The two-step "New project" wizard: 1) choose a project type, 2) name it and pick where it lives.
/// It only collects and validates input; MainWindowViewModel does the actual creation (through the
/// unchanged ProjectFactory) once <see cref="CreateCommand"/> fires.
/// </summary>
public sealed class NewProjectViewModel : ViewModelBase
{
    private readonly Action<string, string> _onCreate;

    // The only type for now. More can be added later as extra cards bound to a selected-type property.
    public string TypeTitle => "2D Default";
    public string TypeDescription => "Runs on the MonoGame backend. Suited for small to medium projects.";

    private bool _isOpen;
    public bool IsOpen
    {
        get => _isOpen;
        private set => SetProperty(ref _isOpen, value);
    }

    private int _step = 1;
    public int Step
    {
        get => _step;
        private set
        {
            if (!SetProperty(ref _step, value)) return;
            OnPropertyChanged(nameof(IsStep1));
            OnPropertyChanged(nameof(IsStep2));
        }
    }

    public bool IsStep1 => _step == 1;
    public bool IsStep2 => _step == 2;

    private string _name = "MyGame";
    public string Name
    {
        get => _name;
        set
        {
            if (!SetProperty(ref _name, value)) return;
            RefreshValidation();
        }
    }

    private string _location = "";
    public string Location
    {
        get => _location;
        set
        {
            if (!SetProperty(ref _location, value)) return;
            RefreshValidation();
        }
    }

    /// <summary>Why the current name/location can't be used yet, or empty when everything is fine.</summary>
    public string ValidationMessage => ComputeValidation();
    public bool HasValidationMessage => ValidationMessage.Length > 0;
    public bool CanCreate => !HasValidationMessage;

    public RelayCommand NextCommand { get; }
    public RelayCommand BackCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand CreateCommand { get; }

    public NewProjectViewModel(Action<string, string> onCreate)
    {
        _onCreate = onCreate;

        NextCommand = new RelayCommand(() => Step = 2);
        BackCommand = new RelayCommand(() => Step = 1);
        CancelCommand = new RelayCommand(Close);
        CreateCommand = new RelayCommand(Create);
    }

    /// <summary>Opens the wizard on step 1, starting from <paramref name="defaultLocation"/>.</summary>
    public void Open(string defaultLocation)
    {
        Location = defaultLocation;
        Step = 1;
        IsOpen = true;
        RefreshValidation();
    }

    public void Close()
    {
        IsOpen = false;
    }

    /// <summary>What the Enter key does: Next on step 1, Create on step 2 (when valid).</summary>
    public void Advance()
    {
        if (!IsOpen) return;

        if (IsStep1) Step = 2;
        else Create();
    }

    private void Create()
    {
        if (!IsOpen || !CanCreate) return;
        _onCreate(Name.Trim(), Location.Trim());
    }

    private void RefreshValidation()
    {
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(HasValidationMessage));
        OnPropertyChanged(nameof(CanCreate));
    }

    private string ComputeValidation()
    {
        var name = Name.Trim();
        var location = Location.Trim();

        // Same checks, same wording as the launcher always had — plus a guard for characters Windows
        // won't accept in a folder name, so a bad name is explained here instead of failing later.
        if (name.Length == 0) return "Give the project a name first.";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return "Project names can't contain \\ / : * ? \" < > |";
        if (!Directory.Exists(location)) return "Pick a valid location for the project.";
        if (Directory.Exists(Path.Combine(location, name))) return $"A folder named '{name}' already exists there.";

        return "";
    }
}
