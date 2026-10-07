using System.Numerics;
using ImGuiNET;

namespace MyEngine.Editor.Panels;

public readonly struct ContentBrowserResult
{
    /// <summary>Full path of a scene the user double-clicked, or null.</summary>
    public string? SceneToOpen { get; init; }
}

/// <summary>
/// The project-browsing pieces, now spread across two Phaser-style panels instead of one Unity-style
/// Content Browser:
///   - Files panel (bottom-left)  → DrawFilesTree: the whole project as a tree — every folder, with the
///     files inside it as leaves. Clicking a folder makes it the "current" folder.
///   - Blocks panel → Assets tab  → DrawAssetsTab: breadcrumb + thumbnail grid of the CURRENT folder.
///   - Blocks panel → Prefabs tab → DrawPrefabsTab: every prefab in the project, wherever it lives.
/// All of them share the same state (EditorState.ContentBrowserFolder / SelectedAsset / PendingCreate /
/// PendingDelete) and the same interaction code (HandleAssetItemInteractions), so a file behaves identically
/// — select, delete, drag, double-click — whichever panel it's shown in. None of them open a window of their
/// own; FilesPanel/BlocksPanel host them, and BlocksPanel is responsible for calling DrawDeleteConfirmModal
/// (after End()) — its own top-level window, not part of any tab's content. ProcessConfirmedDelete runs
/// earlier still, before every panel — see EditorApp.BuildUI and its own doc comment for why that has to
/// happen even earlier than "before this tab's own tiles."
/// </summary>
public static class ContentBrowserPanel
{
    private const float ThumbnailSize = 64f;
    private const float CellPadding = 12f;

    /// <summary>Breadcrumb toolbar + thumbnail grid for the current folder (the Blocks panel's "Assets" tab).</summary>
    public static ContentBrowserResult DrawAssetsTab(EditorState state)
    {
        DrawToolbar(state);
        ImGui.Separator();

        ImGui.BeginChild("FolderGrid", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.None);
        string? sceneToOpen = DrawGrid(state);
        ImGui.EndChild();

        return new ContentBrowserResult { SceneToOpen = sceneToOpen };
    }

