using System.Collections.ObjectModel;

namespace MyEngine.Launcher.ViewModels;

public enum StepState
{
    Pending,
    Active,
    Done,
    Failed,
}

/// <summary>One line in the launch progress card ("Create project folders", "Start editor", ...).</summary>
public sealed class LaunchStepViewModel : ViewModelBase
{
    public string Title { get; }

    private StepState _state = StepState.Pending;
    public StepState State
    {
        get => _state;
        set
        {
            if (!SetProperty(ref _state, value)) return;
            OnPropertyChanged(nameof(IsPending));
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(IsDone));
            OnPropertyChanged(nameof(IsFailed));
        }
    }

    public bool IsPending => _state == StepState.Pending;
    public bool IsActive => _state == StepState.Active;
    public bool IsDone => _state == StepState.Done;
    public bool IsFailed => _state == StepState.Failed;

    public LaunchStepViewModel(string title)
    {
        Title = title;
    }
}

/// <summary>
/// State behind the launch progress card: which steps there are, which one is running, how far along it is,
/// and the error to show if one of them fails. The steps themselves are performed by MainWindowViewModel —
/// this class only reports them, so the card always reflects what really happened.
/// </summary>
public sealed class LaunchSessionViewModel : ViewModelBase
{
    public ObservableCollection<LaunchStepViewModel> Steps { get; } = new();

    private string _title = "";
    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    private string _subtitle = "";
    public string Subtitle
    {
        get => _subtitle;
        private set => SetProperty(ref _subtitle, value);
    }

    private double _progress;
    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    private bool _isActive;
    /// <summary>True while the card is on screen (running, or showing a failure).</summary>
    public bool IsActive
    {
        get => _isActive;
        private set => SetProperty(ref _isActive, value);
    }

    private bool _hasFailed;
    public bool HasFailed
    {
        get => _hasFailed;
        private set
        {
            if (!SetProperty(ref _hasFailed, value)) return;
            OnPropertyChanged(nameof(IsRunning));
        }
    }

    /// <summary>True while steps are still executing (the card is open and nothing has failed).</summary>
    public bool IsRunning => IsActive && !HasFailed;

    private string _errorMessage = "";
    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public RelayCommand DismissCommand { get; }

    public LaunchSessionViewModel()
    {
        DismissCommand = new RelayCommand(Close);
    }

    public void Begin(string title, string subtitle, IEnumerable<string> stepTitles)
    {
        Steps.Clear();
        foreach (var step in stepTitles)
            Steps.Add(new LaunchStepViewModel(step));

        Title = title;
        Subtitle = subtitle;
        ErrorMessage = "";
        HasFailed = false;
        Progress = 0;
        IsActive = true;
        OnPropertyChanged(nameof(IsRunning));
    }

    public void StartStep(int index)
    {
        if (index < 0 || index >= Steps.Count) return;
        Steps[index].State = StepState.Active;
        Recalculate();
    }

    public void CompleteStep(int index)
    {
        if (index < 0 || index >= Steps.Count) return;
        Steps[index].State = StepState.Done;
        Recalculate();
    }

    public void FailStep(int index, string message)
    {
        if (index >= 0 && index < Steps.Count)
            Steps[index].State = StepState.Failed;

        ErrorMessage = message;
        HasFailed = true;
    }

    /// <summary>Fails whichever step is currently running (used by the catch-all around a whole run).</summary>
    public void FailActiveStep(string message)
    {
        var index = -1;
        for (var i = 0; i < Steps.Count; i++)
        {
            if (Steps[i].State == StepState.Active) { index = i; break; }
        }

        FailStep(index, message);
    }

    /// <summary>Hides the card after a successful run.</summary>
    public void Finish()
    {
        Progress = 1;
        Close();
    }

    public void Close()
    {
        IsActive = false;
        HasFailed = false;
        Steps.Clear();
        OnPropertyChanged(nameof(IsRunning));
    }

    private void Recalculate()
    {
        if (Steps.Count == 0) { Progress = 0; return; }

        double done = 0;
        foreach (var step in Steps)
        {
            if (step.State == StepState.Done) done += 1;
            else if (step.State == StepState.Active) done += 0.5;
        }

        Progress = done / Steps.Count;
    }
}
