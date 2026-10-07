using System.Diagnostics;
using MyEngine.Editor.Scripting;

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

    /// <summary>
    /// Opens a game script for editing <i>with its project context</i>, so the IDE can offer code completion
    /// for engine types (see <see cref="ScriptProjectGenerator"/> for why that context is needed).
    ///
    /// <para>A bare "open this .cs file" (what <see cref="Open"/> does) gives VS Code / Visual Studio a lone
    /// file with no project, i.e. no idea where <c>Script</c> or <c>Transform</c> come from — which is exactly
    /// the "no suggestions" problem. So: refresh the generated project files first, then, if VS Code is
    /// installed, open the <i>project folder</i> together with the file (VS Code finds
    /// <c>GameScripts.csproj</c> in it). Otherwise fall back to the OS default for <c>.cs</c>; a Visual Studio
    /// user gets full completion by opening <c>GameScripts.sln</c> once (Scripts → Open C# Project in IDE) —
    /// files handed to that running instance afterwards belong to its loaded project.</para>
    /// </summary>
    /// <param name="log">Receives non-fatal problems (e.g. the project files couldn't be written). The
    /// script is still opened — the user just won't get engine-aware completion.</param>
    public static string? OpenScript(string projectPath, string scriptPath, Action<string>? log = null)
    {
        var generateProblem = ScriptProjectGenerator.Ensure(projectPath, out _);
        if (generateProblem != null) log?.Invoke($"WARNING: {generateProblem}");

        var vsCode = FindVsCode();
        if (vsCode != null && TryStart(vsCode, projectPath, scriptPath))
            return null;

        return Open(scriptPath);
    }

    /// <summary>
    /// Opens the whole script project in an IDE (Scripts menu → Open C# Project in IDE): VS Code on the
    /// project folder if installed, otherwise the generated <c>GameScripts.sln</c> through the OS association
    /// (Visual Studio on a typical Windows machine). Returns an error message on failure, or null on success.
    /// </summary>
    public static string? OpenScriptProject(string projectPath, Action<string>? log = null)
    {
        var generateProblem = ScriptProjectGenerator.Ensure(projectPath, out _);
        if (generateProblem != null) log?.Invoke($"WARNING: {generateProblem}");

        var vsCode = FindVsCode();
        if (vsCode != null && TryStart(vsCode, projectPath))
            return null;

        var solutionPath = ScriptProjectGenerator.GetSolutionFilePath(projectPath);
        if (!File.Exists(solutionPath))
            return $"'{ScriptProjectGenerator.SolutionFileName}' doesn't exist and couldn't be generated — see the warning above.";

        return Open(solutionPath);
    }

    private static bool TryStart(string executable, params string[] arguments)
    {
        try
        {
            var psi = new ProcessStartInfo { FileName = executable, UseShellExecute = false };
            foreach (var argument in arguments) psi.ArgumentList.Add(argument);
            Process.Start(psi);
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            // Couldn't launch it (moved/uninstalled since we looked, blocked, …) — the caller falls back
            // to the OS default rather than reporting a failure the user can't act on.
            return false;
        }
    }

    /// <summary>Finds a launchable VS Code executable, or null if none is found. On Windows this resolves the
    /// real <c>Code.exe</c> (the <c>code.cmd</c> shim on PATH can't be started without a shell, and routing
    /// paths through <c>cmd /c</c> has notorious quoting pitfalls); on macOS/Linux it uses the <c>code</c>
    /// launcher, which is an executable script.</summary>
    private static string? FindVsCode()
    {
        if (OperatingSystem.IsWindows())
        {
            var candidates = new List<string>();

            var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (!string.IsNullOrEmpty(localAppData))
                candidates.Add(Path.Combine(localAppData, "Programs", "Microsoft VS Code", "Code.exe")); // per-user installer

            foreach (var variable in new[] { "ProgramFiles", "ProgramFiles(x86)" })
            {
                var programFiles = Environment.GetEnvironmentVariable(variable);
                if (!string.IsNullOrEmpty(programFiles))
                    candidates.Add(Path.Combine(programFiles, "Microsoft VS Code", "Code.exe")); // system-wide installer
            }

            // Custom install location: PATH points at <install>\bin\code.cmd, and Code.exe sits one level up.
            var shim = FindOnPath("code.cmd");
            if (shim != null)
            {
                var installDir = Directory.GetParent(Path.GetDirectoryName(shim)!);
                if (installDir != null) candidates.Add(Path.Combine(installDir.FullName, "Code.exe"));
            }

            return candidates.FirstOrDefault(File.Exists);
        }

        var onPath = FindOnPath("code");
        if (onPath != null) return onPath;

        const string macBundledLauncher = "/Applications/Visual Studio Code.app/Contents/Resources/app/bin/code";
        return OperatingSystem.IsMacOS() && File.Exists(macBundledLauncher) ? macBundledLauncher : null;
    }

    private static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim().Trim('"'), fileName);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // a malformed PATH entry — skip it, keep looking.
            }
        }

        return null;
    }
}
