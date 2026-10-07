using System.Diagnostics;
using MyEngine.Launcher.Models;

namespace MyEngine.Launcher.Services;

public enum EditorStartOutcome
{
    /// <summary>The editor's window is on screen.</summary>
    WindowShown,

    /// <summary>The process is alive but no window was seen within the wait — still loading (or not a Windows desktop).</summary>
    StillStarting,

    /// <summary>The process ended before showing a window — almost certainly a crash on startup.</summary>
    ExitedEarly,
}

public readonly record struct EditorStartResult(EditorStartOutcome Outcome, int ExitCode);

public sealed class ProjectLauncher
{
    /// <summary>Starts the Editor for <paramref name="project"/> using <paramref name="version"/>.
    /// Throws with a clear message on failure rather than swallowing errors — the caller decides how to show it.</summary>
    public void Launch(ProjectInfo project, EditorVersionInfo version)
    {
        using var process = Start(project, version);
    }

    /// <summary>Same as <see cref="Launch"/> but hands back the running process so the caller can watch it
    /// (see <see cref="WaitForEditorAsync"/>). The caller owns the returned Process and should dispose it.</summary>
    public Process Start(ProjectInfo project, EditorVersionInfo version)
    {
        if (!version.IsValid)
            throw new InvalidOperationException(
                $"Editor version '{version.Label}' is missing its executable at:\n{version.ExecutablePath}");

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = version.ExecutablePath,
            Arguments = $"\"{project.Path}\"",
            WorkingDirectory = version.FolderPath,
            UseShellExecute = false,
        });

        return process ?? throw new InvalidOperationException("The operating system did not start the editor process.");
    }

    /// <summary>
    /// Waits (without blocking the UI) until the editor shows its window, exits, or the timeout passes.
    /// On Windows "window shown" is the process's MainWindowHandle becoming non-zero, which happens once the
    /// editor has finished loading and made its window visible. On other platforms there is no equivalent, so
    /// a short grace period with the process still alive counts as started.
    /// </summary>
    public async Task<EditorStartResult> WaitForEditorAsync(Process process, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        var canSeeWindows = OperatingSystem.IsWindows();

        while (clock.Elapsed < timeout)
        {
            try
            {
                if (process.HasExited)
                    return new EditorStartResult(EditorStartOutcome.ExitedEarly, process.ExitCode);

                if (canSeeWindows)
                {
                    process.Refresh();
                    if (process.MainWindowHandle != IntPtr.Zero)
                        return new EditorStartResult(EditorStartOutcome.WindowShown, 0);
                }
                else if (clock.Elapsed > TimeSpan.FromSeconds(3))
                {
                    return new EditorStartResult(EditorStartOutcome.StillStarting, 0);
                }
            }
            catch
            {
                // Couldn't query the process (e.g. it runs elevated). It did start, so don't block the user on it.
                return new EditorStartResult(EditorStartOutcome.StillStarting, 0);
            }

            await Task.Delay(150);
        }

        return new EditorStartResult(EditorStartOutcome.StillStarting, 0);
    }
}
