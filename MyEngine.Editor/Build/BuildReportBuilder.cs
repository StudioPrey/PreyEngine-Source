using System.Globalization;
using System.Text.RegularExpressions;

namespace MyEngine.Editor.Build;

/// <summary>Size and file-count facts about a finished build folder.</summary>
public sealed record BuildFolderStats(int FileCount, long TotalBytes, int AssetFileCount, long AssetBytes,
    IReadOnlyList<(string RelativePath, long Bytes)> LargestFiles);

/// <summary>
/// Everything the Build Report says that needs looking at files: how big the build is, what its biggest files
/// are, and which assets look unused. Pure file-system logic with no Editor/ImGui dependency, so it is tested
/// against real temp folders.
/// </summary>
public static class BuildReportBuilder
{
    private const int LargestFileCount = 5;
    private static readonly HashSet<string> NeverFlaggedExtensions = new(StringComparer.OrdinalIgnoreCase) { ".cs", ".scene", ".prefab" };
    private static readonly Regex QuotedString = new("\"((?:[^\"\\\\\\r\\n]|\\\\.)*)\"", RegexOptions.Compiled);

    public static string FormatBytes(long bytes)
    {
        if (bytes < 0) bytes = 0;
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return unit == 0
            ? $"{bytes} B"
            : string.Create(CultureInfo.InvariantCulture, $"{value:0.#} {units[unit]}");
    }

    /// <summary>Counts and sizes everything under <paramref name="outputDirectory"/>; "assets" are the files
    /// under its <c>Assets</c> folder.</summary>
    public static BuildFolderStats Measure(string outputDirectory)
    {
        int count = 0, assetCount = 0;
        long total = 0, assetBytes = 0;
        var files = new List<(string RelativePath, long Bytes)>();
        var assetsPrefix = "Assets" + Path.DirectorySeparatorChar;

        foreach (var path in Directory.EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories))
        {
            var length = new FileInfo(path).Length;
            var relative = Path.GetRelativePath(outputDirectory, path);
            count++;
            total += length;
            if (relative.StartsWith(assetsPrefix, StringComparison.OrdinalIgnoreCase)) { assetCount++; assetBytes += length; }
            files.Add((relative.Replace('\\', '/'), length));
        }

        var largest = files.OrderByDescending(f => f.Bytes).ThenBy(f => f.RelativePath, StringComparer.Ordinal).Take(LargestFileCount).ToList();
        return new BuildFolderStats(count, total, assetCount, assetBytes, largest);
    }

    /// <summary>
    /// Assets (relative to Assets/, with '/' separators, sorted) that no scene, prefab or script source mentions
    /// by name — candidates to delete to make the download smaller. ADVISORY ONLY: nothing is ever removed from
    /// the build, and a script that builds an asset path from pieces at runtime ("Sprites/" + name) can't be
    /// seen, so the report words it as "possibly unused".
    ///
    /// <para>Returns null when the check could not be completed (a scene/prefab/script file was unreadable):
    /// a partial answer would wrongly flag assets that ARE used, so no answer is better than a misleading one.</para>
    /// </summary>
    public static IReadOnlyList<string>? FindPossiblyUnusedAssets(string assetsRoot)
    {
        if (!Directory.Exists(assetsRoot)) return Array.Empty<string>();

        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<string>();

        foreach (var path in Directory.EnumerateFiles(assetsRoot, "*", SearchOption.AllDirectories))
        {
            var extension = Path.GetExtension(path);
            if (!NeverFlaggedExtensions.Contains(extension))
            {
                candidates.Add(path);
                continue;
            }

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }

            // Every quoted string in a scene/prefab (JSON) or script (C#) is a potential asset path or name.
            // Both the whole string and its last path segment are recorded, so "Sprites/hero.png",
            // "Sprites\\hero.png" and plain "hero.png" all count as mentioning hero.png.
            foreach (Match match in QuotedString.Matches(text))
            {
                var value = match.Groups[1].Value.Replace("\\\\", "/").Replace('\\', '/');
                if (value.Length == 0) continue;
                referenced.Add(value);
                var slash = value.LastIndexOf('/');
                if (slash >= 0 && slash < value.Length - 1) referenced.Add(value[(slash + 1)..]);
            }
        }

        var unused = new List<string>();
        foreach (var path in candidates)
        {
            var relative = Path.GetRelativePath(assetsRoot, path).Replace('\\', '/');
            if (!referenced.Contains(relative) && !referenced.Contains(Path.GetFileName(path)))
                unused.Add(relative);
        }

        unused.Sort(StringComparer.Ordinal);
        return unused;
    }
}
