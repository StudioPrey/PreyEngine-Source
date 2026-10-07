using System.Diagnostics;
using System.IO.Compression;
using MyEngine.Core;
using MyEngine.Editor.Scripting;

namespace MyEngine.Editor.Build;

public sealed class BuildResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public IReadOnlyList<string> LogLines { get; init; } = Array.Empty<string>();
    public string? OutputDirectory { get; init; }

    /// <summary>The ready-to-share .zip of the build, or null if it could not be created (the build itself
    /// still succeeded — see <see cref="Warnings"/>).</summary>
    public string? ZipPath { get; init; }

    /// <summary>The Build Report as lines of text (size, file counts, largest files, warnings). Empty for a failed build.</summary>
    public IReadOnlyList<string> ReportLines { get; init; } = Array.Empty<string>();

    /// <summary>Things worth the developer's attention that did not stop the build.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Turns a project into a standalone, double-clickable game: publishes MyEngine.Runtime (a project that
/// references only MyEngine.Core — no ImGui, no Editor code, so none of that can end up in a shipped
/// build), compiles the project's scripts straight to a real .dll instead of loading them in-memory, copies
/// Assets/ (skipping .cs source, which is compiled rather than shipped), and writes the small manifest the
/// Runtime reads on startup. After that it ships the license notices next to the game, packs the folder into a
/// ready-to-share .zip, and produces the Build Report. Runs synchronously — the Editor calls this from a background thread (see
/// EditorApp's build-triggering code, which mirrors the existing script-hot-reload background pattern) so
/// the multi-second `dotnet publish` step doesn't freeze the UI.
/// </summary>
public static class ProjectBuilder
{
    private const string ScriptsAssemblyName = "GameScripts.dll";
    private const string NoticesFolderName = "Notices";
    private const int MaxUnusedAssetsListed = 10;

    public static BuildResult Build(string projectPath, ProjectSettings settings)
    {
        var stopwatch = Stopwatch.StartNew();
        var log = new List<string>();
        var warnings = new List<string>();
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
        if (!string.IsNullOrWhiteSpace(settings.IconPath) && !File.Exists(Path.Combine(projectPath, settings.IconPath.Trim())))
            warnings.Add($"The icon '{settings.IconPath.Trim()}' was not found, so MyEngine's default icon was used.");

        var metadata = new BuildMetadata(settings.GameName, settings.GameVersion, settings.CompanyName, settings.Copyright);

        Log("Publishing Runtime (self-contained, win-x64 — this can take a minute)...");
        if (!PublishRuntime(runtimeCsproj, outputDir, iconPath, metadata, Log))
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
            VSync = settings.VSync,
            Resizable = settings.Resizable,
            TargetFps = GameManifest.NormalizeTargetFps(settings.TargetFps),
            ShowEngineSplash = settings.ShowEngineSplash,
            ScriptsAssemblyName = hasScripts ? ScriptsAssemblyName : "",
        };
        manifest.Save(outputDir);

        if (!hasScripts) warnings.Add("The project has no scripts, so the game has no behavior beyond what the scene itself defines.");

        // The engine's license notices always ship, whatever the splash setting is — see ProjectSettings.ShowEngineSplash.
        if (!CopyNotices(runtimeCsproj, outputDir))
            warnings.Add($"The '{NoticesFolderName}' folder (engine license notices) was not found next to MyEngine.Runtime, " +
                         "so this build was made WITHOUT it. Restore src/MyEngine.Runtime/Notices from the engine source.");

        var unusedAssets = BuildReportBuilder.FindPossiblyUnusedAssets(assetsRoot);
        if (unusedAssets == null)
            warnings.Add("The unused-asset check was skipped because a scene, prefab or script file could not be read.");
        else if (unusedAssets.Count > 0)
            warnings.Add(FormatUnusedAssetsWarning(unusedAssets));

        string? zipPath = CreateZip(outputDir, settings, Log, warnings);

        stopwatch.Stop();
        var reportLines = BuildReportLines(settings, outputDir, zipPath, stopwatch.Elapsed, warnings);
        WriteReportFile(outputDir, reportLines, warnings);

