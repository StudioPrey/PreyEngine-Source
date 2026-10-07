using System.Runtime.InteropServices;
using System.Text;

namespace MyEngine.Runtime;

/// <summary>What a crash report should say about the game it happened in. Any value may be a placeholder when
/// the crash happened before the game finished loading.</summary>
internal sealed record CrashContext(string GameName, string GameVersion, string GameDirectory, string? GpuDescription);

/// <summary>
/// Turns a crash into something a tester can send back: a plain-text report (game, version, OS, .NET, GPU and the
/// full exception with its stack) appended to <c>crash-log.txt</c>. Pure logic with no MonoGame types, so it is
/// tested without a window; the native "the game crashed" dialog lives in <see cref="CrashHandler"/>.
///
/// <para>Where the log goes: next to the game (easiest for a tester to find and send). If that folder isn't
/// writable — a game installed under Program Files, say — it falls back to
/// <c>%LocalAppData%\&lt;Game Name&gt;\crash-log.txt</c> and the on-screen message says where it ended up.</para>
/// </summary>
internal static class CrashReporter
{
    public const string LogFileName = "crash-log.txt";
    private const long MaxLogBytes = 512 * 1024;

    public static string BuildReport(Exception exception, CrashContext context, DateTime utcNow)
    {
        var sb = new StringBuilder();
        sb.AppendLine("==================== CRASH REPORT ====================");
        sb.AppendLine($"Time (UTC): {utcNow:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Game:       {context.GameName} {context.GameVersion}");
        sb.AppendLine($"OS:         {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        sb.AppendLine($".NET:       {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"GPU:        {(string.IsNullOrWhiteSpace(context.GpuDescription) ? "unknown" : context.GpuDescription)}");
        sb.AppendLine($"Folder:     {context.GameDirectory}");
        sb.AppendLine();
        sb.AppendLine("--- Exception ---");
        sb.AppendLine(exception.ToString()); // includes inner exceptions and the full stack trace
        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>Appends <paramref name="report"/> to the crash log. Returns the file it was written to, or null
    /// if no candidate location was writable (the on-screen message still shows the report's essentials).</summary>
    public static string? TryWriteLog(string report, string gameDirectory, string gameName)
    {
        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            MyEngine.Core.Saving.SaveData.MakeSafeFolderName(gameName, "Game"));

        return TryAppend(gameDirectory, report) ?? TryAppend(fallback, report);
    }

    /// <summary>Appends to <c>crash-log.txt</c> in <paramref name="directory"/>. A log that has grown past
    /// 512 KB is first set aside as <c>crash-log.txt.old</c> so it can't grow without bound.</summary>
    public static string? TryAppend(string directory, string report)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, LogFileName);
            if (File.Exists(path) && new FileInfo(path).Length > MaxLogBytes)
                File.Move(path, path + ".old", overwrite: true);
            File.AppendAllText(path, report, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }
}
