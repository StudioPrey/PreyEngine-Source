using ImGuiNET;

namespace MyEngine.Editor.Panels;

public static class ConsolePanel
{
    /// <summary>Clear button + scrolling log, with no window Begin/End of its own — hosted inside
    /// BottomPanel's shared tabbed window alongside the Content Browser (see Panels/BottomPanel.cs).</summary>
    public static void DrawContents(EditorState state)
    {
        if (ImGui.Button("Clear")) state.Log.Clear();
        ImGui.Separator();

        ImGui.BeginChild("ConsoleScroll");
        foreach (var line in state.Log)
            ImGui.TextWrapped(line);
        if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
            ImGui.SetScrollHereY(1.0f);
        ImGui.EndChild();
    }
}
