using System.Collections.Concurrent;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using MyEngine.Core.Audio;
using MyEngine.Core.Components;
using MyEngine.Core.SceneSystem;
using MyEngine.Editor.Scripting;
using MyEngine.EditorFramework;

namespace MyEngine.Editor;

public enum AssetType { Unknown, Texture, Prefab, Scene, Script, Audio }

public sealed class AssetInfo
{
    public required string RelativePath { get; init; } // relative to the project's Assets folder, '/' separated
    public required string FullPath { get; init; }
    public AssetType Type { get; init; }
    public Texture2D? Texture { get; set; }
    public SoundEffect? Sound { get; set; }
    public IntPtr ThumbnailId { get; set; } = IntPtr.Zero;
    /// <summary>The file's on-disk write time when this AssetInfo was (re)imported — lets Refresh()/ImportFile
    /// tell an unchanged file apart from one that actually needs reimporting, so it never disposes a
    /// Texture2D (or unbinds an ImGui thumbnail) that's still current and possibly still in use this frame.</summary>
    public DateTime LastWriteTimeUtc { get; set; }
    public string DisplayName => System.IO.Path.GetFileNameWithoutExtension(RelativePath);

    /// <summary>The folder this asset lives in, relative to Assets ("" = Assets root itself).</summary>
    public string FolderPath => System.IO.Path.GetDirectoryName(RelativePath)?.Replace('\\', '/') ?? "";
}

/// <summary>
/// Scans the project's Assets folder and keeps an in-memory index of every importable file, plus every
/// folder (including empty ones — needed so a just-created empty folder still shows up before it has
/// any content). Everything the project has — textures, prefabs, *and scenes* — lives under Assets/,
/// matching how a Unity project only has one browsable root; there's no separate top-level Scenes or
/// Prefabs folder anymore.
///
/// PNG/JPG/BMP become Textures, loaded directly via Texture2D.FromStream — no content-pipeline build
/// step needed, so dropping a file in and seeing it appear is instant.
/// </summary>
public sealed class AssetDatabase : IDisposable
{
    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp" };

