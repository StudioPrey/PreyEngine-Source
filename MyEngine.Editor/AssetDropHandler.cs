using Microsoft.Xna.Framework;
using MyEngine.Core.Audio;
using MyEngine.Core.Components;
using MyEngine.Core.ECS;
using MyEngine.Core.SceneSystem;
using MyEngine.Editor.UndoSystem;

namespace MyEngine.Editor;

public static class AssetDropHandler
{
    /// <summary>Creates a new GameObject with a SpriteRenderer pointing at <paramref name="texturePath"/>, at
    /// <paramref name="worldPosition"/> (defaults to the origin when the drop target has no spatial context,
    /// e.g. the Outline panel). Selects it and records an undo entry.</summary>
    public static void CreateSpriteFromTexture(EditorState state, string texturePath, Vector2? worldPosition = null)
    {
        var go = state.ActiveScene.CreateGameObject(Path.GetFileNameWithoutExtension(texturePath));
        var sr = go.AddComponent<SpriteRenderer>();
        sr.TexturePath = texturePath;
        sr.Texture = state.Assets.GetTexture(texturePath);
        go.Transform.Position = worldPosition ?? Vector2.Zero;

        state.SelectGameObject(go);
        state.LogMessage($"Placed sprite '{go.Name}'.");

        var command = CreateDeleteGameObjectCommand.ForCreate(
            $"Create {go.Name}", state.ActiveScene, state.Assets.ResolveSceneAssets, go);
        state.Undo.Push(command);
    }

    /// <summary>Creates a new GameObject with an AudioSource pointing at <paramref name="clipPath"/>, at
    /// <paramref name="worldPosition"/> — the Viewport's "drag a .wav in, get a speaker icon" workflow (see
    /// AudioIconRenderer). Play On Start defaults to on, since dropping a clip straight into the scene is
    /// almost always "I want to hear this when I press Play", not "I want to wire this up from a script
    /// later" — the latter is one checkbox away in the Inspector if that's what's actually wanted.</summary>
    public static void CreateAudioSourceFromClip(EditorState state, string clipPath, Vector2? worldPosition = null)
    {
        var go = state.ActiveScene.CreateGameObject(Path.GetFileNameWithoutExtension(clipPath));
        var audio = go.AddComponent<AudioSource>();
        audio.ClipPath = clipPath; // resolves Clip on its own now — see ClipPath's doc comment
        audio.PlayOnStart = true;
        go.Transform.Position = worldPosition ?? Vector2.Zero;

        state.SelectGameObject(go);
        state.LogMessage($"Placed audio source '{go.Name}'.");

        var command = CreateDeleteGameObjectCommand.ForCreate(
            $"Create {go.Name}", state.ActiveScene, state.Assets.ResolveSceneAssets, go);
        state.Undo.Push(command);
    }

    /// <summary>Creates a new, empty GameObject (Transform only) — the GameObject menu's "Empty
    /// GameObject" entry. Selects it and records an undo entry.</summary>
    public static void CreateEmptyGameObject(EditorState state)
    {
        var go = state.ActiveScene.CreateGameObject("GameObject");
        state.SelectGameObject(go);
        state.LogMessage($"Created '{go.Name}'.");

        var command = CreateDeleteGameObjectCommand.ForCreate(
            $"Create {go.Name}", state.ActiveScene, state.Assets.ResolveSceneAssets, go);
        state.Undo.Push(command);
    }

    /// <summary>Creates a new GameObject with a SpriteRenderer showing one of the GameObject menu's 5 basic
    /// geometric shapes — see PrimitiveShapeGenerator for how the shape becomes an actual texture asset.
    /// Selects it and records an undo entry.</summary>
    public static void CreatePrimitiveShape(EditorState state, PrimitiveShape shape)
    {
        var asset = state.Assets.GetOrCreatePrimitiveShape(shape);
        var go = state.ActiveScene.CreateGameObject(shape.ToString());
        var sr = go.AddComponent<SpriteRenderer>();
        sr.TexturePath = asset.RelativePath;
        sr.Texture = asset.Texture;

        state.SelectGameObject(go);
        state.LogMessage($"Created '{go.Name}'.");

        var command = CreateDeleteGameObjectCommand.ForCreate(
            $"Create {go.Name}", state.ActiveScene, state.Assets.ResolveSceneAssets, go);
        state.Undo.Push(command);
    }

