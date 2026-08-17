using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Xna.Framework;
using MyEngine.Core.Components;
using MyEngine.Core.ECS;
using MyEngine.Core.Scripting;

namespace MyEngine.Core.SceneSystem;

// --- Plain DTOs, decoupled from the runtime types so the file format stays stable
// even as engine internals change. Add one *Data class + one branch in ToGameObjectData/
// ApplyComponentData whenever you add a new serializable component type. ---

public sealed class SceneData
{
    public string Name { get; set; } = "Untitled Scene";
    public List<GameObjectData> Objects { get; set; } = new();
}

public sealed class GameObjectData
{
    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = "GameObject";
    public bool Enabled { get; set; } = true;
    public string? SourcePrefabPath { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Rotation { get; set; }
    public float ScaleX { get; set; } = 1;
    public float ScaleY { get; set; } = 1;
    public List<ComponentData> Components { get; set; } = new();
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SpriteRendererData), "SpriteRenderer")]
[JsonDerivedType(typeof(Camera2DData), "Camera2D")]
[JsonDerivedType(typeof(ScriptComponentData), "Script")]
public abstract class ComponentData { }

public sealed class SpriteRendererData : ComponentData
{
    public string? TexturePath { get; set; }
    public byte R { get; set; } = 255;
    public byte G { get; set; } = 255;
    public byte B { get; set; } = 255;
    public byte A { get; set; } = 255;
    public float SizeX { get; set; } = 64;
    public float SizeY { get; set; } = 64;
    public int SortingOrder { get; set; }
}

public sealed class Camera2DData : ComponentData
{
    public float Zoom { get; set; } = 1f;
    public bool IsActive { get; set; } = true;
    public Guid? FollowTargetId { get; set; }
    public float FollowSmoothing { get; set; } = 0.15f;
    public float FollowOffsetX { get; set; }
    public float FollowOffsetY { get; set; }
}

/// <summary>
/// Generic container for a user script component. Core doesn't know user script classes (they're
/// compiled per-project, at runtime) so this can't be one DTO per script the way SpriteRendererData is
/// one DTO for SpriteRenderer — instead it carries the script's class name (resolved back to a compiled
/// Type via ScriptRegistry) and a name-to-string map of its public field values.
/// See ScriptSerialization for what field types are actually supported in this first version.
/// </summary>
public sealed class ScriptComponentData : ComponentData
{
    public string TypeName { get; set; } = "";
    public Dictionary<string, string> Fields { get; set; } = new();
}

