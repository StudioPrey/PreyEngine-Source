using Avalonia;
using Avalonia.Styling;

namespace MyEngine.Launcher.Services;

/// <summary>Switches the whole launcher between its Dark and Light palettes (see Themes/Colors.axaml).</summary>
public static class ThemeService
{
    public const string Dark = "Dark";
    public const string Light = "Light";

    public static void Apply(string? theme)
    {
        if (Application.Current == null) return;

        Application.Current.RequestedThemeVariant =
            string.Equals(theme, Light, StringComparison.OrdinalIgnoreCase) ? ThemeVariant.Light : ThemeVariant.Dark;
    }
}
