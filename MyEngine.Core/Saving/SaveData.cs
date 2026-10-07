namespace MyEngine.Core.Saving;

/// <summary>
/// Persistent game data for scripts — settings, high scores, progress, unlocked levels. Works the same in
/// Play Mode and in a built game, and the data survives closing the game:
/// <code>
/// SaveData.SetInt("HighScore", score);
/// SaveData.SetBool("SoundOn", false);
/// SaveData.SetString("PlayerName", "Arian");
/// int best = SaveData.GetInt("HighScore", 0);      // 2nd argument = value when nothing is saved yet
///
/// SaveData.SetObject("Inventory", myInventory);    // any class/struct with public fields or properties
/// var inv = SaveData.GetObject&lt;Inventory&gt;("Inventory");
/// </code>
///
/// <para><b>When is it written to disk?</b> Setters are cheap and only remember the change (so it is fine to
/// call them every frame). The data is written automatically when the game closes, every 10 seconds while
/// there are changes, and when you press Stop in the Editor. Call <see cref="Save"/> yourself at a moment you
/// care about (a checkpoint, finishing a level) to write it immediately.</para>
///
/// <para><b>Where?</b> Built game: <c>%LocalAppData%\&lt;Game Name&gt;\save.json</c>. Play Mode in the Editor
/// uses its own folder per project, so testing never touches a shipped game's saves. Renaming the game in
/// Build Settings changes the folder, and with it which saves the game finds — pick the name before release.</para>
///
/// <para>Nothing here throws: on a problem the read returns your default and <see cref="LastError"/> says why.
/// A key holds one value at a time regardless of type. Keys are case-sensitive.</para>
///
/// The host (Editor Play Mode / Runtime) calls <see cref="Initialize"/> — scripts never do.
/// </summary>
public static class SaveData
{
    /// <summary>How often (in seconds of game time) a host writes pending changes on its own. Bounds what a
    /// crash or power cut can lose without writing to disk on every change.</summary>
    public const float AutoSaveIntervalSeconds = 10f;

    private static SaveStore _store = new(null);
    private static float _secondsSinceAutoSave;

    /// <summary>Host call: opens (or creates) the save data in <paramref name="directory"/>. Anything the
    /// previous store still had unsaved is written first.</summary>
    public static void Initialize(string directory)
    {
        var previous = _store;
        if (previous.Directory != null && previous.HasUnsavedChanges) previous.Save();
        _store = new SaveStore(directory);
        _secondsSinceAutoSave = 0f;
    }

    /// <summary>Host call, once per frame: every <see cref="AutoSaveIntervalSeconds"/> writes pending changes
    /// (if any and if there is a save folder). Never throws; a failed write is retried next interval.</summary>
    public static void TickAutoSave(float deltaSeconds)
    {
        if (!(deltaSeconds > 0f) || float.IsInfinity(deltaSeconds)) return;

        _secondsSinceAutoSave += deltaSeconds;
        if (_secondsSinceAutoSave < AutoSaveIntervalSeconds) return;

        _secondsSinceAutoSave = 0f;
        if (_store.Directory != null && _store.HasUnsavedChanges) _store.Save();
    }

    /// <summary>The folder currently used, or null before <see cref="Initialize"/>.</summary>
    public static string? Directory => _store.Directory;

    /// <summary>Description of the most recent problem (failed write, unreadable file, bad key…), or null.</summary>
    public static string? LastError => _store.LastError;

    /// <summary>True when there are changes not yet written to disk. Used by the host to auto-save.</summary>
    public static bool HasUnsavedChanges => _store.HasUnsavedChanges;

    public static void SetInt(string key, int value) => _store.SetInt(key, value);
    public static int GetInt(string key, int defaultValue = 0) => _store.GetInt(key, defaultValue);

    public static void SetFloat(string key, float value) => _store.SetFloat(key, value);
    public static float GetFloat(string key, float defaultValue = 0f) => _store.GetFloat(key, defaultValue);

    public static void SetBool(string key, bool value) => _store.SetBool(key, value);
    public static bool GetBool(string key, bool defaultValue = false) => _store.GetBool(key, defaultValue);

    public static void SetString(string key, string value) => _store.SetString(key, value);
    public static string GetString(string key, string defaultValue = "") => _store.GetString(key, defaultValue);

    public static void SetObject<T>(string key, T value) => _store.SetObject(key, value);
    public static T? GetObject<T>(string key, T? defaultValue = default) => _store.GetObject(key, defaultValue);

    public static bool HasKey(string key) => _store.HasKey(key);
    public static void DeleteKey(string key) => _store.DeleteKey(key);
    public static void DeleteAll() => _store.DeleteAll();

    /// <summary>Writes pending changes now. Returns true when everything is on disk.</summary>
    public static bool Save() => _store.Save();

    /// <summary>The folder a built game named <paramref name="gameName"/> keeps its save data in.</summary>
    public static string GetGameDirectory(string gameName) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), MakeSafeFolderName(gameName, "Game"));

    // Path.GetInvalidFileNameChars() is OS-specific (on Linux it is just '/' and NUL), but a save folder name
    // should be valid everywhere the game might be copied to, so the Windows set is always excluded too.
    private static readonly char[] InvalidNameChars = Path.GetInvalidFileNameChars().Union("<>:\"/\\|?*").ToArray();
    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Turns free text into a valid single folder name: invalid characters become '_', trailing dots
    /// and spaces (which Windows silently drops) are removed, and reserved device names like "CON" get a
    /// leading underscore. Empty input gives <paramref name="fallback"/>.</summary>
    public static string MakeSafeFolderName(string? name, string fallback)
    {
        var cleaned = new string((name ?? string.Empty).Select(c => c < ' ' || InvalidNameChars.Contains(c) ? '_' : c).ToArray())
            .Trim().TrimEnd('.', ' ');
        if (cleaned.Length == 0) return fallback;
        return ReservedWindowsNames.Contains(cleaned) ? "_" + cleaned : cleaned;
    }
}
