using System.Diagnostics;
using MyEngine.Launcher.Services;

namespace MyEngine.Launcher.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settings;

    public bool IsDarkTheme => !IsLightTheme;
    public bool IsLightTheme => string.Equals(_settings.Current.Theme, ThemeService.Light, StringComparison.OrdinalIgnoreCase);

    public string DefaultProjectLocation
    {
        get => _settings.Current.DefaultProjectLocation;
        set
        {
            if (_settings.Current.DefaultProjectLocation == value) return;
            _settings.Current.DefaultProjectLocation = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    public bool MinimizeOnOpen
    {
        get => _settings.Current.MinimizeOnOpen;
        set
        {
            if (_settings.Current.MinimizeOnOpen == value) return;
            _settings.Current.MinimizeOnOpen = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    private string _statusMessage = "";
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public RelayCommand SetDarkCommand { get; }
    public RelayCommand SetLightCommand { get; }
    public RelayCommand OpenDataFolderCommand { get; }

    public SettingsViewModel(SettingsService settings)
    {
        _settings = settings;

        SetDarkCommand = new RelayCommand(() => SetTheme(ThemeService.Dark));
        SetLightCommand = new RelayCommand(() => SetTheme(ThemeService.Light));
        OpenDataFolderCommand = new RelayCommand(OpenDataFolder);
    }

    private void SetTheme(string theme)
    {
        if (string.Equals(_settings.Current.Theme, theme, StringComparison.OrdinalIgnoreCase)) return;

        _settings.Current.Theme = theme;
        _settings.Save();
        ThemeService.Apply(theme);

        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(IsLightTheme));
    }

    private void OpenDataFolder()
    {
        try
        {
            var folder = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyEngine");
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
            StatusMessage = "";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't open the data folder: {ex.Message}";
        }
    }
}
