using MyEngine.Core.Animation;
using MyEngine.Core.Audio;
using MyEngine.Core.ECS;
using MyEngine.Core.Scripting;
using MyEngine.Core.SceneSystem;
using MyEngine.Editor.UndoSystem;

namespace MyEngine.Editor;

public enum GizmoMode { Move, Rotate, Scale }
public enum GizmoSpace { World, Local }

public enum PendingCreateKind { Folder, Scene, Script }

/// <summary>Transient "type a name for the thing you just asked to create" state for the Content Browser,
/// mirroring the inline-rename box Unity shows immediately after Assets > Create > ...</summary>
public sealed class PendingCreateItem
{
    public required PendingCreateKind Kind { get; init; }
    public string NameBuffer = "";
    public bool FocusRequested = true;
}

/// <summary>Set when the user picks "Delete" on a Content Browser tile; ContentBrowserPanel shows a
/// confirmation for it every frame until Confirmed is set. The actual file deletion only ever runs at
/// the very start of the *next* frame's Draw() (see ContentBrowserPanel.ProcessPendingDelete) — never in
/// the same frame the confirmation was clicked, since this tile's icon may already have been drawn
/// earlier that same frame, and disposing its texture/thumbnail binding before that frame finishes
/// rendering is exactly what used to crash the engine on Save/Create (stale texture id already queued
/// in a draw call).</summary>
public sealed class PendingDeleteItem
{
    public required string RelativePath { get; init; }
    public required string DisplayName { get; init; }
    public required bool IsFolder { get; init; }
    public bool Confirmed { get; set; }
}

/// <summary>One entry in the scene tab strip above the Viewport.</summary>
public sealed class SceneTab
{
    public required string Name { get; set; }
    public required string Path { get; init; }
}

/// <summary>
/// Everything the editor panels need to share: which scene is open (and its live Play Mode copy),
/// what's selected, where the project lives on disk, the asset index, and editor-only tool state
/// (undo history, gizmo mode/space, grid, content browser navigation).
/// </summary>
public sealed class EditorState
{
    public string ProjectPath { get; }
    public AssetDatabase Assets { get; }
    public UndoStack Undo { get; } = new();

    /// <summary>The scene as it exists on disk / as the user is editing it.</summary>
    public Scene EditScene { get; set; }

    /// <summary>A throwaway clone that only exists while Play Mode is active. Never saved.</summary>
    public Scene? PlayScene { get; private set; }

    /// <summary>The scene panels should actually read/draw: the play clone if playing, otherwise the edit scene.</summary>
    public Scene ActiveScene => PlayScene ?? EditScene;

    public bool IsPlaying => PlayScene != null;

    public string? CurrentScenePath { get; set; }

    /// <summary>Currently selected GameObject in the Hierarchy/Viewport, if any.</summary>
    public GameObject? Selected { get; set; }

    /// <summary>Currently selected asset in the Content Browser, if any. Selecting a GameObject clears this and vice versa.</summary>
    public AssetInfo? SelectedAsset { get; set; }

    /// <summary>Which clip is showing in the Inspector's Sprite Animation section for the currently selected
    /// GameObject. Editor-UI-only — never serialized. InspectorPanel falls back to the first clip whenever
    /// this doesn't match a clip that still exists (including right after selecting a different GameObject
    /// entirely), so nothing needs to explicitly reset it on selection change.</summary>
    public string? SelectedAnimationClip { get; set; }

    /// <summary>The SpriteAnimation currently being scrubbed by the Inspector's animation preview ("▶
    /// Preview" button), if any — null the rest of the time. InspectorPanel.Draw stops and clears this the
    /// moment its owner stops being the selected GameObject, so a preview can never keep silently ticking
    /// in the background after you've navigated away from it.</summary>
    public SpriteAnimation? PreviewingAnimation { get; set; }

    /// <summary>Same idea as PreviewingAnimation, for the Inspector's AudioSource "▶ Preview" button.</summary>
    public AudioSource? PreviewingAudio { get; set; }

    public GizmoMode Gizmo { get; set; } = GizmoMode.Move;
    public GizmoSpace GizmoSpace { get; set; } = GizmoSpace.World;
    public bool ShowGrid { get; set; } = true;

    /// <summary>Whether Collider2D outlines are overlaid in the Viewport — the physics equivalent of
    /// Unity's/Godot's "show colliders" gizmo toggle.</summary>
    public bool ShowColliders { get; set; } = true;

    /// <summary>Current folder the Content Browser is showing, relative to Assets/ ("" = Assets root).</summary>
    public string ContentBrowserFolder { get; set; } = "";
    public PendingCreateItem? PendingCreate { get; set; }
    public PendingDeleteItem? PendingDelete { get; set; }

    /// <summary>Scenes opened this session, shown as a tab strip above the Viewport for quick manual switching.</summary>
    public List<SceneTab> SceneTabs { get; } = new();

    public List<string> Log { get; } = new();

    /// <summary>Set by anything that just changed a script file (Content Browser's Create Script, or the
    /// editor window regaining focus after external edits) and cleared by EditorApp once it has picked up
    /// and started handling the recompile.</summary>
    public bool ScriptCompileRequested { get; private set; }

