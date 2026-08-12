using System.Diagnostics;
using MyEngine.Launcher.Models;

namespace MyEngine.Launcher.Services;

public sealed class ProjectLauncher
{
    /// <summary>Starts the Editor for <paramref name="project"/> using <paramref name="version"/>.
    /// Throws with a clear message on failure rather than swallowing errors — the caller decides how to show it.</summary>
    public void Launch(ProjectInfo project, EditorVersionInfo version)
    {
        if (!version.IsValid)
            throw new InvalidOperationException(
                $"Editor version '{version.Label}' is missing its executable at:\n{version.ExecutablePath}");

        Process.Start(new ProcessStartInfo
        {
            FileName = version.ExecutablePath,
            Arguments = $"\"{project.Path}\"",
            WorkingDirectory = version.FolderPath,
            UseShellExecute = false,
        });
    }
}
