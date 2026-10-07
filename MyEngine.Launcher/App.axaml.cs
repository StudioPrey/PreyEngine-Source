using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MyEngine.Launcher.Services;

namespace MyEngine.Launcher;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Preferences are read first so the splash already appears in the chosen theme.
            var settings = new SettingsService();
            settings.Load();
            ThemeService.Apply(settings.Current.Theme);

            desktop.MainWindow = new Views.SplashWindow(settings);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
