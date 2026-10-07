using System.Runtime.InteropServices;

namespace MyEngine.Runtime;

/// <summary>
/// What happens when the game dies with an unhandled exception: write the report (see
/// <see cref="CrashReporter"/>) and tell the player, instead of the window silently vanishing — which is all a
/// tester would otherwise see from a windowed (no console) game.
/// </summary>
internal static class CrashHandler
{
    private static int _reported;

    /// <summary>Reports a crash. Safe to call from any thread and more than once (only the first call reports).
    /// Never throws: a failure while reporting must not replace the original crash with a new one.</summary>
    public static void Report(Exception exception, RuntimeGame? game, string gameDirectory)
    {
        if (Interlocked.Exchange(ref _reported, 1) == 1) return;

        try
        {
            var manifest = game?.LoadedManifest;
            var context = new CrashContext(manifest?.GameName ?? "Unknown game", manifest?.Version ?? "?", gameDirectory, TryGetGpuDescription(game));
            var report = CrashReporter.BuildReport(exception, context, DateTime.UtcNow);
            var logPath = CrashReporter.TryWriteLog(report, gameDirectory, context.GameName);

            var message = logPath != null
                ? $"The game has crashed and needs to close.\n\n{exception.GetType().Name}: {exception.Message}\n\nA crash report was saved to:\n{logPath}\n\nPlease send that file to the developer."
                : $"The game has crashed and needs to close.\n\n{exception.GetType().Name}: {exception.Message}\n\nThe crash report could not be saved to disk.";
            NativeMessageBox.ShowError(context.GameName, message);
        }
        catch (Exception)
        {
            // Deliberately swallowed: this is the last line of defense, and throwing here would only hide the
            // exception that is already being reported.
        }
    }

    private static string? TryGetGpuDescription(RuntimeGame? game)
    {
        try
        {
            return game?.GraphicsDevice?.Adapter?.Description;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            return null; // no graphics device (yet, or any more) — the report just says "unknown"
        }
    }
}

/// <summary>A native error dialog. Windows only (that is the only platform builds target today); a no-op
/// elsewhere, where the crash log is still written.</summary>
internal static class NativeMessageBox
{
    // MB_SETFOREGROUND + MB_TOPMOST: without them the dialog can open BEHIND a fullscreen game window, leaving
    // the player looking at a frozen-looking screen with no idea a message is waiting.
    private const uint MbOk = 0x0, MbIconError = 0x10, MbSetForeground = 0x10000, MbTopmost = 0x40000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    public static void ShowError(string title, string text)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            MessageBoxW(IntPtr.Zero, text, title, MbOk | MbIconError | MbSetForeground | MbTopmost);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // no user32 (unusual environment) — the crash log has already been written
        }
    }
}