    public void RequestScriptCompile() => ScriptCompileRequested = true;
    public void ClearScriptCompileRequest() => ScriptCompileRequested = false;

    /// <summary>True if EditScene has changes that haven't been saved to disk. Tracked automatically —
    /// every scene-mutating action in this editor flows through the Undo stack, so listening to its
    /// Changed event is enough; nothing needs to remember to flag this separately.</summary>
    public bool IsDirty { get; private set; }

    public EditorState(string projectPath, AssetDatabase assets)
    {
        ProjectPath = projectPath;
        Assets = assets;
        EditScene = new Scene("Untitled Scene");

        // Play Mode edits happen on the throwaway PlayScene clone, not EditScene, so they must not mark
        // the persisted scene as having unsaved changes.
        Undo.Changed += () =>
        {
            if (!IsPlaying) IsDirty = true;
        };
    }

    /// <summary>Call after successfully saving or loading a scene — it now exactly matches what's on disk.</summary>
    public void MarkClean() => IsDirty = false;

    public void SelectGameObject(GameObject? go)
    {
        Selected = go;
        SelectedAsset = null;
    }

    public void SelectAsset(AssetInfo? asset)
    {
        SelectedAsset = asset;
        Selected = null;
    }

    public void LogMessage(string message) => Log.Add($"[{DateTime.Now:HH:mm:ss}] {message}");

    // Scenes and prefabs live inside Assets/ (like every other asset), matching the Unity convention of
    // a single browsable project root — there's no separate top-level Scenes/Prefabs folder anymore.
    // These constants are the single source of truth for that location — anything that creates or looks
    // for a scene/prefab should go through ScenesFolder/PrefabsFolder (absolute) or the *RelativeFolder
    // constants (relative to Assets/, for AssetDatabase calls) rather than hardcoding "Scenes"/"Prefabs" again.
    public const string ScenesRelativeFolder = "Scenes";
    public const string PrefabsRelativeFolder = "Prefabs";

    public string ScenesFolder => Directory.CreateDirectory(Path.Combine(ProjectPath, "Assets", ScenesRelativeFolder)).FullName;
    public string PrefabsFolder => Directory.CreateDirectory(Path.Combine(ProjectPath, "Assets", PrefabsRelativeFolder)).FullName;

    /// <summary>Adds (or refreshes) a tab for this scene path so it shows in the tab strip.</summary>
    public void RegisterSceneTab(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var existing = SceneTabs.FirstOrDefault(t => t.Path == path);
        if (existing != null) existing.Name = name;
        else SceneTabs.Add(new SceneTab { Name = name, Path = path });
    }

    public void CloseSceneTab(SceneTab tab) => SceneTabs.Remove(tab);

    /// <summary>Enters Play Mode: clones EditScene (same Ids, so selection carries over) and re-links textures.</summary>
    public void StartPlay()
    {
        if (IsPlaying) return;

        // An Inspector audio/animation preview belongs to an EditScene component instance, entirely
        // separate from the fresh clone PlayScene is about to become — left running, it would just keep
        // playing alongside whatever Play Mode itself starts, audibly overlapping. Stopped the same
        // defensive way StopPlay below stops PlayScene's own AudioSources.
        PreviewingAnimation?.Stop();
        PreviewingAnimation = null;
        PreviewingAudio?.StopAllSounds();
        PreviewingAudio = null;

        PlayScene = SceneSerializer.Clone(EditScene);
        Assets.ResolveSceneAssets(PlayScene);

        if (Selected != null)
            Selected = PlayScene.FindById(Selected.Id);

        // undo history from edit mode doesn't apply to the throwaway play clone (different Scene instance)
        Undo.Clear();
        Time.Reset();

        LogMessage("▶ Entered Play Mode.");
    }

    /// <summary>Exits Play Mode and discards every change made while playing.</summary>
    public void StopPlay()
    {
        if (!IsPlaying) return;

        // Dropping the PlayScene reference below doesn't walk the hierarchy calling Destroy()/OnDetach()
        // the way removing an individual GameObject would, so anything holding a live native resource —
        // a playing AudioSource, specifically — needs an explicit chance to stop here, or the sound would
        // keep playing after Stop is pressed: MonoGame's audio engine has no idea the C# Scene object it
        // belongs to was just abandoned.
        foreach (var go in PlayScene!.GameObjects)
            foreach (var audio in go.GetComponents<AudioSource>())
                audio.StopAllSounds();

        PlayScene = null;
        if (Selected != null)
            Selected = EditScene.FindById(Selected.Id);

        Undo.Clear();

        LogMessage("■ Stopped Play Mode.");
    }

    public void TogglePlay()
    {
        if (IsPlaying) StopPlay();
        else StartPlay();
    }

    /// <summary>Undo, then clear Selected if it no longer belongs to the active scene (the undone action may have deleted it).</summary>
    public void PerformUndo()
    {
        Undo.Undo();
        PruneDeadSelection();
    }

    /// <summary>Redo, then clear Selected if it no longer belongs to the active scene (the redone action may have deleted it).</summary>
    public void PerformRedo()
    {
        Undo.Redo();
        PruneDeadSelection();
    }

    private void PruneDeadSelection()
    {
        if (Selected != null && Selected.Scene != ActiveScene)
            Selected = null;
    }
}
