using ImGuiNET;

namespace MyEngine.Editor.Panels;

/// <summary>
/// Console and Content Browser share one panel, switched via tabs, rather than sitting side by side —
/// each gets the panel's full width while it's the active tab, instead of a fixed half each regardless of
/// which one is actually being used. ImGui's own tab bar persists which tab was last active across frames
/// on its own; nothing here needs to track that separately.
/// </summary>
public static class BottomPanel
{
    public static ContentBrowserResult Draw(EditorState state)
    {
        // Must run before anything else this frame draws a single Content Browser tile, regardless of
        // which tab happens to be active right now — a delete confirmed on a previous frame still needs to
        // execute even if the user has since switched to the Console tab. Same reasoning as
        // ContentBrowserPanel.DrawContents' own doc comment.
        ContentBrowserPanel.ProcessConfirmedDelete(state);

        EditorLayout.PinBottomPanel();
        ImGui.Begin("Console / Content Browser", EditorLayout.PanelFlags);

        var result = new ContentBrowserResult();

        if (ImGui.BeginTabBar("BottomPanelTabs"))
        {
            if (ImGui.BeginTabItem("Content Browser"))
            {
                result = ContentBrowserPanel.DrawContents(state);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Console"))
            {
                ConsolePanel.DrawContents(state);
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }

        ImGui.End();

        // Its own top-level window, not part of either tab's content — has to run after End(), not nested
        // inside a tab item, or it would be clipped to that tab's content region and only usable while the
        // Content Browser tab happens to be the active one.
        ContentBrowserPanel.DrawDeleteConfirmModal(state);

        return result;
    }
}
