using System.Numerics;
using ImGuiNET;

namespace MyEngine.Editor.Panels;

/// <summary>
/// The small floating tool palette in the Viewport's bottom-left corner — Move / Rotate / Scale, the
/// World/Local axis toggle, and the Grid / Colliders view toggles. Phaser Editor keeps its scene tools in
/// the same spot; here it replaces the long strip these buttons used to occupy in the top toolbar, and
/// frees that strip's height for the scene itself.
///
/// Drawn from inside the Viewport window, right after the scene Image (see ViewportPanel.Draw), using
/// SetCursorScreenPos so the buttons float over the picture. Everything each button DOES is unchanged from
/// when they lived in the toolbar (same EditorState fields, same tooltips) — only where they're drawn moved.
/// </summary>
public static class ViewportToolsOverlay
{
    private const float ButtonWidth = 26f;
    private static readonly Vector2 ButtonSize = new(ButtonWidth, ButtonWidth);
    private const float SpaceToggleWidth = 56f;
    private const float Gap = 4f;           // between neighboring items
    private const float GroupGap = 6f;      // EXTRA space where one group (tools / axis / view toggles) ends
    private const float Pad = 4f;           // inside the background plate
    private const float Margin = 10f;       // from the Viewport image's edges

    // Fixed, computed-not-measured width: the plate has to be painted BEFORE the buttons drawn on top of it,
    // so it can't wait to measure them afterward. 6 items, 5 gaps between them, 2 extra group gaps.
    private const float ContentWidth = ButtonWidth * 5f + SpaceToggleWidth + Gap * 5f + GroupGap * 2f;
    private const float PlateWidth = ContentWidth + Pad * 2f;
    private const float PlateHeight = ButtonWidth + Pad * 2f;

    /// <summary>Draws the palette over the bottom-left of the Viewport image and returns true if the mouse is
    /// currently over it — the caller must then NOT treat that mouse position as being "over the scene"
    /// (clicking a tool button must not also select/deselect a GameObject or grab a gizmo handle behind it).</summary>
    public static bool Draw(EditorState state, Vector2 imageMin, Vector2 imageMax)
    {
        // Not enough room to show it without covering the whole scene (a tiny Viewport) — skip this frame.
        if (imageMax.X - imageMin.X < PlateWidth + Margin * 2f || imageMax.Y - imageMin.Y < PlateHeight + Margin * 2f)
            return false;

        var plateMin = new Vector2(imageMin.X + Margin, imageMax.Y - Margin - PlateHeight);
        var plateMax = plateMin + new Vector2(PlateWidth, PlateHeight);

        var style = ImGui.GetStyle();
        var drawList = ImGui.GetWindowDrawList();
        uint border = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.55f));
        uint fill = ImGui.ColorConvertFloat4ToU32(style.Colors[(int)ImGuiCol.WindowBg] with { W = 0.92f });
        drawList.AddRectFilled(plateMin - new Vector2(1f, 1f), plateMax + new Vector2(1f, 1f), border);
        drawList.AddRectFilled(plateMin, plateMax, fill);

        ImGui.SetCursorScreenPos(plateMin + new Vector2(Pad, Pad));

        DrawGizmoModeButton(state, GizmoMode.Move, EditorIcons.Move, "Move (W)");
        ImGui.SameLine(0f, Gap);
        DrawGizmoModeButton(state, GizmoMode.Rotate, EditorIcons.Rotate, "Rotate (E)");
        ImGui.SameLine(0f, Gap);
        DrawGizmoModeButton(state, GizmoMode.Scale, EditorIcons.Scale, "Scale (R)");

        ImGui.SameLine(0f, Gap + GroupGap);
        DrawGizmoSpaceToggle(state);

        ImGui.SameLine(0f, Gap + GroupGap);
        DrawViewToggle(EditorIcons.Grid, "Grid", () => state.ShowGrid, v => state.ShowGrid = v);
        ImGui.SameLine(0f, Gap);
        DrawViewToggle(EditorIcons.Colliders, "Colliders", () => state.ShowColliders, v => state.ShowColliders = v);

        var mouse = ImGui.GetMousePos();
        return mouse.X >= plateMin.X && mouse.X <= plateMax.X && mouse.Y >= plateMin.Y && mouse.Y <= plateMax.Y;
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
        if (ImGui.Button(label + "##GizmoSpace", new Vector2(SpaceToggleWidth, ButtonSize.Y)))
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
