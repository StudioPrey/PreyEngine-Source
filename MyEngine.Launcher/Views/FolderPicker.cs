using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace MyEngine.Launcher.Views;

/// <summary>Opens the system folder picker for any view; returns the chosen local path, or null if cancelled.</summary>
internal static class FolderPicker
{
    public static async Task<string?> PickAsync(Visual owner, string title)
    {
        var topLevel = TopLevel.GetTopLevel(owner);
        if (topLevel == null) return null;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });

        var folder = folders.Count > 0 ? folders[0] : null;
        return folder?.TryGetLocalPath();
    }
}
