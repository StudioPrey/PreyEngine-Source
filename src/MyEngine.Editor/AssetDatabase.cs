using System.Collections.Concurrent;
using Microsoft.Xna.Framework.Graphics;
using MyEngine.Core.Components;
using MyEngine.Core.SceneSystem;
using MyEngine.Editor.Scripting;
using MyEngine.EditorFramework;

namespace MyEngine.Editor;

public enum AssetType { Unknown, Texture, Prefab, Scene, Script }

public sealed class AssetInfo
{
    public required string RelativePath { get; init; } // relative to the project's Assets folder, '/' separated
    public required string FullPath { get; init; }
    public AssetType Type { get; init; }
    public Texture2D? Texture { get; set; }
    public IntPtr ThumbnailId { get; set; } = IntPtr.Zero;
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
    /// and after any structural change (create folder/scene) since those are infrequent enough that a full
    /// rescan is simpler and cheap enough than tracking deltas.</summary>
    public void Refresh()
    {
        foreach (var info in _assets.Values)
        {
            if (info.ThumbnailId != IntPtr.Zero) _imGui.UnbindTexture(info.ThumbnailId);
            info.Texture?.Dispose();
        }
        _assets.Clear();
        _folders.Clear();

        if (!Directory.Exists(_assetsRoot)) return;

        foreach (var dir in Directory.GetDirectories(_assetsRoot, "*", SearchOption.AllDirectories))
            _folders.Add(Path.GetRelativePath(_assetsRoot, dir).Replace('\\', '/'));

        foreach (var file in Directory.GetFiles(_assetsRoot, "*.*", SearchOption.AllDirectories))
            ImportFile(file);
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
                 : ext.Equals(".prefab", StringComparison.OrdinalIgnoreCase) ? AssetType.Prefab
                 : ext.Equals(".scene", StringComparison.OrdinalIgnoreCase) ? AssetType.Scene
                 : ext.Equals(".cs", StringComparison.OrdinalIgnoreCase) ? AssetType.Script
                 : AssetType.Unknown;
        if (type == AssetType.Unknown) return;

        var relative = Path.GetRelativePath(_assetsRoot, fullPath).Replace('\\', '/');

        // dispose the previous version of this asset (if any) before reimporting it
        if (_assets.TryGetValue(relative, out var old))
        {
            if (old.ThumbnailId != IntPtr.Zero) _imGui.UnbindTexture(old.ThumbnailId);
            old.Texture?.Dispose();
        }

        var info = new AssetInfo { RelativePath = relative, FullPath = fullPath, Type = type };

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

        _assets[relative] = info;
    }

    private void RemoveByFullPath(string fullPath)
    {
        var relative = Path.GetRelativePath(_assetsRoot, fullPath).Replace('\\', '/');
        if (!_assets.TryGetValue(relative, out var info)) return;

        if (info.ThumbnailId != IntPtr.Zero) _imGui.UnbindTexture(info.ThumbnailId);
        info.Texture?.Dispose();
        _assets.Remove(relative);
    }

    public Texture2D? GetTexture(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return null;
        return _assets.TryGetValue(relativePath, out var info) ? info.Texture : null;
    }

    public AssetInfo? Find(string relativePath) => _assets.GetValueOrDefault(relativePath);

    /// <summary>After loading or cloning a scene, every SpriteRenderer.Texture is null (only TexturePath survives
    /// the round trip through JSON) — this re-links each sprite to the matching imported asset.</summary>
    public void ResolveSceneTextures(Scene scene)
    {
        foreach (var go in scene.GameObjects)
            foreach (var sr in go.GetComponents<SpriteRenderer>())
                if (sr.TexturePath != null)
                    sr.Texture = GetTexture(sr.TexturePath);
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
        }
    }
}
