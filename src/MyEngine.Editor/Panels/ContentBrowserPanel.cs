using System.Numerics;
using ImGuiNET;

namespace MyEngine.Editor.Panels;

public readonly struct ContentBrowserResult
{
    /// <summary>Full path of a scene the user double-clicked, or null.</summary>
    public string? SceneToOpen { get; init; }
}

public static class ContentBrowserPanel
{
    private const float ThumbnailSize = 64f;
    private const float CellPadding = 12f;

    public static ContentBrowserResult Draw(EditorState state)
    {
        ImGui.Begin("Content Browser");

        DrawToolbar(state);
        ImGui.Separator();

        string? sceneToOpen = null;

        var subfolders = state.Assets.GetSubfolders(state.ContentBrowserFolder).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();
        var assets = state.Assets.GetAssetsInFolder(state.ContentBrowserFolder)
            .OrderBy(a => a.Type).ThenBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();

        float cellSize = ThumbnailSize + CellPadding;
        float panelWidth = ImGui.GetContentRegionAvail().X;
        int columns = Math.Max(1, (int)(panelWidth / cellSize));
        int i = 0;

        void Advance()
        {
            i++;
            if (i % columns != 0) ImGui.SameLine();
        }

        if (state.ContentBrowserFolder.Length > 0)
        {
            DrawUpTile(state);
            Advance();
        }

        foreach (var folder in subfolders)
        {
            ImGui.PushID("folder_" + folder);
            DrawFolderTile(state, folder);
            ImGui.PopID();
            Advance();
        }

        foreach (var asset in assets)
        {
            ImGui.PushID(asset.RelativePath);
            var opened = DrawAssetTile(asset, state);
            if (opened != null) sceneToOpen = opened;
            ImGui.PopID();
            Advance();
        }

        if (state.PendingCreate != null)
        {
            DrawPendingCreateTile(state);
        }

        if (subfolders.Length == 0 && assets.Length == 0 && state.PendingCreate == null)
            ImGui.TextDisabled("Empty. Right-click for Create, or drop a .png here.");

        HandleContextMenu(state);

        ImGui.End();
        return new ContentBrowserResult { SceneToOpen = sceneToOpen };
    }

    private static void DrawToolbar(EditorState state)
    {
        if (ImGui.Button("Refresh"))
        {
            state.Assets.Refresh();
            state.LogMessage("Reimported all assets.");
        }
        ImGui.SameLine();

        if (ImGui.SmallButton("Assets")) state.ContentBrowserFolder = "";

        if (state.ContentBrowserFolder.Length > 0)
        {
            var parts = state.ContentBrowserFolder.Split('/');
            string accum = "";
            foreach (var part in parts)
            {
                accum = accum.Length == 0 ? part : accum + "/" + part;
                var target = accum;
                ImGui.SameLine(0, 2);
                ImGui.TextDisabled("/");
                ImGui.SameLine(0, 2);
                if (ImGui.SmallButton(part)) state.ContentBrowserFolder = target;
            }
        }
    }

    private static void HandleContextMenu(EditorState state)
    {
        if (ImGui.IsWindowHovered() && !ImGui.IsAnyItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            ImGui.OpenPopup("ContentBrowserContextMenu");

        if (!ImGui.BeginPopup("ContentBrowserContextMenu")) return;

        if (ImGui.BeginMenu("Create"))
        {
            if (ImGui.MenuItem("Folder")) BeginCreate(state, PendingCreateKind.Folder);
            if (ImGui.MenuItem("Scene")) BeginCreate(state, PendingCreateKind.Scene);
            if (ImGui.MenuItem("Script")) BeginCreate(state, PendingCreateKind.Script);
            ImGui.EndMenu();
        }
        if (ImGui.MenuItem("Refresh")) state.Assets.Refresh();
        ImGui.EndPopup();
    }

    private static void BeginCreate(EditorState state, PendingCreateKind kind)
    {
        state.PendingCreate = new PendingCreateItem
        {
            Kind = kind,
            NameBuffer = kind switch
            {
                PendingCreateKind.Folder => "New Folder",
                PendingCreateKind.Scene => "New Scene",
                PendingCreateKind.Script => "NewScript",
                _ => "New",
            },
            FocusRequested = true,
        };
    }

    // ---------------------------------------------------------------- tiles

    private static void DrawUpTile(EditorState state)
    {
        ImGui.BeginGroup();
        if (ImGui.Button("..", new Vector2(ThumbnailSize, ThumbnailSize)))
        {
            var idx = state.ContentBrowserFolder.LastIndexOf('/');
            state.ContentBrowserFolder = idx < 0 ? "" : state.ContentBrowserFolder[..idx];
        }
        ImGui.TextDisabled("..");
        ImGui.EndGroup();
    }

    private static void DrawFolderTile(EditorState state, string folderName)
    {
        ImGui.BeginGroup();
        ImGui.Button("[Folder]", new Vector2(ThumbnailSize, ThumbnailSize));
        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            state.ContentBrowserFolder = state.ContentBrowserFolder.Length == 0
                ? folderName
                : state.ContentBrowserFolder + "/" + folderName;
        }
        ImGui.TextWrapped(Truncate(folderName, 10));
        ImGui.EndGroup();
    }

