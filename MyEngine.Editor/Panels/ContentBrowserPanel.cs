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
    private const float TreeWidth = 130f;

    /// <summary>Toolbar + folder tree + grid only — no window Begin/End of its own — hosted inside
    /// BottomPanel's shared tabbed window alongside Console (see Panels/BottomPanel.cs), which is
    /// responsible for calling ProcessConfirmedDelete (before anything else that frame) and
    /// DrawDeleteConfirmModal (after End()) around this — those are their own top-level windows, not part
    /// of this panel's content, so they don't belong inside DrawContents itself.</summary>
    public static ContentBrowserResult DrawContents(EditorState state)
    {
        DrawToolbar(state);
        ImGui.Separator();

        ImGui.BeginChild("FolderTree", new Vector2(TreeWidth, 0f), ImGuiChildFlags.None, ImGuiWindowFlags.None);
        DrawFolderTree(state);
        ImGui.EndChild();

        ImGui.SameLine();

        ImGui.BeginChild("FolderGrid", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.None);
        string? sceneToOpen = DrawGrid(state);
        ImGui.EndChild();

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

    // ---------------------------------------------------------------- folder tree (left sidebar)

    /// <summary>A persistent, always-expandable tree of every folder in the project — lets the person jump
    /// straight to any folder instead of clicking through the grid level by level, and (like every node's
    /// tile in the grid) accepts drops to move an asset or folder there.</summary>
    private static void DrawFolderTree(EditorState state)
    {
        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.DefaultOpen;
        if (state.ContentBrowserFolder.Length == 0) flags |= ImGuiTreeNodeFlags.Selected;

        bool open = ImGui.TreeNodeEx("Assets##contentTreeRoot", flags);
        if (ImGui.IsItemClicked()) state.ContentBrowserFolder = "";
        AcceptFolderDrop(state, "");

        if (open)
        {
            foreach (var sub in state.Assets.GetSubfolders("").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                DrawTreeNode(state, sub, sub);
            ImGui.TreePop();
        }
    }

    private static void DrawTreeNode(EditorState state, string folderPath, string displayName)
    {
        var subfolders = state.Assets.GetSubfolders(folderPath).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();

        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
        if (subfolders.Length == 0) flags |= ImGuiTreeNodeFlags.Leaf;
        if (state.ContentBrowserFolder == folderPath) flags |= ImGuiTreeNodeFlags.Selected;

        bool open = ImGui.TreeNodeEx(displayName + "##tree_" + folderPath, flags);
        if (ImGui.IsItemClicked()) state.ContentBrowserFolder = folderPath;
        AcceptFolderDrop(state, folderPath);

        if (open)
        {
            foreach (var sub in subfolders)
                DrawTreeNode(state, folderPath + "/" + sub, sub);
            ImGui.TreePop();
        }
    }

    /// <summary>Wrap around anything that represents a folder (a tree node, a folder tile, the ".." tile) —
    /// accepts a dropped asset or folder and moves it to <paramref name="targetFolder"/>.</summary>
    private static void AcceptFolderDrop(EditorState state, string targetFolder)
    {
        if (!ImGui.BeginDragDropTarget()) return;
        var dropped = DragDropPayloads.AcceptTarget(DragDropPayloads.ContentBrowserItem);
        if (dropped != null) TryMoveItem(state, dropped, targetFolder);
        ImGui.EndDragDropTarget();
    }

    private static void TryMoveItem(EditorState state, string itemRelativePath, string targetFolder)
    {
        bool moved = state.Assets.Find(itemRelativePath) != null
            ? state.Assets.MoveAsset(itemRelativePath, targetFolder)
            : state.Assets.MoveFolder(itemRelativePath, targetFolder);

        if (moved)
            state.LogMessage($"Moved '{itemRelativePath}' to '{(targetFolder.Length == 0 ? "Assets" : targetFolder)}'.");
    }

    // ---------------------------------------------------------------- grid (right side)

    private static string? DrawGrid(EditorState state)
    {
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
        return sceneToOpen;
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

    // ---------------------------------------------------------------- delete

    /// <summary>Executes a delete that was confirmed on a *previous* frame. Never called from inside the
    /// confirmation button's own handler — that runs in the same frame the grid already drew this item's
    /// tile (queuing a draw command against its current texture id), so disposing it right there would
    /// crash the renderer for the exact reason Refresh() used to (see AssetDatabase.DeleteAsset's doc
    /// comment). Waiting for the top of the next frame guarantees nothing this frame has referenced it yet.</summary>
    internal static void ProcessConfirmedDelete(EditorState state)
    {
        var pending = state.PendingDelete;
        if (pending == null || !pending.Confirmed) return;
        state.PendingDelete = null;

        bool ok = pending.IsFolder
            ? state.Assets.DeleteFolder(pending.RelativePath)
            : state.Assets.DeleteAsset(pending.RelativePath);

        if (!ok)
        {
            state.LogMessage($"ERROR: couldn't delete '{pending.DisplayName}' — it may be open in another program.");
            return;
        }

        state.LogMessage($"Deleted {(pending.IsFolder ? "folder" : "asset")} '{pending.DisplayName}'.");

        if (!pending.IsFolder && state.SelectedAsset?.RelativePath == pending.RelativePath)
            state.SelectedAsset = null;

        // Don't leave the browser pointed at a folder that no longer exists.
        if (pending.IsFolder && (state.ContentBrowserFolder == pending.RelativePath
            || state.ContentBrowserFolder.StartsWith(pending.RelativePath + "/", StringComparison.Ordinal)))
        {
            var idx = pending.RelativePath.LastIndexOf('/');
            state.ContentBrowserFolder = idx < 0 ? "" : pending.RelativePath[..idx];
        }
    }

    /// <summary>Same flag-driven plain-window approach as EditorApp.DrawUnsavedChangesModal, and for the
    /// same reason: no dependency on the ImGui popup ID-stack context active wherever "Delete" happened to
    /// be clicked from (a tile deep in a scrolled grid) — this just checks state.PendingDelete every frame.</summary>
    internal static void DrawDeleteConfirmModal(EditorState state)
    {
        var pending = state.PendingDelete;
        if (pending == null || pending.Confirmed) return;

        var viewport = ImGui.GetMainViewport();
        var center = new Vector2(viewport.Pos.X + viewport.Size.X * 0.5f, viewport.Pos.Y + viewport.Size.Y * 0.5f);
        ImGui.SetNextWindowPos(center, ImGuiCond.Always, new Vector2(0.5f, 0.5f));

        bool open = true;
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking;

        ImGui.Begin(pending.IsFolder ? "Delete Folder" : "Delete Asset", ref open, flags);

        ImGui.Text(pending.IsFolder
            ? $"Delete folder '{pending.DisplayName}' and everything inside it?"
            : $"Delete '{pending.DisplayName}'?");
        ImGui.TextDisabled("This can't be undone.");
        ImGui.Spacing();

        // Only sets the flag — the actual delete runs at the top of next frame, see ProcessConfirmedDelete.
        if (ImGui.Button("Delete", new Vector2(120, 0)))
            pending.Confirmed = true;

        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(100, 0)) || !open)
            state.PendingDelete = null;

        ImGui.End();
    }

    // ---------------------------------------------------------------- tiles

    private static void DrawUpTile(EditorState state)
    {
        var idx = state.ContentBrowserFolder.LastIndexOf('/');
        var parentFolder = idx < 0 ? "" : state.ContentBrowserFolder[..idx];

        ImGui.BeginGroup();
        if (EditorIcons.Button("up", new Vector2(ThumbnailSize, ThumbnailSize), false, EditorIcons.FolderUp))
            state.ContentBrowserFolder = parentFolder;
        AcceptFolderDrop(state, parentFolder);
        ImGui.TextDisabled("..");
        ImGui.EndGroup();
    }

    private static void DrawFolderTile(EditorState state, string folderName)
    {
        string folderPath = state.ContentBrowserFolder.Length == 0 ? folderName : state.ContentBrowserFolder + "/" + folderName;

        ImGui.BeginGroup();
        EditorIcons.Button("folder", new Vector2(ThumbnailSize, ThumbnailSize), false, EditorIcons.Folder);
        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            state.ContentBrowserFolder = folderPath;

        if (ImGui.BeginPopupContextItem())
        {
            if (ImGui.MenuItem("Delete"))
                state.PendingDelete = new PendingDeleteItem { RelativePath = folderPath, DisplayName = folderName, IsFolder = true };
            ImGui.EndPopup();
        }

        if (DragDropPayloads.BeginSource(DragDropPayloads.ContentBrowserItem, folderPath, folderName))
        {
            ImGui.Text(folderName);
            ImGui.EndDragDropSource();
        }
        AcceptFolderDrop(state, folderPath);

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
            Action<ImDrawListPtr, Vector2, Vector2, uint> icon = asset.Type switch
            {
                AssetType.Prefab => EditorIcons.Prefab,
                AssetType.Scene => EditorIcons.Scene,
                AssetType.Script => EditorIcons.Script,
                AssetType.Audio => EditorIcons.Audio,
                _ => EditorIcons.Script,
            };
            if (EditorIcons.Button(asset.RelativePath, new Vector2(ThumbnailSize, ThumbnailSize), false, icon))
                state.SelectAsset(asset);
        }

        if (selected) ImGui.PopStyleColor();

        if (ImGui.BeginPopupContextItem())
        {
            if (ImGui.MenuItem("Delete"))
                state.PendingDelete = new PendingDeleteItem { RelativePath = asset.RelativePath, DisplayName = asset.DisplayName, IsFolder = false };
            ImGui.EndPopup();
        }

        // Every asset type is draggable to move it between folders (ContentBrowserItem); Texture/Prefab
        // additionally carry their original payload type so dragging one into the Viewport still works
        // exactly as before — one drag gesture, two possible drop outcomes depending on where it lands.
        if (DragDropPayloads.BeginSource(DragDropPayloads.ContentBrowserItem, asset.RelativePath, asset.DisplayName))
        {
            if (asset.Type == AssetType.Texture) DragDropPayloads.AddPayloadType(DragDropPayloads.Texture);
            else if (asset.Type == AssetType.Prefab) DragDropPayloads.AddPayloadType(DragDropPayloads.Prefab);
            else if (asset.Type == AssetType.Audio) DragDropPayloads.AddPayloadType(DragDropPayloads.Audio);

            if (asset.Type == AssetType.Texture && asset.ThumbnailId != IntPtr.Zero)
                ImGui.Image(asset.ThumbnailId, new Vector2(48, 48));
            else
                ImGui.Text(asset.DisplayName);
            ImGui.EndDragDropSource();
        }

        if (asset.Type == AssetType.Prefab && ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            AssetDropHandler.InstantiatePrefab(state, asset.RelativePath);
        }
        else if (asset.Type == AssetType.Scene && ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            openScene = asset.FullPath;
        }
        else if (asset.Type == AssetType.Script && ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            var error = ExternalEditorLauncher.Open(asset.FullPath);
            if (error != null) state.LogMessage($"ERROR: {error}");
        }

        ImGui.TextWrapped(Truncate(asset.DisplayName, 10));
        ImGui.EndGroup();
        return openScene;
    }

    private static void DrawPendingCreateTile(EditorState state)
    {
        var pending = state.PendingCreate!;
        ImGui.BeginGroup();

        Action<ImDrawListPtr, Vector2, Vector2, uint> icon = pending.Kind switch
        {
            PendingCreateKind.Folder => EditorIcons.Folder,
            PendingCreateKind.Scene => EditorIcons.Scene,
            PendingCreateKind.Script => EditorIcons.Script,
            _ => EditorIcons.Script,
        };
        EditorIcons.Button("pendingCreate", new Vector2(ThumbnailSize, ThumbnailSize), false, icon);

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