    /// <summary>Every prefab in the project as a tile (the Blocks panel's "Prefabs" tab) — a shortcut to the
    /// prefabs regardless of which folder they were saved into. Drag a tile into the scene, or double-click it,
    /// to instantiate; same tile behavior as in the Assets tab.</summary>
    public static void DrawPrefabsTab(EditorState state)
    {
        var prefabs = state.Assets.Assets
            .Where(a => a.Type == AssetType.Prefab)
            .OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (prefabs.Length == 0)
        {
            ImGui.TextDisabled("No prefabs yet. Right-click a GameObject in the Outline and choose \"Save As Prefab...\".");
            return;
        }

        ImGui.BeginChild("PrefabGrid", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.None);

        float cellSize = ThumbnailSize + CellPadding;
        int columns = Math.Max(1, (int)(ImGui.GetContentRegionAvail().X / cellSize));
        int i = 0;
        foreach (var prefab in prefabs)
        {
            ImGui.PushID(prefab.RelativePath);
            DrawAssetTile(prefab, state);
            ImGui.PopID();

            i++;
            if (i % columns != 0) ImGui.SameLine();
        }

        ImGui.EndChild();
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

    // ---------------------------------------------------------------- files tree (Files panel)

    /// <summary>The whole project as one always-expandable tree — every folder, with the files inside it as
    /// leaves — so any folder or file is one click away instead of level-by-level through the grid. Clicking a
    /// folder makes it the current folder the Assets tab shows; clicking a file selects it (Inspector). Like
    /// every tile in the grid, folders accept drops to move an asset or folder there, and both folders and
    /// files can be dragged. Returns the full path of a scene the user double-clicked, or null.</summary>
    public static string? DrawFilesTree(EditorState state)
    {
        string? sceneToOpen = null;

        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.DefaultOpen;
        if (state.ContentBrowserFolder.Length == 0) flags |= ImGuiTreeNodeFlags.Selected;

        bool open = ImGui.TreeNodeEx("Assets##filesTreeRoot", flags);
        if (ImGui.IsItemClicked()) state.ContentBrowserFolder = "";
        AcceptFolderDrop(state, "");

        if (open)
        {
            DrawFolderContents(state, "", ref sceneToOpen);
            ImGui.TreePop();
        }

        return sceneToOpen;
    }

    /// <summary>One folder's children: its subfolders first (each recursing), then its files.</summary>
    private static void DrawFolderContents(EditorState state, string folderPath, ref string? sceneToOpen)
    {
        foreach (var sub in state.Assets.GetSubfolders(folderPath).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray())
            DrawFolderNode(state, folderPath.Length == 0 ? sub : folderPath + "/" + sub, sub, ref sceneToOpen);

        foreach (var asset in state.Assets.GetAssetsInFolder(folderPath)
                     .OrderBy(a => a.Type).ThenBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray())
        {
            var opened = DrawAssetRow(state, asset);
            if (opened != null) sceneToOpen = opened;
        }
    }

    private static void DrawFolderNode(EditorState state, string folderPath, string displayName, ref string? sceneToOpen)
    {
        bool isEmpty = !state.Assets.GetSubfolders(folderPath).Any() && !state.Assets.GetAssetsInFolder(folderPath).Any();

        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
        if (isEmpty) flags |= ImGuiTreeNodeFlags.Leaf;
        if (state.ContentBrowserFolder == folderPath) flags |= ImGuiTreeNodeFlags.Selected;

        // Two leading spaces reserve room for the icon EditorIcons.DrawTreeRowIcon paints right after this —
        // see that method's doc comment.
        bool open = ImGui.TreeNodeEx("  " + displayName + "##tree_" + folderPath, flags);
        EditorIcons.DrawTreeRowIcon(EditorIcons.Folder, ImGui.ColorConvertFloat4ToU32(EditorTheme.FolderAccent));
        if (ImGui.IsItemClicked()) state.ContentBrowserFolder = folderPath;
        AcceptFolderDrop(state, folderPath);

        if (ImGui.BeginPopupContextItem())
        {
            if (ImGui.MenuItem("Delete"))
                state.PendingDelete = new PendingDeleteItem { RelativePath = folderPath, DisplayName = displayName, IsFolder = true };
            ImGui.EndPopup();
        }

        if (DragDropPayloads.BeginSource(DragDropPayloads.ContentBrowserItem, folderPath, displayName))
        {
            ImGui.Text(displayName);
            ImGui.EndDragDropSource();
        }

        if (open)
        {
            DrawFolderContents(state, folderPath, ref sceneToOpen);
            ImGui.TreePop();
        }
    }

    /// <summary>One file as a leaf row in the Files tree. Returns the full path of a scene to open, if the user
    /// just double-clicked one.</summary>
    private static string? DrawAssetRow(EditorState state, AssetInfo asset)
    {
        var flags = ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.SpanAvailWidth;
        if (state.SelectedAsset == asset) flags |= ImGuiTreeNodeFlags.Selected;

        ImGui.PushStyleColor(ImGuiCol.Text, AssetRowColor(asset.Type));
        // Two leading spaces reserve room for the icon/thumbnail drawn right after this — see
        // EditorIcons.DrawTreeRowIcon's doc comment.
        bool open = ImGui.TreeNodeEx("  " + Path.GetFileName(asset.RelativePath) + "##row_" + asset.RelativePath, flags);
        ImGui.PopStyleColor();

        // A texture shows its own real thumbnail (same asset.ThumbnailId the Assets grid's ImageButton
        // uses) rather than a generic glyph — a small but real preview is more useful here than an icon,
        // and it costs nothing extra: the thumbnail is already loaded and bound for the grid.
        //
        // Painted via the draw list (AddImage), NOT ImGui.Image — ImGui.Image is a real widget with its own
        // hit-box and becomes "the last item", which would hijack the IsItemClicked/BeginPopupContextItem/
        // drag-source calls below (they all target "the last item") away from the TreeNodeEx row and onto
        // the thumbnail instead, silently breaking right-click-delete and drag-to-move for texture rows.
        // AddImage just paints pixels, exactly like DrawTreeRowIcon's icons — it never touches "last item".
        if (asset.Type == AssetType.Texture && asset.Texture != null)
        {
            Vector2 min = ImGui.GetItemRectMin();
            Vector2 max = ImGui.GetItemRectMax();
            float size = (max.Y - min.Y) * 0.72f;
            float x = min.X + ImGui.GetTreeNodeToLabelSpacing() - size - 2f;
            float y = min.Y + (max.Y - min.Y - size) * 0.5f;
            ImGui.GetWindowDrawList().AddImage(asset.ThumbnailId, new Vector2(x, y), new Vector2(x + size, y + size));
        }
        else
        {
            EditorIcons.DrawTreeRowIcon(GetAssetTypeIcon(asset.Type), ImGui.ColorConvertFloat4ToU32(AssetRowColor(asset.Type)));
        }

        if (ImGui.IsItemClicked()) state.SelectAsset(asset);
        var openScene = HandleAssetItemInteractions(asset, state);

        if (open) ImGui.TreePop();
        return openScene;
    }

    /// <summary>A soft type tint for file rows so scenes / prefabs / scripts / audio can be told apart at a
    /// glance in a plain-text tree (textures keep the normal text color).</summary>
    private static Vector4 AssetRowColor(AssetType type) => type switch
    {
        AssetType.Scene => new Vector4(0.62f, 0.80f, 1f, 1f),
        AssetType.Prefab => new Vector4(0.55f, 0.75f, 1f, 1f),
        AssetType.Script => new Vector4(0.62f, 0.90f, 0.62f, 1f),
        AssetType.Audio => new Vector4(0.85f, 0.70f, 1f, 1f),
        _ => ImGui.GetStyle().Colors[(int)ImGuiCol.Text],
    };

    /// <summary>Which EditorIcons glyph represents an asset type — the single switch DrawAssetTile (the
    /// Assets grid), DrawFolderNode and DrawAssetRow (the Files tree) all share, so the grid and the tree
    /// can never silently drift into showing a different icon for the same file. Texture is deliberately
    /// absent: a texture shows its own real thumbnail instead of a generic glyph (see both call sites).</summary>
    private static Action<ImDrawListPtr, Vector2, Vector2, uint> GetAssetTypeIcon(AssetType type) => type switch
    {
        AssetType.Prefab => EditorIcons.Prefab,
        AssetType.Scene => EditorIcons.Scene,
        AssetType.Script => EditorIcons.Script,
        AssetType.Audio => EditorIcons.Audio,
        _ => EditorIcons.Script,
    };

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
    /// comment).
    ///
    /// Waiting for the top of the NEXT frame only actually guarantees nothing that frame has referenced it
    /// yet if this runs before every panel that frame, not merely before this one's own tiles — which is
    /// why EditorApp.BuildUI calls this as its very first statement rather than this class calling it from
    /// its own Draw. A real, previously-shipped crash came from exactly that gap: InspectorPanel draws
    /// before the bottom (now Blocks) panel every frame, and if the asset being deleted was also state.SelectedAsset,
    /// DrawAssetInspector had already queued an ImGui.Image draw command against its ThumbnailId earlier
    /// in that same frame — before this method (back then called later, from inside the bottom panel) unbound that exact
    /// id. Dear ImGui doesn't retract an already-queued command; the renderer throws the moment it tries to
    /// render it (see ImGuiRenderer.RenderCommandLists' "Unbound ImGui texture id" check). A freshly-created
    /// primitive shape (see AssetDropHandler.CreatePrimitiveShape/PrimitiveShapeGenerator) is a common way to
    /// hit this specific case: create one, click its thumbnail in the Assets grid to select it (which is
    /// what populates state.SelectedAsset and makes the Inspector start previewing it every frame), then
    /// delete it or its containing folder — but the same crash could happen for any selected texture.</summary>
    internal static void ProcessConfirmedDelete(EditorState state)
    {
        var pending = state.PendingDelete;
        if (pending == null || !pending.Confirmed) return;
        state.PendingDelete = null;

        // Every asset path this delete is about to remove — just the one for a single asset, or every
        // asset currently tracked underneath it for a folder. Captured BEFORE the delete call below, since
        // DeleteFolder drops these from the index (and disposes their Texture2D/SoundEffect) as part of
        // doing its job — there'd be nothing left to enumerate afterward.
        var affectedPaths = pending.IsFolder
            ? state.Assets.Assets
                .Where(a => a.RelativePath.StartsWith(pending.RelativePath + "/", StringComparison.Ordinal))
                .Select(a => a.RelativePath)
                .ToArray()
            : new[] { pending.RelativePath };

        bool ok = pending.IsFolder
            ? state.Assets.DeleteFolder(pending.RelativePath)
            : state.Assets.DeleteAsset(pending.RelativePath);

        if (!ok)
        {
            state.LogMessage($"ERROR: couldn't delete '{pending.DisplayName}' — it may be open in another program.");
            return;
        }

        // The delete above just disposed each of affectedPaths' Texture2D/SoundEffect. Clear any
        // SpriteRenderer.Texture / AudioSource.Clip in either live scene that was still pointing at one of
        // them — see AssetDatabase.ClearAssetReferences' doc comment for why this can't be skipped.
        state.Assets.ClearAssetReferences(state.EditScene, affectedPaths);
        if (state.PlayScene != null)
            state.Assets.ClearAssetReferences(state.PlayScene, affectedPaths);

        state.LogMessage($"Deleted {(pending.IsFolder ? "folder" : "asset")} '{pending.DisplayName}'.");

        // Covers the asset itself being deleted AND it having simply lived inside a just-deleted folder —
        // the old check here only handled the former, so deleting a folder out from under a selected asset
        // left state.SelectedAsset pointing at a disposed Texture2D and an unbound ImGui thumbnail id,
        // which DrawAssetInspector would then try to draw and the renderer would throw on.
        if (state.SelectedAsset != null && affectedPaths.Contains(state.SelectedAsset.RelativePath))
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
            if (EditorIcons.Button(asset.RelativePath, new Vector2(ThumbnailSize, ThumbnailSize), false, GetAssetTypeIcon(asset.Type)))
                state.SelectAsset(asset);
        }

        if (selected) ImGui.PopStyleColor();

        openScene = HandleAssetItemInteractions(asset, state);

        ImGui.TextWrapped(Truncate(asset.DisplayName, 10));
        ImGui.EndGroup();
        return openScene;
    }

    /// <summary>Everything a file can DO once its widget (a grid tile's button or a Files-tree row) has been
    /// submitted as the most recent ImGui item: right-click → Delete, drag it (to move it between folders, or
    /// out into the scene), double-click to act on it (open a scene, instantiate a prefab, open a script).
    /// Shared by DrawAssetTile and DrawAssetRow so the two can never behave differently. MUST be called
    /// immediately after the item it applies to — every call below refers to "the last item". Returns the
    /// full path of a scene to open, if the user just double-clicked one.</summary>
    private static string? HandleAssetItemInteractions(AssetInfo asset, EditorState state)
    {
        string? openScene = null;

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
            var error = ExternalEditorLauncher.OpenScript(state.ProjectPath, asset.FullPath, state.LogMessage);
            if (error != null) state.LogMessage($"ERROR: {error}");
        }

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
