using System.Numerics;
using ImGuiNET;

namespace MyEngine.Editor.Panels;

/// <summary>
/// The Editor's toolbar: Play/Stop, Undo/Redo, gizmo mode, gizmo space, and the Grid/Colliders view
/// toggles — drawn as their own fixed strip below the main menu bar, like a professional engine's editor,
/// rather than living inside the Viewport panel's own window. This is a pure relocation + re-skin: every
/// piece of behavior here (what each button does, the W/E/R shortcuts) is unchanged from what used to live
/// in ViewportPanel's DrawToolbar.
/// </summary>
public static class Toolbar
{
    private static readonly Vector2 ButtonSize = new(30f, 30f);

    public static void Draw(EditorState state)
    {
        EditorLayout.PinToolbar();
        ImGui.Begin("##Toolbar", EditorLayout.ToolbarFlags);

        DrawPlayStop(state);
        ImGui.SameLine();
        DrawUndoRedoButtons(state);

        Separator();
        DrawGizmoModeButton(state, GizmoMode.Move, EditorIcons.Move, "Move (W)");
        ImGui.SameLine();
        DrawGizmoModeButton(state, GizmoMode.Rotate, EditorIcons.Rotate, "Rotate (E)");
        ImGui.SameLine();
        DrawGizmoModeButton(state, GizmoMode.Scale, EditorIcons.Scale, "Scale (R)");

        Separator();
        DrawGizmoSpaceToggle(state);

        Separator();
        DrawViewToggle(EditorIcons.Grid, "Grid", () => state.ShowGrid, v => state.ShowGrid = v);
        ImGui.SameLine();
        DrawViewToggle(EditorIcons.Colliders, "Colliders", () => state.ShowColliders, v => state.ShowColliders = v);

        if (!ImGui.GetIO().WantTextInput)
        {
            if (ImGui.IsKeyPressed(ImGuiKey.W)) state.Gizmo = GizmoMode.Move;
            if (ImGui.IsKeyPressed(ImGuiKey.E)) state.Gizmo = GizmoMode.Rotate;
            if (ImGui.IsKeyPressed(ImGuiKey.R)) state.Gizmo = GizmoMode.Scale;
        }

        ImGui.End();
    }

    private static void Separator()
    {
        ImGui.SameLine();
        ImGui.TextDisabled("|");
        ImGui.SameLine();
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

    private static void DrawGizmoModeButton(EditorState state, GizmoMode mode, Action<ImDrawListPtr, Vector2, Vector2, uint> icon, string tooltip)
    {
        bool active = state.Gizmo == mode;
        if (EditorIcons.Button("Gizmo" + mode, ButtonSize, active, icon)) state.Gizmo = mode;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(tooltip);
    }

    private static void DrawGizmoSpaceToggle(EditorState state)
    {
        string label = state.GizmoSpace == GizmoSpace.World ? "World" : "Local";
        if (ImGui.Button(label, new Vector2(60f, ButtonSize.Y)))
            state.GizmoSpace = state.GizmoSpace == GizmoSpace.World ? GizmoSpace.Local : GizmoSpace.World;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Click to toggle Move-gizmo axis space (Scale is always local).");
    }

    private static void DrawViewToggle(Action<ImDrawListPtr, Vector2, Vector2, uint> icon, string tooltip, Func<bool> getter, Action<bool> setter)
    {
        bool value = getter();
        if (EditorIcons.Button("View" + tooltip, ButtonSize, value, icon)) setter(!value);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(tooltip);
    }
}
