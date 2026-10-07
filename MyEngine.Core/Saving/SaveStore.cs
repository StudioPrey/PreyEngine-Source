using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyEngine.Core.Saving;

/// <summary>
/// The engine behind <see cref="SaveData"/>: a small typed key/value store persisted as one human-readable
/// JSON file (<c>save.json</c>) in a folder chosen by the host. Kept separate from the static
/// <see cref="SaveData"/> facade so the storage rules can be exercised without any global state.
///
/// <para><b>Design rules</b></para>
/// <list type="bullet">
/// <item>Reads/writes NEVER throw into game code: a save problem must not crash a game. Problems are exposed
/// through <see cref="LastError"/> and (for <see cref="Save"/>) a false return value.</item>
/// <item>A key lives in ONE namespace across all value types (setting an int then a string under the same key
/// replaces it), matching what game developers expect from a PlayerPrefs-style store.</item>
/// <item>Writes are deferred: setters only mark the store dirty; <see cref="Save"/> writes it. That keeps
/// per-frame setters cheap. The write itself is crash-safe: the new file is written completely to a temp
/// file, then swapped in with the previous version kept as <c>save.json.bak</c>.</item>
/// <item>A damaged file is never silently thrown away: the backup is tried first, and if that fails too the
/// damaged file is renamed to <c>save.corrupt-&lt;timestamp&gt;.json</c> so the player's data can still be
/// recovered by hand.</item>
/// </list>
/// All members are safe to call from any thread (one internal lock).
/// </summary>
internal sealed class SaveStore
{
    public const string FileName = "save.json";
    public const string BackupSuffix = ".bak";
    private const string TempSuffix = ".tmp";
    private const int FormatVersion = 1;

