using System.Text.Json;
using Microsoft.Xna.Framework;
using MyEngine.Core.ECS;

namespace MyEngine.Core.SceneSystem;

/// <summary>
/// A "prefab" is a small, self-contained SceneData capturing one GameObject and all of its
/// descendants (objects[0] is always the root). Saving/loading reuses the exact same DTOs as
/// full scenes, so anything the scene format can represent, a prefab can too.
///
/// Every instance remembers which .prefab file it came from (GameObject.SourcePrefabPath), which
/// enables ApplyToPrefab/RevertToPrefab below. This is still not a full nested-override system like
/// Unity's (there's no per-property override tracking, and ApplyToPrefab only updates the source file —
/// it does not retroactively touch other instances already placed in scenes) — see the editor's README
/// roadmap for that. What it does give you: a working "push my changes back" / "discard my changes"
/// pair, which covers the common single-instance-at-a-time editing workflow.
/// </summary>
public static class PrefabSerializer
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Captures <paramref name="root"/> and all of its descendants into prefab data.</summary>
    public static SceneData CreateFromGameObject(GameObject root, string? prefabName = null)
    {
        var data = new SceneData { Name = prefabName ?? root.Name };

        void Visit(GameObject go)
        {
            data.Objects.Add(SceneSerializer.ToGameObjectData(go));
            foreach (var child in go.Transform.Children)
                Visit(child.Owner);
        }

        Visit(root);
        return data;
    }

    /// <summary>Writes <paramref name="root"/> out as a .prefab file, and marks it as an instance of the
    /// prefab it just created (matching the "drag into Project window" behavior in other editors).</summary>
    public static void Save(GameObject root, string path, string? prefabName = null)
    {
        var data = CreateFromGameObject(root, prefabName ?? Path.GetFileNameWithoutExtension(path));
        File.WriteAllText(path, JsonSerializer.Serialize(data, Options));
        root.SourcePrefabPath = path;
    }

    public static SceneData LoadData(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<SceneData>(json, Options)
               ?? throw new InvalidDataException($"Could not parse prefab file: {path}");
    }

    /// <summary>
    /// Creates a fresh instance of the prefab inside <paramref name="targetScene"/> (every object gets
    /// a brand new Id, so the same prefab can be dropped in multiple times). Returns the new root.
    /// </summary>
    public static GameObject Instantiate(SceneData prefabData, Scene targetScene, Transform? parent = null,
        Vector2? worldPosition = null, string? sourcePath = null)
    {
        if (prefabData.Objects.Count == 0)
            throw new InvalidOperationException("Prefab has no objects to instantiate.");

        var lookup = SceneSerializer.BuildIntoScene(prefabData.Objects, targetScene, preserveIds: false);
        var root = lookup[prefabData.Objects[0].Id];

        if (sourcePath != null)
            root.SourcePrefabPath = sourcePath;

        if (parent != null)
            root.Transform.SetParent(parent, keepWorldPosition: false);

        if (worldPosition.HasValue)
            root.Transform.Position = worldPosition.Value;

        return root;
    }

    public static GameObject Instantiate(string prefabPath, Scene targetScene, Transform? parent = null, Vector2? worldPosition = null)
        => Instantiate(LoadData(prefabPath), targetScene, parent, worldPosition, sourcePath: prefabPath);

    /// <summary>Re-saves the prefab file from this instance's current state. A simplified "Apply Overrides" —
    /// it updates the source file for future instantiations, but does not touch other instances already in scenes.</summary>
    public static void ApplyToPrefab(GameObject instanceRoot)
    {
        var path = instanceRoot.SourcePrefabPath
            ?? throw new InvalidOperationException($"'{instanceRoot.Name}' is not a prefab instance.");
        Save(instanceRoot, path, Path.GetFileNameWithoutExtension(path));
    }

    /// <summary>Discards local edits by destroying and re-instantiating this object fresh from its source
    /// prefab file, in the same place in the hierarchy (position/rotation/scale are kept; everything else —
    /// components, children — is reset to match the prefab file).</summary>
    public static GameObject RevertToPrefab(GameObject instanceRoot, Scene scene)
    {
        var path = instanceRoot.SourcePrefabPath
            ?? throw new InvalidOperationException($"'{instanceRoot.Name}' is not a prefab instance.");

        var parent = instanceRoot.Transform.Parent;
        var position = instanceRoot.Transform.LocalPosition;
        var rotation = instanceRoot.Transform.LocalRotation;
        var scale = instanceRoot.Transform.LocalScale;

        scene.Destroy(instanceRoot);

        var fresh = Instantiate(path, scene, parent);
        fresh.Transform.LocalPosition = position;
        fresh.Transform.LocalRotation = rotation;
        fresh.Transform.LocalScale = scale;
        return fresh;
    }
}
