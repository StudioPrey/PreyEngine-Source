using System.Diagnostics;

namespace MyEngine.Editor;

public static class ExternalEditorLauncher
{
    /// <summary>Opens <paramref name="filePath"/> with whatever the OS has associated with its extension.
    /// Returns an error message on failure, or null on success.</summary>
    public static string? Open(string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true,
            });
            return null;
        }
        catch (Exception ex)
        {
            return $"Could not open '{Path.GetFileName(filePath)}': {ex.Message}";
        }
    }
}