    private static readonly JsonSerializerOptions FileOptions = new()
    {
        WriteIndented = true,
        // NaN / +-Infinity are legal float values in game code but not in strict JSON; write them as the
        // named literals instead of failing the whole save.
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    // Public fields are how most game scripts declare their data, and System.Text.Json ignores fields
    // unless asked — without this, SetObject would silently save empty objects.
    private static readonly JsonSerializerOptions ObjectOptions = new() { IncludeFields = true };

    private readonly object _gate = new();
    private readonly Dictionary<string, int> _ints = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> _floats = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _strings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _bools = new(StringComparer.Ordinal);
    private bool _dirty;
    private string? _lastError;

    /// <summary>Folder the store persists to, or null for a memory-only store (nothing is ever written).</summary>
    public string? Directory { get; }

    public SaveStore(string? directory)
    {
        Directory = string.IsNullOrWhiteSpace(directory) ? null : directory;
        if (Directory != null) Load();
    }

    public string? LastError { get { lock (_gate) return _lastError; } }
    public bool HasUnsavedChanges { get { lock (_gate) return _dirty; } }

    private string? MainPath => Directory == null ? null : Path.Combine(Directory, FileName);

    // ------------------------------------------------------------------ typed access

    public void SetInt(string key, int value) => Set(key, _ints, value);
    public void SetFloat(string key, float value) => Set(key, _floats, value);
    public void SetString(string key, string? value) => Set(key, _strings, value ?? string.Empty);
    public void SetBool(string key, bool value) => Set(key, _bools, value);

    public int GetInt(string key, int defaultValue) => Get(key, _ints, defaultValue);
    public bool GetBool(string key, bool defaultValue) => Get(key, _bools, defaultValue);
    public string GetString(string key, string defaultValue) => Get(key, _strings, defaultValue);

    /// <summary>Floats also read back an int stored under the same key (5 reads as 5f) — the reverse is not
    /// done, because silently truncating a float into an int would hide a bug.</summary>
    public float GetFloat(string key, float defaultValue)
    {
        if (!ValidKey(key)) return defaultValue;
        lock (_gate)
        {
            if (_floats.TryGetValue(key, out var f)) return f;
            if (_ints.TryGetValue(key, out var i)) return i;
            return defaultValue;
        }
    }

    public void SetObject<T>(string key, T value)
    {
        string json;
        try
        {
            json = JsonSerializer.Serialize(value, ObjectOptions);
        }
        catch (Exception ex) when (ex is NotSupportedException or JsonException or InvalidOperationException)
        {
            lock (_gate) _lastError = $"SetObject('{key}'): {typeof(T).Name} cannot be saved ({ex.Message})";
            return;
        }
        SetString(key, json);
    }

    public T? GetObject<T>(string key, T? defaultValue)
    {
        var json = GetString(key, string.Empty);
        if (json.Length == 0) return defaultValue;
        try
        {
            return JsonSerializer.Deserialize<T>(json, ObjectOptions) ?? defaultValue;
        }
        catch (JsonException ex)
        {
            lock (_gate) _lastError = $"GetObject('{key}'): stored value is not a valid {typeof(T).Name} ({ex.Message})";
            return defaultValue;
        }
    }

    public bool HasKey(string key)
    {
        if (!ValidKey(key)) return false;
        lock (_gate)
            return _ints.ContainsKey(key) || _floats.ContainsKey(key) || _strings.ContainsKey(key) || _bools.ContainsKey(key);
    }

    public void DeleteKey(string key)
    {
        if (!ValidKey(key)) return;
        lock (_gate)
        {
            bool removed = _ints.Remove(key) | _floats.Remove(key) | _strings.Remove(key) | _bools.Remove(key);
            if (removed) _dirty = true;
        }
    }

    public void DeleteAll()
    {
        lock (_gate)
        {
            if (_ints.Count + _floats.Count + _strings.Count + _bools.Count == 0) return;
            _ints.Clear(); _floats.Clear(); _strings.Clear(); _bools.Clear();
            _dirty = true;
        }
    }

    // ------------------------------------------------------------------ persistence

    /// <summary>Writes pending changes. Returns true if the data on disk is up to date afterwards (including
    /// "nothing to write"); false if it could not be written — then <see cref="LastError"/> says why and the
    /// store stays dirty so a later call retries.</summary>
    public bool Save()
    {
        lock (_gate)
        {
            if (!_dirty) return true;
            if (MainPath == null)
            {
                _lastError = "SaveData has no save folder (it was not initialized), so nothing can be written.";
                return false;
            }

            try
            {
                System.IO.Directory.CreateDirectory(Directory!);

                var model = new FileModel
                {
                    Version = FormatVersion,
                    Ints = new(_ints, StringComparer.Ordinal),
                    Floats = new(_floats, StringComparer.Ordinal),
                    Strings = new(_strings, StringComparer.Ordinal),
                    Bools = new(_bools, StringComparer.Ordinal),
                };
                string json = JsonSerializer.Serialize(model, FileOptions);

                string tempPath = MainPath + TempSuffix;
                File.WriteAllText(tempPath, json, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                if (File.Exists(MainPath))
                    File.Replace(tempPath, MainPath, MainPath + BackupSuffix); // keeps the previous version as .bak
                else
                    File.Move(tempPath, MainPath);

                _dirty = false;
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or JsonException)
            {
                _lastError = $"Could not write save data to '{MainPath}': {ex.Message}";
                return false;
            }
        }
    }

    private void Load()
    {
        lock (_gate)
        {
            var path = MainPath!;
            if (!File.Exists(path) && !File.Exists(path + BackupSuffix)) return; // first run: nothing saved yet

            if (TryRead(path, out var model, out var mainProblem))
            {
                Apply(model!);
                return;
            }

            if (TryRead(path + BackupSuffix, out model, out var backupProblem))
            {
                Apply(model!);
                _dirty = true; // rewrite a good main file on the next Save
                _lastError = $"Save file was unreadable ({mainProblem}); restored the previous version from its backup.";
                return;
            }

            _lastError = $"Save file was unreadable ({mainProblem}) and so was its backup ({backupProblem}); starting with empty save data.";
            PreserveDamagedFile(path);
        }
    }

    private static bool TryRead(string path, out FileModel? model, out string problem)
    {
        model = null;
        problem = "";
        if (!File.Exists(path)) { problem = "missing"; return false; }
        try
        {
            model = JsonSerializer.Deserialize<FileModel>(File.ReadAllText(path), FileOptions);
            if (model == null) { problem = "empty"; return false; }
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            problem = ex.Message;
            return false;
        }
    }

    private void Apply(FileModel model)
    {
        _ints.Clear(); _floats.Clear(); _strings.Clear(); _bools.Clear();
        if (model.Ints != null) foreach (var kv in model.Ints) _ints[kv.Key] = kv.Value;
        if (model.Floats != null) foreach (var kv in model.Floats) _floats[kv.Key] = kv.Value;
        if (model.Strings != null) foreach (var kv in model.Strings) _strings[kv.Key] = kv.Value ?? string.Empty;
        if (model.Bools != null) foreach (var kv in model.Bools) _bools[kv.Key] = kv.Value;
    }

    private static void PreserveDamagedFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Move(path, Path.Combine(Path.GetDirectoryName(path)!, $"save.corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Could not set the damaged file aside; the next Save will overwrite it. Nothing more to do here —
            // LastError already reports the underlying problem.
        }
    }

    // ------------------------------------------------------------------ helpers

    private static bool ValidKey(string? key) => !string.IsNullOrEmpty(key);

    private void Set<T>(string key, Dictionary<string, T> target, T value)
    {
        if (!ValidKey(key))
        {
            lock (_gate) _lastError = "SaveData keys must not be null or empty.";
            return;
        }
        lock (_gate)
        {
            bool changed = false;
            // one namespace: drop the key from every OTHER type's table
            if (!ReferenceEquals(target, _ints)) changed |= _ints.Remove(key);
            if (!ReferenceEquals(target, _floats)) changed |= _floats.Remove(key);
            if (!ReferenceEquals(target, _strings)) changed |= _strings.Remove(key);
            if (!ReferenceEquals(target, _bools)) changed |= _bools.Remove(key);

            if (!target.TryGetValue(key, out var existing) || !EqualityComparer<T>.Default.Equals(existing, value))
            {
                target[key] = value;
                changed = true;
            }
            // NaN != NaN by EqualityComparer? Double/Single Equals treats NaN as equal to NaN, so a repeated
            // SetFloat(key, NaN) correctly stays clean.
            if (changed) _dirty = true;
        }
    }

    private T Get<T>(string key, Dictionary<string, T> source, T defaultValue)
    {
        if (!ValidKey(key)) return defaultValue;
        lock (_gate) return source.TryGetValue(key, out var value) ? value : defaultValue;
    }

    /// <summary>On-disk shape. Four typed tables (rather than one table of tagged values) keeps the file trivial
    /// to read and to hand-edit while debugging, and keeps ints and floats unambiguous on reload.</summary>
    private sealed class FileModel
    {
        public int Version { get; set; } = FormatVersion;
        public Dictionary<string, int>? Ints { get; set; }
        public Dictionary<string, float>? Floats { get; set; }
        public Dictionary<string, string>? Strings { get; set; }
        public Dictionary<string, bool>? Bools { get; set; }
    }
}