/// <summary>
/// Converts between the runtime Scene/GameObject graph and a flat, human-readable JSON file.
/// Also backs Prefabs (see PrefabSerializer) and the in-memory clone used for Play Mode, since
/// both are really "turn a GameObject subtree into data and back" operations.
/// Texture loading is intentionally left to the caller (via AssetDatabase.ResolveSceneTextures)
/// because Core doesn't know about the editor's asset system.
/// </summary>
public static class SceneSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        IncludeFields = false,
    };

    // ---------------------------------------------------------------- GameObject <-> Data

    public static GameObjectData ToGameObjectData(GameObject go)
    {
        var data = new GameObjectData
        {
            Id = go.Id,
            ParentId = go.Parent?.Id,
            Name = go.Name,
            Enabled = go.Enabled,
            SourcePrefabPath = go.SourcePrefabPath,
            X = go.Transform.LocalPosition.X,
            Y = go.Transform.LocalPosition.Y,
            Rotation = go.Transform.LocalRotation,
            ScaleX = go.Transform.LocalScale.X,
            ScaleY = go.Transform.LocalScale.Y,
        };

        foreach (var c in go.Components)
        {
            switch (c)
            {
                case SpriteRenderer sr:
                    data.Components.Add(new SpriteRendererData
                    {
                        TexturePath = sr.TexturePath,
                        R = sr.Color.R,
                        G = sr.Color.G,
                        B = sr.Color.B,
                        A = sr.Color.A,
                        SizeX = sr.Size.X,
                        SizeY = sr.Size.Y,
                        SortingOrder = sr.SortingOrder,
                    });
                    break;
                case Camera2D cam:
                    data.Components.Add(new Camera2DData
                    {
                        Zoom = cam.Zoom,
                        IsActive = cam.IsActive,
                        FollowTargetId = cam.FollowTarget?.Id,
                        FollowSmoothing = cam.FollowSmoothing,
                        FollowOffsetX = cam.FollowOffset.X,
                        FollowOffsetY = cam.FollowOffset.Y,
                    });
                    break;
                case Script script:
                    data.Components.Add(ScriptSerialization.Capture(script));
                    break;
                // Transform is implicit and already captured above; skip it here.
            }
        }

        return data;
    }

    private static void ApplyComponentData(GameObject go, ComponentData cData)
    {
        switch (cData)
        {
            case SpriteRendererData sr:
                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.TexturePath = sr.TexturePath;
                renderer.Color = new Color(sr.R, sr.G, sr.B, sr.A);
                renderer.Size = new Vector2(sr.SizeX, sr.SizeY);
                renderer.SortingOrder = sr.SortingOrder;
                break;
            case Camera2DData cam:
                var camera = go.AddComponent<Camera2D>();
                camera.Zoom = cam.Zoom;
                camera.IsActive = cam.IsActive;
                camera.FollowSmoothing = cam.FollowSmoothing;
                camera.FollowOffset = new Vector2(cam.FollowOffsetX, cam.FollowOffsetY);
                break;
            case ScriptComponentData scriptData:
                var scriptType = ScriptRegistry.Find(scriptData.TypeName);
                if (scriptType == null) break; // "missing script" — skip rather than fail the whole load
                var script = (Script)go.AddComponent(scriptType);
                ScriptSerialization.Apply(script, scriptData);
                break;
        }
    }

    /// <summary>
    /// Creates a GameObject for every entry in <paramref name="objects"/> inside <paramref name="targetScene"/>,
    /// then wires up parenting and cross-references (e.g. Camera2D.FollowTarget) in a second pass.
    /// When <paramref name="preserveIds"/> is true, each new GameObject keeps the Id from the data (used when
    /// loading a scene, or cloning it for Play Mode, so external references and selection stay valid).
    /// When false, every object gets a brand new Id (used for prefab instantiation, so you can drop the same
    /// prefab into a scene multiple times without Id collisions).
    /// Returns a lookup from each entry's *original* Id (as written in the data) to the GameObject created for it.
    /// </summary>
    public static Dictionary<Guid, GameObject> BuildIntoScene(
        IReadOnlyList<GameObjectData> objects, Scene targetScene, bool preserveIds)
    {
        var lookup = new Dictionary<Guid, GameObject>();

        foreach (var goData in objects)
        {
            var id = preserveIds ? goData.Id : Guid.NewGuid();
            var go = targetScene.CreateGameObject(goData.Name, id);
            go.Enabled = goData.Enabled;
            go.SourcePrefabPath = goData.SourcePrefabPath;
            go.Transform.LocalPosition = new Vector2(goData.X, goData.Y);
            go.Transform.LocalRotation = goData.Rotation;
            go.Transform.LocalScale = new Vector2(goData.ScaleX, goData.ScaleY);

            foreach (var cData in goData.Components)
                ApplyComponentData(go, cData);

            lookup[goData.Id] = go;
        }

        foreach (var goData in objects)
        {
            var go = lookup[goData.Id];

            if (goData.ParentId is { } parentId && lookup.TryGetValue(parentId, out var parent))
                go.SetParent(parent, keepWorldPosition: false);

            foreach (var cData in goData.Components)
            {
                if (cData is Camera2DData { FollowTargetId: { } targetId } &&
                    lookup.TryGetValue(targetId, out var target))
                {
                    var camera = go.GetComponent<Camera2D>();
                    if (camera != null) camera.FollowTarget = target;
                }
            }
        }

        return lookup;
    }

    // ---------------------------------------------------------------- Scene <-> Data

    public static SceneData ToData(Scene scene)
    {
        var data = new SceneData { Name = scene.Name };
        foreach (var go in scene.GameObjects)
            data.Objects.Add(ToGameObjectData(go));
        return data;
    }

    public static Scene FromData(SceneData data)
    {
        var scene = new Scene(data.Name);
        BuildIntoScene(data.Objects, scene, preserveIds: true);
        return scene;
    }

    /// <summary>Deep-clones a scene in memory (same Ids preserved) — used for Play Mode.</summary>
    public static Scene Clone(Scene scene) => FromData(ToData(scene));

    public static void Save(Scene scene, string path)
    {
        var json = JsonSerializer.Serialize(ToData(scene), Options);
        File.WriteAllText(path, json);
    }

    public static Scene Load(string path)
    {
        var json = File.ReadAllText(path);
        var data = JsonSerializer.Deserialize<SceneData>(json, Options)
                   ?? throw new InvalidDataException($"Could not parse scene file: {path}");
        return FromData(data);
    }
}
