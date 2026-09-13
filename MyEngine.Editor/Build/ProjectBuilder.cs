using System.Diagnostics;
using MyEngine.Core;
using MyEngine.Editor.Scripting;

namespace MyEngine.Editor.Build;

public sealed class BuildResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public IReadOnlyList<string> LogLines { get; init; } = Array.Empty<string>();
    public string? OutputDirectory { get; init; }
}

/// <summary>
/// Turns a project into a standalone, double-clickable game: publishes MyEngine.Runtime (a project that
/// references only MyEngine.Core — no ImGui, no Editor code, so none of that can end up in a shipped
/// build), compiles the project's scripts straight to a real .dll instead of loading them in-memory, copies
/// Assets/ (skipping .cs source, which is compiled rather than shipped), and writes the small manifest the
/// Runtime reads on startup. Runs synchronously — the Editor calls this from a background thread (see
/// EditorApp's build-triggering code, which mirrors the existing script-hot-reload background pattern) so
/// the multi-second `dotnet publish` step doesn't freeze the UI.
/// </summary>
public static class ProjectBuilder
{
    private const string ScriptsAssemblyName = "GameScripts.dll";

    public static BuildResult Build(string projectPath, ProjectSettings settings)
    {
        var log = new List<string>();
        void Log(string line) => log.Add(line);
        BuildResult Fail(string message)
        {
            Log("BUILD FAILED: " + message);
            return new BuildResult { Success = false, Message = message, LogLines = log };
        }

        string assetsRoot = Path.Combine(projectPath, "Assets");

        string? bootSceneRelative = ResolveBootSceneRelativePath(projectPath, assetsRoot, settings);
        if (bootSceneRelative == null)
            return Fail("No boot scene configured and no scene is currently open. Set one in Build Settings, or open a scene first.");

        string bootSceneAbsolute = Path.Combine(assetsRoot, bootSceneRelative);
        if (!File.Exists(bootSceneAbsolute))
            return Fail($"Boot scene not found on disk: Assets/{bootSceneRelative}");
        Log($"Boot scene: Assets/{bootSceneRelative}");

        string? runtimeCsproj = FindRuntimeProjectPath();
        if (runtimeCsproj == null)
        {
            return Fail("Could not find the MyEngine.Runtime project (expected at src/MyEngine.Runtime/ " +
                        "alongside the solution). Building a standalone game needs the full engine source " +
                        "tree, not just a compiled Editor.");
        }

        string outputDir = Path.Combine(projectPath, "Builds", SanitizeFolderName(settings.GameName));
        Log($"Output folder: {outputDir}");
        try
        {
            if (Directory.Exists(outputDir)) Directory.Delete(outputDir, recursive: true);
            Directory.CreateDirectory(outputDir);
        }
        catch (Exception ex)
        {
            return Fail($"Could not prepare the output folder: {ex.Message}");
        }

        string iconPath = ResolveIconAbsolutePath(projectPath, settings, runtimeCsproj);
        Log($"Icon: {iconPath}");

        Log("Publishing Runtime (self-contained, win-x64 — this can take a minute)...");
        if (!PublishRuntime(runtimeCsproj, outputDir, iconPath, Log))
            return Fail("`dotnet publish` failed — see the lines above for its full output.");

        Log("Compiling scripts...");
        string scriptsDllPath = Path.Combine(outputDir, ScriptsAssemblyName);
        var compileResult = ScriptCompiler.CompileToFile(assetsRoot, scriptsDllPath);
        if (!compileResult.Success)
        {
            foreach (var error in compileResult.Errors) Log("  " + error);
            return Fail($"Script compilation failed ({compileResult.Errors.Count} error(s)) — see the lines above.");
        }
        bool hasScripts = File.Exists(scriptsDllPath);
        Log(hasScripts ? "Scripts compiled." : "No scripts to compile.");

        Log("Copying assets...");
        int copiedFiles = CopyAssets(assetsRoot, Path.Combine(outputDir, "Assets"));
        Log($"Copied {copiedFiles} asset file(s).");

        var manifest = new GameManifest
        {
            GameName = settings.GameName,
            Version = settings.GameVersion,
            BootScenePath = bootSceneRelative,
            WindowWidth = settings.WindowWidth,
            WindowHeight = settings.WindowHeight,
            Fullscreen = settings.Fullscreen,
            ScriptsAssemblyName = hasScripts ? ScriptsAssemblyName : "",
        };
        manifest.Save(outputDir);

        Log("Build succeeded.");
        return new BuildResult { Success = true, Message = "Build succeeded.", LogLines = log, OutputDirectory = outputDir };
    }