    // v1 supports WAV only — see AudioSource's doc comment for why (no content-pipeline build step needed,
    // consistent with how textures are loaded directly from PNG/JPG bytes). Compressed formats are a
    // natural future addition: add the extension here and a branch in ImportFile below, nothing else in
    // the asset pipeline needs to change.
    private static readonly HashSet<string> AudioExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".wav" };

    private readonly string _assetsRoot;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly ImGuiRenderer _imGui;
    private readonly Dictionary<string, AssetInfo> _assets = new();
    private readonly HashSet<string> _folders = new();
    private readonly ConcurrentQueue<string> _pendingChanges = new();
    private FileSystemWatcher? _watcher;

    public IReadOnlyCollection<AssetInfo> Assets => _assets.Values;
    public string AssetsRoot => _assetsRoot;

    public AssetDatabase(string projectPath, GraphicsDevice graphicsDevice, ImGuiRenderer imGui)
    {
        _assetsRoot = Directory.CreateDirectory(Path.Combine(projectPath, "Assets")).FullName;
        _graphicsDevice = graphicsDevice;
        _imGui = imGui;
        SetupWatcher();
    }

    private void SetupWatcher()
    {
        try
        {
            _watcher = new FileSystemWatcher(_assetsRoot)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName,
            };
            _watcher.Created += (_, e) => _pendingChanges.Enqueue(e.FullPath);
            _watcher.Changed += (_, e) => _pendingChanges.Enqueue(e.FullPath);
            _watcher.Renamed += (_, e) => _pendingChanges.Enqueue(e.FullPath);
            _watcher.Deleted += (_, e) => _pendingChanges.Enqueue(e.FullPath);
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception)
        {
            // watcher setup can fail on some platforms/filesystems (e.g. certain network drives) —
            // that just means new files need a manual Refresh() instead of auto-import.
            _watcher = null;
        }
    }

    /// <summary>Full rescan of the Assets folder (files AND folders). Call on startup, after Refresh button,
    /// and after any structural change (create folder/scene, move, save) since those are infrequent enough
    /// that a full rescan is simpler and cheap enough than tracking deltas.
    ///
    /// Deliberately does NOT dispose/reimport everything unconditionally — ImportFile below only touches an
    /// asset whose file actually changed on disk (new file, or a newer write time). Assets that didn't
    /// change keep their existing Texture2D and ImGui thumbnail binding completely untouched. This matters
    /// because Refresh() is called mid-frame from lots of places (after Save Scene, after Create Script,
    /// after a Content Browser move) — disposing every texture unconditionally used to invalidate/unbind
    /// textures that the very same frame's scene render or ImGui thumbnails had already queued draw calls
    /// for, crashing the app the moment those draw calls actually executed.</summary>
    public void Refresh()
    {
        if (!Directory.Exists(_assetsRoot))
        {
            // The Assets folder itself is gone — nothing on disk to preserve, so this is the one case
            // where dropping everything (and disposing every live texture with it) is actually correct.
            foreach (var info in _assets.Values)
            {
                if (info.ThumbnailId != IntPtr.Zero) _imGui.UnbindTexture(info.ThumbnailId);
                info.Texture?.Dispose();
                info.Sound?.Dispose();
            }
            _assets.Clear();
            _folders.Clear();
            return;
        }

        _folders.Clear();
        foreach (var dir in Directory.GetDirectories(_assetsRoot, "*", SearchOption.AllDirectories))
            _folders.Add(Path.GetRelativePath(_assetsRoot, dir).Replace('\\', '/'));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(_assetsRoot, "*.*", SearchOption.AllDirectories))
        {
            seen.Add(Path.GetRelativePath(_assetsRoot, file).Replace('\\', '/'));
            ImportFile(file);
        }

        // Anything previously tracked that no longer exists on disk (deleted, or moved away from this
        // path) gets dropped the same way a watcher Deleted event would.
        foreach (var stale in _assets.Keys.Where(k => !seen.Contains(k)).ToArray())
            RemoveByFullPath(Path.Combine(_assetsRoot, stale));
    }

    /// <summary>
    /// Drains filesystem-watcher events and (re)imports whatever changed. Must be called once per frame
    /// from the main thread — watcher events fire on a background thread, and creating a Texture2D requires
    /// the GraphicsDevice's owning thread.
    /// </summary>
    public void ProcessPendingChanges()
    {
        var seen = new HashSet<string>();
        bool structureMayHaveChanged = false;
        while (_pendingChanges.TryDequeue(out var path))
        {
            if (!seen.Add(path)) continue;
            if (Directory.Exists(path)) { structureMayHaveChanged = true; continue; }
            if (File.Exists(path)) ImportFile(path);
            else RemoveByFullPath(path);
        }
        // folders don't carry enough info in a single watcher event to update incrementally; a full
        // rescan on the rare "a directory appeared/disappeared" event is simplest and still cheap.
        if (structureMayHaveChanged) Refresh();
    }

    private void ImportFile(string fullPath)
    {
        var ext = Path.GetExtension(fullPath);
        var type = ImageExtensions.Contains(ext) ? AssetType.Texture
                 : AudioExtensions.Contains(ext) ? AssetType.Audio
                 : ext.Equals(".prefab", StringComparison.OrdinalIgnoreCase) ? AssetType.Prefab
                 : ext.Equals(".scene", StringComparison.OrdinalIgnoreCase) ? AssetType.Scene
                 : ext.Equals(".cs", StringComparison.OrdinalIgnoreCase) ? AssetType.Script
                 : AssetType.Unknown;
        if (type == AssetType.Unknown) return;

        var relative = Path.GetRelativePath(_assetsRoot, fullPath).Replace('\\', '/');
        var writeTimeUtc = File.GetLastWriteTimeUtc(fullPath);

        // Already imported and unchanged on disk — leave the existing AssetInfo (and its live Texture2D /
        // ImGui thumbnail binding, if any) completely alone. This is what makes Refresh() safe to call
        // mid-frame: the vast majority of assets hit this early-out on every Refresh(), since normally only
        // the one new/changed file needs anything done at all.
        if (_assets.TryGetValue(relative, out var existing) && existing.Type == type && existing.LastWriteTimeUtc == writeTimeUtc)
            return;

        // dispose the previous version of this asset (if any) before reimporting it
        if (_assets.TryGetValue(relative, out var old))
        {
            if (old.ThumbnailId != IntPtr.Zero) _imGui.UnbindTexture(old.ThumbnailId);
            old.Texture?.Dispose();
            old.Sound?.Dispose();
        }

        var info = new AssetInfo { RelativePath = relative, FullPath = fullPath, Type = type, LastWriteTimeUtc = writeTimeUtc };

        if (type == AssetType.Texture)
        {
            try
            {
                using var stream = File.OpenRead(fullPath);
                info.Texture = Texture2D.FromStream(_graphicsDevice, stream);
                info.ThumbnailId = _imGui.BindTexture(info.Texture);
            }
            catch (Exception)
            {
                // file might be corrupt, or still being written by another program (locked/partial) —
                // if it's the latter, the watcher will fire a further Changed event once the write finishes.
                return;
            }
        }
        else if (type == AssetType.Audio)
        {
            try
            {
                using var stream = File.OpenRead(fullPath);
                info.Sound = SoundEffect.FromStream(stream);
            }
            catch (Exception)
            {
                // same reasoning as the texture case above — corrupt/partial file, tolerate and move on.
                return;
            }
        }

        _assets[relative] = info;
    }

    private void RemoveByFullPath(string fullPath)
    {
        var relative = Path.GetRelativePath(_assetsRoot, fullPath).Replace('\\', '/');
        if (!_assets.TryGetValue(relative, out var info)) return;

        if (info.ThumbnailId != IntPtr.Zero) _imGui.UnbindTexture(info.ThumbnailId);
        info.Texture?.Dispose();
        info.Sound?.Dispose();
        _assets.Remove(relative);
    }

    public Texture2D? GetTexture(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return null;
        return _assets.TryGetValue(relativePath, out var info) ? info.Texture : null;
    }

    public SoundEffect? GetSound(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return null;
        return _assets.TryGetValue(relativePath, out var info) ? info.Sound : null;
    }

    public AssetInfo? Find(string relativePath) => _assets.GetValueOrDefault(relativePath);

    /// <summary>Returns the texture asset for one of the GameObject menu's 5 basic shapes, generating and
    /// importing Assets/Primitives/&lt;Shape&gt;.png the first time it's requested — see
    /// PrimitiveShapeGenerator's doc comment for why these are real file-backed assets rather than
    /// something purely in-memory. Every call after the first for a given shape just returns the
    /// already-imported asset.</summary>
    public AssetInfo GetOrCreatePrimitiveShape(PrimitiveShape shape)
    {
        var relativePath = PrimitiveShapeGenerator.GetOrCreateTexturePath(shape, _assetsRoot, _graphicsDevice);
        if (!_assets.ContainsKey(relativePath))
        {
            _folders.Add("Primitives");
            ImportFile(Path.Combine(_assetsRoot, relativePath));
        }
        return _assets[relativePath];
    }

    /// <summary>After loading or cloning a scene, every SpriteRenderer.Texture / AudioSource.Clip is null —
    /// only the *Path fields actually survive the round trip through JSON — so this re-links each one to
    /// its matching imported asset. Named generically (not ResolveSceneTextures) since it covers every
    /// asset-referencing component type; add a loop here whenever a new one is introduced.</summary>
    public void ResolveSceneAssets(Scene scene)
    {
        foreach (var go in scene.GameObjects)
        {
            foreach (var sr in go.GetComponents<SpriteRenderer>())
                if (sr.TexturePath != null)
                    sr.Texture = GetTexture(sr.TexturePath);
            foreach (var audio in go.GetComponents<AudioSource>())
                if (audio.ClipPath != null)
                    audio.Clip = GetSound(audio.ClipPath);
        }
    }

    // ---------------------------------------------------------------- folder browsing

    /// <summary>Immediate subfolders of <paramref name="folder"/> ("" = Assets root), name only (not full path).</summary>
    public IEnumerable<string> GetSubfolders(string folder)
    {
        string prefix = folder.Length == 0 ? "" : folder + "/";
        foreach (var f in _folders)
        {
            if (!f.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var remainder = f[prefix.Length..];
            if (remainder.Length == 0 || remainder.Contains('/')) continue; // not an immediate child
            yield return remainder;
        }
    }

    /// <summary>Assets directly inside <paramref name="folder"/> ("" = Assets root), not in subfolders.</summary>
    public IEnumerable<AssetInfo> GetAssetsInFolder(string folder)
        => _assets.Values.Where(a => a.FolderPath == folder);

    public void CreateFolder(string parentFolder, string name)
    {
        Directory.CreateDirectory(Path.Combine(_assetsRoot, parentFolder, name));
        Refresh();
    }

    /// <summary>Moves an already-imported asset into a different folder, keeping its file name. Returns
    /// false (without throwing) rather than overwriting an existing file, or if the target is where the
    /// asset already lives.</summary>
    public bool MoveAsset(string relativePath, string targetFolder)
    {
        if (!_assets.TryGetValue(relativePath, out var info)) return false;
        if (info.FolderPath == targetFolder) return false;

        var fileName = Path.GetFileName(info.FullPath);
        var destPath = Path.Combine(_assetsRoot, targetFolder, fileName);
        if (File.Exists(destPath)) return false;

        Directory.CreateDirectory(Path.Combine(_assetsRoot, targetFolder));
        File.Move(info.FullPath, destPath);
        Refresh();
        return true;
    }

    /// <summary>Moves an already-imported folder (and everything inside it) to become a child of
    /// <paramref name="targetFolder"/>. Returns false (without throwing) for a no-op move, a move that
    /// would nest the folder inside itself, or if something already exists at the destination.</summary>
    public bool MoveFolder(string folderRelativePath, string targetFolder)
    {
        var name = Path.GetFileName(folderRelativePath);
        var destPath = targetFolder.Length == 0 ? name : targetFolder + "/" + name;

        if (destPath == folderRelativePath) return false;
        if (targetFolder == folderRelativePath || targetFolder.StartsWith(folderRelativePath + "/", StringComparison.Ordinal))
            return false; // would move the folder inside itself or one of its own subfolders

        var sourceFull = Path.Combine(_assetsRoot, folderRelativePath);
        var destFull = Path.Combine(_assetsRoot, destPath);
        if (Directory.Exists(destFull)) return false;

        Directory.Move(sourceFull, destFull);
        Refresh();
        return true;
    }

    /// <summary>Writes a new, minimal default scene (a single "Main Camera") as a .scene file and imports it.
    /// Returns the relative asset path of the new scene.</summary>
    public string CreateScene(string parentFolder, string name)
    {
        var fileName = name.EndsWith(".scene", StringComparison.OrdinalIgnoreCase) ? name : name + ".scene";
        var fullPath = Path.Combine(_assetsRoot, parentFolder, fileName);

        var scene = new Scene(Path.GetFileNameWithoutExtension(fileName));
        var camera = scene.CreateGameObject("Main Camera");
        camera.AddComponent<Camera2D>();
        SceneSerializer.Save(scene, fullPath);

        Refresh();
        return Path.GetRelativePath(_assetsRoot, fullPath).Replace('\\', '/');
    }

    /// <summary>Writes a new .cs file from the default script template and imports it. Returns the relative asset path.</summary>
    public string CreateScript(string parentFolder, string name)
    {
        var className = ScriptTemplate.SanitizeClassName(name);
        var fileName = className + ".cs";
        var fullPath = Path.Combine(_assetsRoot, parentFolder, fileName);

        File.WriteAllText(fullPath, ScriptTemplate.Generate(className));

        Refresh();
        return Path.GetRelativePath(_assetsRoot, fullPath).Replace('\\', '/');
    }

    /// <summary>Deletes a single asset's file from disk and drops it from the index, disposing its
    /// Texture2D and unbinding its ImGui thumbnail (if any). Callers MUST only call this at a point in the
    /// frame *before* this asset's tile could possibly be drawn — e.g. the very top of
    /// BottomPanel.Draw(), never from inside a click/menu handler mid-frame — otherwise a tile
    /// already drawn earlier that same frame would still reference the texture id this just unbound, and
    /// the renderer throws the moment it tries to render that already-queued draw command (the exact
    /// mechanism Refresh() was fixed for above). Returns false, without throwing, if the asset isn't
    /// tracked or its file couldn't be deleted (e.g. locked by another program) — the index is left
    /// untouched in that case so it never disagrees with what's actually on disk.</summary>
    public bool DeleteAsset(string relativePath)
    {
        if (!_assets.TryGetValue(relativePath, out var info)) return false;

        try
        {
            if (File.Exists(info.FullPath)) File.Delete(info.FullPath);
        }
        catch (Exception)
        {
            return false;
        }

        if (info.ThumbnailId != IntPtr.Zero) _imGui.UnbindTexture(info.ThumbnailId);
        info.Texture?.Dispose();
        info.Sound?.Dispose();
        _assets.Remove(relativePath);
        return true;
    }

    /// <summary>Deletes a folder — and everything inside it, recursively — from disk, and drops every asset
    /// that lived under it from the index (disposing each one's Texture2D/thumbnail the same as DeleteAsset).
    /// Same mid-frame timing requirement as DeleteAsset: only ever call this before this frame has drawn
    /// any tile that could be inside the folder. Returns false, without throwing, if the folder isn't
    /// tracked or couldn't be deleted.</summary>
    public bool DeleteFolder(string relativePath)
    {
        if (!_folders.Contains(relativePath)) return false;

        var fullPath = Path.Combine(_assetsRoot, relativePath);
        try
        {
            if (Directory.Exists(fullPath)) Directory.Delete(fullPath, recursive: true);
        }
        catch (Exception)
        {
            return false;
        }

        var prefix = relativePath + "/";
        foreach (var key in _assets.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
        {
            var info = _assets[key];
            if (info.ThumbnailId != IntPtr.Zero) _imGui.UnbindTexture(info.ThumbnailId);
            info.Texture?.Dispose();
            info.Sound?.Dispose();
            _assets.Remove(key);
        }

        _folders.RemoveWhere(f => f == relativePath || f.StartsWith(prefix, StringComparison.Ordinal));
        return true;
    }

    /// <summary>Finds the .cs asset whose file name matches a script class name (the same "file name = class
    /// name" convention C# already enforces for public types) — used by the Inspector to show a read-only
    /// preview of a Script component's source.</summary>
    public AssetInfo? FindScriptAsset(string typeName) =>
        _assets.Values.FirstOrDefault(a => a.Type == AssetType.Script && a.DisplayName == typeName);

    /// <summary>
    /// Copies a file from outside the project (e.g. dropped in from the OS file manager) into
    /// <paramref name="targetFolder"/> (relative to Assets/) and imports just that one file — deliberately
    /// not a full Refresh(), so every other already-loaded texture is left completely untouched.
    /// Returns the new asset's relative path. Only image files are supported.
    /// </summary>
    public string ImportExternalFile(string sourceFilePath, string targetFolder)
    {
        var ext = Path.GetExtension(sourceFilePath);
        if (!ImageExtensions.Contains(ext))
            throw new NotSupportedException($"'{ext}' files can't be imported this way — only images (.png, .jpg, .jpeg, .bmp).");

        var targetDir = Path.Combine(_assetsRoot, targetFolder);
        Directory.CreateDirectory(targetDir);

        var baseName = Path.GetFileNameWithoutExtension(sourceFilePath);
        var destPath = Path.Combine(targetDir, baseName + ext);
        int suffix = 1;
        while (File.Exists(destPath))
        {
            destPath = Path.Combine(targetDir, $"{baseName} ({suffix}){ext}");
            suffix++;
        }

        File.Copy(sourceFilePath, destPath);

        // make sure the folder the file landed in is tracked even if it was just created above
        var relativeFolder = Path.GetRelativePath(_assetsRoot, targetDir).Replace('\\', '/');
        if (relativeFolder != ".") _folders.Add(relativeFolder);

        ImportFile(destPath);
        return Path.GetRelativePath(_assetsRoot, destPath).Replace('\\', '/');
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        foreach (var info in _assets.Values)
        {
            if (info.ThumbnailId != IntPtr.Zero) _imGui.UnbindTexture(info.ThumbnailId);
            info.Texture?.Dispose();
            info.Sound?.Dispose();
        }
    }
}
