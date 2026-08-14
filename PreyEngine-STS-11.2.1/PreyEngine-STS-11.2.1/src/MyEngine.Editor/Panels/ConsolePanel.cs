using ImGuiNET;

namespace MyEngine.Editor.Panels;

public static class ConsolePanel
{
    public static void Draw(EditorState state)
    {
        ImGui.Begin("Console");

        if (ImGui.Button("Clear")) state.Log.Clear();
        ImGui.Separator();

        ImGui.BeginChild("ConsoleScroll");
        foreach (var line in state.Log)
            ImGui.TextUnformatted(line);
        if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
            ImGui.SetScrollHereY(1.0f);
        ImGui.EndChild();

        ImGui.End();
    }
}
