using Avalonia;

namespace MyEngine.Launcher;

internal class Program
{
    // Avalonia requires a synchronous, attribute-decorated Main on Windows for proper startup.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