    /// <summary>Returns the full path of the scene to open, if the user just double-clicked one.</summary>
    private static string? DrawAssetTile(AssetInfo asset, EditorState state)
    {
        string? openScene = null;
        ImGui.BeginGroup();

        bool selected = state.SelectedAsset == asset;
        if (selected) ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.26f, 0.45f, 0.75f, 0.7f));

        if (asset.Type == AssetType.Texture && asset.Texture != null)
        {
            if (ImGui.ImageButton("##" + asset.RelativePath, asset.ThumbnailId, new Vector2(ThumbnailSize, ThumbnailSize)))
                state.SelectAsset(asset);
        }
        else
        {
            string label = asset.Type switch
            {
                AssetType.Prefab => "[Prefab]",
                AssetType.Scene => "[Scene]",
                AssetType.Script => "[Script]",
                _ => "[?]",
            };
            if (ImGui.Button(label, new Vector2(ThumbnailSize, ThumbnailSize)))
                state.SelectAsset(asset);
        }

        if (selected) ImGui.PopStyleColor();

        if (asset.Type == AssetType.Texture)
        {
            if (DragDropPayloads.BeginSource(DragDropPayloads.Texture, asset.RelativePath, asset.DisplayName))
            {
                ImGui.Image(asset.ThumbnailId, new Vector2(48, 48));
                ImGui.EndDragDropSource();
            }
        }
        else if (asset.Type == AssetType.Prefab)
        {
            if (DragDropPayloads.BeginSource(DragDropPayloads.Prefab, asset.RelativePath, asset.DisplayName))
            {
                ImGui.Text(asset.DisplayName);
                ImGui.EndDragDropSource();
            }
            if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                AssetDropHandler.InstantiatePrefab(state, asset.RelativePath);
        }
        else if (asset.Type == AssetType.Scene)
        {
            if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                openScene = asset.FullPath;
        }
        else if (asset.Type == AssetType.Script)
        {
            if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                var error = ExternalEditorLauncher.Open(asset.FullPath);
                if (error != null) state.LogMessage($"ERROR: {error}");
            }
        }

        ImGui.TextWrapped(Truncate(asset.DisplayName, 10));
        ImGui.EndGroup();
        return openScene;
    }

    private static void DrawPendingCreateTile(EditorState state)
    {
        var pending = state.PendingCreate!;
        ImGui.BeginGroup();

        string label = pending.Kind switch
        {
            PendingCreateKind.Folder => "[Folder]",
            PendingCreateKind.Scene => "[Scene]",
            PendingCreateKind.Script => "[Script]",
            _ => "[?]",
        };
        ImGui.Button(label, new Vector2(ThumbnailSize, ThumbnailSize));

        ImGui.SetNextItemWidth(ThumbnailSize);
        if (pending.FocusRequested)
        {
            ImGui.SetKeyboardFocusHere();
            pending.FocusRequested = false;
        }

        bool confirmed = ImGui.InputText("##pendingName", ref pending.NameBuffer, 64,
            ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
        bool lostFocus = ImGui.IsItemDeactivated() && !confirmed;
        bool cancelled = ImGui.IsKeyPressed(ImGuiKey.Escape);

        ImGui.EndGroup();

        if (cancelled) { state.PendingCreate = null; return; }
        if (confirmed || lostFocus) CommitPendingCreate(state, pending);
    }

    private static void CommitPendingCreate(EditorState state, PendingCreateItem pending)
    {
        var name = pending.NameBuffer.Trim();
        state.PendingCreate = null;
        if (name.Length == 0) return;

        try
        {
            switch (pending.Kind)
            {
                case PendingCreateKind.Folder:
                    state.Assets.CreateFolder(state.ContentBrowserFolder, name);
                    state.LogMessage($"Created folder '{name}'.");
                    break;

                case PendingCreateKind.Scene:
                    // Scenes always live in Assets/Scenes — never wherever the browser happens to be pointed —
                    // so "File > Open Scene" (which only looks there) can always find every scene that exists,
                    // and there's only ever one place a new scene can end up.
                    state.Assets.CreateScene(EditorState.ScenesRelativeFolder, name);
                    state.ContentBrowserFolder = EditorState.ScenesRelativeFolder;
                    state.LogMessage($"Created scene '{name}' in Assets/{EditorState.ScenesRelativeFolder}.");
                    break;

                case PendingCreateKind.Script:
                    var relativePath = state.Assets.CreateScript(state.ContentBrowserFolder, name);
                    state.LogMessage($"Created script '{relativePath}'. Compiling…");
                    state.RequestScriptCompile();
                    break;
            }
        }
        catch (Exception ex)
        {
            state.LogMessage($"ERROR creating '{name}': {ex.Message}");
        }
    }

    private static string Truncate(string s, int maxChars) =>
        s.Length <= maxChars ? s : s[..maxChars] + "…";
}
