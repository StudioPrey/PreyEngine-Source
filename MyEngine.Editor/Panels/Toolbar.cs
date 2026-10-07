using System.Numerics;
using ImGuiNET;

namespace MyEngine.Editor.Panels;

/// <summary>
/// The Editor's slim top strip below the main menu bar: Play/Stop and Undo/Redo on the left, the current
/// scene's name (with an unsaved-changes mark) on the right — and nothing else, like Phaser Editor's, whose
/// top bar is little more than a Play button. Everything that acts on the scene view itself (Move/Rotate/
/// Scale, World/Local, Grid, Colliders) lives in the Viewport's own floating tool overlay instead — see
/// ViewportToolsOverlay.
///
/// The W/E/R gizmo-mode shortcuts are still polled from here: this strip is drawn every frame regardless
/// of which panel has focus, which is exactly what a global shortcut needs.
/// </summary>
public static class Toolbar
{
    private static readonly Vector2 ButtonSize = new(26f, 26f);

    private static readonly Vector4 PlayModeColor = new(1f, 0.8f, 0.3f, 1f);

    public static void Draw(EditorState state)
    {
        EditorLayout.PinToolbar();
        ImGui.Begin("##Toolbar", EditorLayout.ToolbarFlags);

        DrawPlayStop(state);
        ImGui.SameLine();
        DrawUndoRedoButtons(state);

        DrawSceneStatus(state);

        if (!ImGui.GetIO().WantTextInput)
        {
            if (ImGui.IsKeyPressed(ImGuiKey.W)) state.Gizmo = GizmoMode.Move;
            if (ImGui.IsKeyPressed(ImGuiKey.E)) state.Gizmo = GizmoMode.Rotate;
            if (ImGui.IsKeyPressed(ImGuiKey.R)) state.Gizmo = GizmoMode.Scale;
        }

        ImGui.End();
    }

    private static void DrawPlayStop(EditorState state)
    {
        bool playing = state.IsPlaying;
        if (EditorIcons.Button("PlayStop", ButtonSize, playing, playing ? EditorIcons.Stop : EditorIcons.Play))
            state.TogglePlay();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(playing ? "Stop" : "Play");
    }

    private static void DrawUndoRedoButtons(EditorState state)
    {
        ImGui.BeginDisabled(!state.Undo.CanUndo);
        if (EditorIcons.Button("Undo", ButtonSize, false, EditorIcons.Undo)) state.PerformUndo();
        if (ImGui.IsItemHovered() && state.Undo.NextUndoDescription != null)
            ImGui.SetTooltip(state.Undo.NextUndoDescription);
        ImGui.EndDisabled();

        ImGui.SameLine();

        ImGui.BeginDisabled(!state.Undo.CanRedo);
        if (EditorIcons.Button("Redo", ButtonSize, false, EditorIcons.Redo)) state.PerformRedo();
        if (ImGui.IsItemHovered() && state.Undo.NextRedoDescription != null)
            ImGui.SetTooltip(state.Undo.NextRedoDescription);
        ImGui.EndDisabled();
    }

    /// <summary>Right-aligned "which scene am I in" label — "Play Mode" (amber) while playing, otherwise the
    /// scene's file name with a trailing " *" when it has unsaved changes.</summary>
    private static void DrawSceneStatus(EditorState state)
    {
        string sceneName = state.CurrentScenePath != null
            ? Path.GetFileNameWithoutExtension(state.CurrentScenePath)
            : state.EditScene.Name;
        string label = state.IsPlaying ? "Play Mode" : sceneName + (state.IsDirty ? " *" : "");

        float textWidth = ImGui.CalcTextSize(label).X;
        ImGui.SameLine();
        ImGui.SetCursorPosX(MathF.Max(ImGui.GetCursorPosX(), ImGui.GetWindowWidth() - textWidth - 14f));
        // Buttons are 26px tall; the text is shorter, so nudge it down to sit on the same visual center line.
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 5f);

        if (state.IsPlaying) ImGui.TextColored(PlayModeColor, label);
        else ImGui.TextDisabled(label);
    }
}