        Log("Build succeeded.");
        return new BuildResult
        {
            Success = true,
            Message = "Build succeeded.",
            LogLines = log,
            OutputDirectory = outputDir,
            ZipPath = zipPath,
            ReportLines = reportLines,
            Warnings = warnings,
        };
    }

    /// <summary>Copies the engine's license notices (the Runtime project's Notices folder) into the build.
    /// Returns false if that folder doesn't exist.</summary>
    private static bool CopyNotices(string runtimeCsproj, string outputDir)
    {
        var source = Path.Combine(Path.GetDirectoryName(runtimeCsproj)!, NoticesFolderName);
        if (!Directory.Exists(source)) return false;

        CopyDirectory(source, Path.Combine(outputDir, NoticesFolderName));
        return true;
    }

    private static void CopyDirectory(string sourceRoot, string destRoot)
    {
        foreach (var file in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(destRoot, Path.GetRelativePath(sourceRoot, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
    }

    /// <summary>Packs the finished build folder into "&lt;Game&gt;-&lt;version&gt;-win-x64.zip" next to it, with the game's
    /// folder as the zip's single top-level entry (so unzipping never scatters files across the destination).
    /// A failure here doesn't fail the build — the folder is complete and usable — it becomes a warning.</summary>
    private static string? CreateZip(string outputDir, ProjectSettings settings, Action<string> log, List<string> warnings)
    {
        string zipPath = GetZipPath(outputDir, settings);
        try
        {
            log($"Creating zip: {zipPath}");
            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(outputDir, zipPath, CompressionLevel.Optimal, includeBaseDirectory: true);
            return zipPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            warnings.Add($"The build succeeded but the .zip could not be created: {ex.Message}");
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch (Exception) { /* a half-written zip that can't be removed either — the warning above already tells the user the zip is not usable */ }
            return null;
        }
    }

    /// <summary>Where the zip goes: beside the build folder (in Builds/), named after the game and version.
    /// Public so the Editor can show/open the same path the builder writes.</summary>
    public static string GetZipPath(string outputDir, ProjectSettings settings)
    {
        var version = BuildMetadata.CleanText(settings.GameVersion);
        var versionPart = version.Length == 0 ? "" : "-" + SanitizeFolderName(version);
        return Path.Combine(Path.GetDirectoryName(outputDir)!, Path.GetFileName(outputDir) + versionPart + "-win-x64.zip");
    }

    private static string FormatUnusedAssetsWarning(IReadOnlyList<string> unused)
    {
        var shown = string.Join(", ", unused.Take(MaxUnusedAssetsListed));
        var more = unused.Count > MaxUnusedAssetsListed ? $" and {unused.Count - MaxUnusedAssetsListed} more" : "";
        return $"{unused.Count} asset(s) are possibly unused (no scene, prefab or script mentions them by name) — " +
               $"deleting them would make the download smaller: {shown}{more}.";
    }

    private static List<string> BuildReportLines(ProjectSettings settings, string outputDir, string? zipPath, TimeSpan elapsed, List<string> warnings)
    {
        var stats = BuildReportBuilder.Measure(outputDir);
        var lines = new List<string>
        {
            $"{settings.GameName} {settings.GameVersion} — built in {elapsed.TotalSeconds:0.#} s",
            $"Build folder: {outputDir}",
            zipPath != null && File.Exists(zipPath)
                ? $"Zip: {zipPath}  ({BuildReportBuilder.FormatBytes(new FileInfo(zipPath).Length)} to download)"
                : "Zip: not created",
            $"Size on disk: {BuildReportBuilder.FormatBytes(stats.TotalBytes)} in {stats.FileCount} file(s)",
            $"  of which your assets: {BuildReportBuilder.FormatBytes(stats.AssetBytes)} in {stats.AssetFileCount} file(s)",
            "Largest files:",
        };
        foreach (var (relativePath, bytes) in stats.LargestFiles)
            lines.Add($"  {BuildReportBuilder.FormatBytes(bytes),9}  {relativePath}");

        lines.Add(warnings.Count == 0 ? "Warnings: none" : $"Warnings ({warnings.Count}):");
        foreach (var warning in warnings) lines.Add("  - " + warning);
        return lines;
    }

    /// <summary>Keeps a copy of the report as text in Builds/, beside the zip (not inside the build folder — it
    /// isn't part of the game). Best effort: the report is also shown in the Editor.</summary>
    private static void WriteReportFile(string outputDir, IReadOnlyList<string> reportLines, List<string> warnings)
    {
        try
        {
            var path = Path.Combine(Path.GetDirectoryName(outputDir)!, Path.GetFileName(outputDir) + "-BuildReport.txt");
            File.WriteAllLines(path, reportLines);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"The build report could not be saved as a text file: {ex.Message}");
        }
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
    private static bool PublishRuntime(string runtimeCsproj, string outputDir, string iconPath, BuildMetadata metadata, Action<string> log)
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
        // Escaped like every other property value: a path or name containing ';' or ',' would otherwise be
        // split by MSBuild into bogus extra properties.
        psi.ArgumentList.Add($"-p:ApplicationIcon={BuildMetadata.EscapeMsBuildValue(iconPath)}");
        psi.ArgumentList.Add("-p:PublishReadyToRun=true");
        // What Windows shows in the exe's Properties > Details: product name, company, copyright, version.
        foreach (var argument in metadata.ToMsBuildArguments()) psi.ArgumentList.Add(argument);

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
