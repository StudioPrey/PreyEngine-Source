using Microsoft.Xna.Framework;
using MyEngine.Core.Audio;
using MyEngine.Core.Components;
using MyEngine.Core.SceneSystem;
using MyEngine.Editor.UndoSystem;

namespace MyEngine.Editor;

public static class AssetDropHandler
{
    /// <summary>Creates a new GameObject with a SpriteRenderer pointing at <paramref name="texturePath"/>, at
    /// <paramref name="worldPosition"/> (defaults to the origin when the drop target has no spatial context,
    /// e.g. the Hierarchy panel). Selects it and records an undo entry.</summary>
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
        audio.ClipPath = clipPath;
        audio.Clip = state.Assets.GetSound(clipPath);
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

    /// <summary>Instantiates the prefab at <paramref name="prefabRelativePath"/> (a Content Browser asset path)
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
}