    /// <summary>Instantiates the prefab at <paramref name="prefabRelativePath"/> (a project asset path)
    /// into the active scene, optionally at a specific world position. Selects it and records an undo entry.</summary>
    public static void InstantiatePrefab(EditorState state, string prefabRelativePath, Vector2? worldPosition = null)
    {
        var asset = state.Assets.Find(prefabRelativePath);
        if (asset == null)
        {
            state.LogMessage($"ERROR: prefab asset not found: {prefabRelativePath}");
            return;
        }

        try
        {
            var root = PrefabSerializer.Instantiate(asset.FullPath, state.ActiveScene, worldPosition: worldPosition);
            state.SelectGameObject(root);
            state.LogMessage($"Instantiated prefab '{root.Name}'.");

            var command = CreateDeleteGameObjectCommand.ForCreate(
                $"Instantiate {root.Name}", state.ActiveScene, state.Assets.ResolveSceneAssets, root);
            state.Undo.Push(command);
        }
        catch (Exception ex)
        {
            state.LogMessage($"ERROR instantiating prefab: {ex.Message}");
        }
    }

    /// <summary>Creates a new, empty child GameObject under <paramref name="parent"/> — the Outline
    /// right-click menu's "Create Empty Child" entry, as opposed to CreateEmptyGameObject's root-level
    /// "Create Empty" (see the dual-path component-addition architecture: the Inspector's "+ Add Component"
    /// adds directly to the selected object; this "as Child" family instead makes a new, separately-named
    /// object, so e.g. three AudioSources meant for different sounds can be told apart by name instead of
    /// sitting unlabeled on one GameObject). Sits exactly at parent's position (a fresh Transform's local
    /// position is already Vector2.Zero, and keepWorldPosition: false leaves it there rather than trying to
    /// preserve a world position that was never meaningfully set). Selects it and records an undo entry.</summary>
    public static void CreateEmptyChild(EditorState state, GameObject parent)
    {
        var go = state.ActiveScene.CreateGameObject("GameObject");
        go.SetParent(parent, keepWorldPosition: false);

        state.SelectGameObject(go);
        state.LogMessage($"Created '{go.Name}' under '{parent.Name}'.");

        var command = CreateDeleteGameObjectCommand.ForCreate(
            $"Create {go.Name}", state.ActiveScene, state.Assets.ResolveSceneAssets, go);
        state.Undo.Push(command);
    }

    /// <summary>Creates a new GameObject with a single TComponent already attached — the generic engine
    /// behind every Outline right-click "X as Child" entry (Audio Source as Child, Sprite Animation as
    /// Child, ...) and every "create at root" entry (the Outline's empty-space menu and the Blocks panel's
    /// Built-in tiles). One generic method instead of a hand-written copy per component type, so adding the
    /// next one is a single new menu item calling this, not a new parallel block to keep in sync.
    /// <paramref name="parent"/> null means root-level — no SetParent call at all, exactly like
    /// CreateEmptyGameObject plus a component. <paramref name="childName"/> is the new object's default,
    /// user-renamable name (e.g. "Audio Source"); <paramref name="configure"/> runs right after AddComponent
    /// so a caller can set up whatever the component needs beyond its own defaults (ClipPath, PlayOnStart,
    /// ...) before the single undo snapshot below captures the finished result. Selects the new object and
    /// records ONE undo entry covering the object and its component together — CreateDeleteGameObjectCommand
    /// already snapshots a GameObject's full component state, not just its bare existence, and already
    /// restores the parent link on Redo (see its constructor and Restore()).</summary>
    public static void CreateComponentAsChild<TComponent>(
        EditorState state, GameObject? parent, string childName, Action<TComponent>? configure = null)
        where TComponent : Component, new()
    {
        var go = state.ActiveScene.CreateGameObject(childName);
        if (parent != null) go.SetParent(parent, keepWorldPosition: false);
        var component = go.AddComponent<TComponent>();
        configure?.Invoke(component);

        state.SelectGameObject(go);
        state.LogMessage(parent != null ? $"Created '{go.Name}' under '{parent.Name}'." : $"Created '{go.Name}'.");

        var command = CreateDeleteGameObjectCommand.ForCreate(
            $"Create {go.Name}", state.ActiveScene, state.Assets.ResolveSceneAssets, go);
        state.Undo.Push(command);
    }
}