    private static string? ResolveBootSceneRelativePath(string projectPath, string assetsRoot, ProjectSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.BootScenePath))
        {
            var path = settings.BootScenePath.Trim().Replace('\\', '/').TrimStart('/');
            // Forgiving of the common mistake of typing "Assets/Scenes/Main.scene" here — this field is
            // meant to already be relative to Assets/, same as GameManifest.BootScenePath.
            if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                path = path["Assets/".Length..];
            return path;
        }

        // Fall back to whatever scene was last open — stored relative to the project root (e.g.
        // "Assets/Scenes/Main.scene"), but the manifest wants it relative to Assets/ instead.
        if (!string.IsNullOrWhiteSpace(settings.LastOpenedScenePath))
        {
            var full = Path.Combine(projectPath, settings.LastOpenedScenePath);
            return Path.GetRelativePath(assetsRoot, full).Replace('\\', '/');
        }

        return null;
    }

    /// <summary>Walks up from wherever the Editor is actually running from (which varies with build
    /// configuration/output layout) until it finds the engine's own source tree, so this works regardless
    /// of whether the Editor was launched from bin/Debug/net8.0 or a differently-shaped publish output.</summary>
    private static string? FindRuntimeProjectPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "MyEngine.Runtime", "MyEngine.Runtime.csproj");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    private static string ResolveIconAbsolutePath(string projectPath, ProjectSettings settings, string runtimeCsprojPath)
    {
        if (!string.IsNullOrWhiteSpace(settings.IconPath))
        {
            var custom = Path.Combine(projectPath, settings.IconPath.Trim());
            if (File.Exists(custom)) return Path.GetFullPath(custom);
        }

        // No custom icon configured (or it's missing) — MyEngine's own bundled default, shipped alongside
        // the Runtime project's source specifically so this fallback always exists.
        return Path.Combine(Path.GetDirectoryName(runtimeCsprojPath)!, "DefaultIcon.ico");
    }

    /// <summary>Publishes the Runtime as a single self-contained, framework-independent folder — the
    /// player never needs .NET installed separately. ReadyToRun precompiles the IL ahead of time so the
    /// game doesn't pay JIT-compilation cost on first launch, which matters most on exactly the low-end
    /// hardware this is meant to run acceptably on. Trimming is deliberately left off: it can silently
    /// strip types that are only ever reached through reflection (exactly how compiled scripts get loaded),
    /// and reliability matters more here than shaving off a few extra megabytes.</summary>
    private static bool PublishRuntime(string runtimeCsproj, string outputDir, string iconPath, Action<string> log)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("publish");
        psi.ArgumentList.Add(runtimeCsproj);
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("Release");
        psi.ArgumentList.Add("-r");
        psi.ArgumentList.Add("win-x64");
        psi.ArgumentList.Add("--self-contained");
        psi.ArgumentList.Add("true");
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add(outputDir);
        psi.ArgumentList.Add($"-p:ApplicationIcon={iconPath}");
        psi.ArgumentList.Add("-p:PublishReadyToRun=true");

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data != null) log("  " + e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) log("  " + e.Data); };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
        }
        catch (Exception ex)
        {
            log("Could not start `dotnet publish`: " + ex.Message);
            return false;
        }

        return process.ExitCode == 0;
    }

    /// <summary>Copies everything under Assets/ except .cs source files (those get compiled into
    /// GameScripts.dll instead — shipping the source too would be dead weight and needlessly exposes it).</summary>
    private static int CopyAssets(string sourceRoot, string destRoot)
    {
        int count = 0;
        Directory.CreateDirectory(destRoot);

        if (!Directory.Exists(sourceRoot)) return count;

        foreach (var file in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            if (string.Equals(Path.GetExtension(file), ".cs", StringComparison.OrdinalIgnoreCase))
                continue;

            var relative = Path.GetRelativePath(sourceRoot, file);
            var dest = Path.Combine(destRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
            count++;
        }

        return count;
    }

    private static string SanitizeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var sanitized = new string(chars).Trim();
        return sanitized.Length == 0 ? "Game" : sanitized;
    }
}
