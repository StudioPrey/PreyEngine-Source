using ImGuiNET;

namespace MyEngine.Editor.Panels;

/// <summary>
/// The two layout helpers behind the Inspector's Phaser-Editor look, so InspectorPanel's ~40 property widgets
/// stay one-liners:
///   Section — a collapsible component header in neutral gray (instead of the amber selection color, which is
///             reserved for "selected" everywhere else — see EditorTheme), open by default.
///   Label   — puts a property's name in a fixed left-hand column with its editor filling the rest of the
///             row, instead of ImGui's default of the editor first and the name trailing after it. The
///             widget itself is then given a "##" (hidden) label so its ID stays unique but nothing is drawn
///             twice. Call it immediately before the widget it labels — the widget must remain the last ImGui
///             item submitted, because ImGuiUndo.Track (right after each widget) reads its activated/
///             deactivated state.
/// </summary>
internal static class InspectorUi
{
    /// <summary>Property-name column: this fraction of the row, but never narrower than the minimum.</summary>
    private const float LabelFraction = 0.40f;
    private const float MinLabelWidth = 96f;
    private const float LabelRightGap = 6f;

    /// <summary>A collapsible component header, open by default. Returns whether it's open — used exactly
    /// like the ImGui.CollapsingHeader call it wraps. The header remains the "last item" afterwards, so
    /// InspectorPanel's right-click component menu (BeginPopupContextItem) still attaches to it.</summary>
    public static bool Section(string title)
    {
        var colors = ImGui.GetStyle().Colors;
        ImGui.PushStyleColor(ImGuiCol.Header, colors[(int)ImGuiCol.FrameBg]);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, colors[(int)ImGuiCol.FrameBgHovered]);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, colors[(int)ImGuiCol.FrameBgActive]);
        bool open = ImGui.CollapsingHeader(title, ImGuiTreeNodeFlags.DefaultOpen);
        ImGui.PopStyleColor(3);
        return open;
    }

    /// <summary>Draws <paramref name="text"/> in the left column and positions the cursor at the start of the
    /// value column. <paramref name="fillWidth"/> also stretches the NEXT widget to the row's right edge —
    /// right for drag/slider/input/combo/color widgets, wrong for a Checkbox (a fixed-size box).</summary>
    public static void Label(string text, bool fillWidth = true)
    {
        float labelWidth = MathF.Max(MinLabelWidth, ImGui.GetContentRegionAvail().X * LabelFraction);
        float startX = ImGui.GetCursorPosX();

        ImGui.AlignTextToFramePadding();
        string shown = Fit(text, labelWidth - LabelRightGap);
        ImGui.TextUnformatted(shown);
        if (shown != text && ImGui.IsItemHovered())
            ImGui.SetTooltip(text);

        ImGui.SameLine(startX + labelWidth);
        if (fillWidth) ImGui.SetNextItemWidth(-1f);
    }

    /// <summary>Shortens <paramref name="text"/> with "..." until it fits <paramref name="maxWidth"/> pixels,
    /// so a long property name never spills into the editor next to it (the full name shows as a tooltip).</summary>
    private static string Fit(string text, float maxWidth)
    {
        if (ImGui.CalcTextSize(text).X <= maxWidth) return text;

        for (int length = text.Length - 1; length > 0; length--)
        {
            string candidate = text.Substring(0, length) + "...";
            if (ImGui.CalcTextSize(candidate).X <= maxWidth) return candidate;
        }
        return "...";
    }
}
