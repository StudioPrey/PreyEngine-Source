using ImGuiNET;

namespace MyEngine.Editor.Panels;

/// <summary>
/// The "Files" panel (bottom-left): the whole project as a tree, like Phaser Editor's — every folder, with
/// the files inside it as leaves. It's the project-level counterpart to the Outline above it (which shows
/// what's IN the scene): click a folder to make it the current one for the Blocks panel's Assets tab, click
/// a file to select it in the Inspector, drag things onto folders to move them, double-click a scene /
/// prefab / script to open / instantiate / edit it. All the actual tree logic lives in ContentBrowserPanel
/// (DrawFilesTree) next to the tile code it shares its interactions with.
/// </summary>
public static class FilesPanel
{
    /// <summary>Returns the full path of a scene the user double-clicked, or null.</summary>
    public static string? Draw(EditorState state)
    {
        EditorLayout.PinFiles();
        ImGui.Begin("Files", EditorLayout.PanelFlags);

        string? sceneToOpen = ContentBrowserPanel.DrawFilesTree(state);

        ImGui.End();
        return sceneToOpen;
    }
}
