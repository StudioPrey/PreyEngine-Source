using ImGuiNET;

namespace MyEngine.Editor;

/// <summary>
/// ImGui drag-and-drop payloads are meant for small POD byte buffers, not managed strings. Rather than
/// marshal a path through unmanaged memory, we stash the "real" payload (the asset's relative path) in a
/// static field for the duration of the drag and send ImGui a 1-byte dummy buffer just to satisfy its API.
/// This is a common, pragmatic pattern for C# ImGui bindings and is safe here because drag & drop is a
/// single-threaded, single-drag-at-a-time UI interaction.
/// </summary>
public static class DragDropPayloads
{
    public const string Texture = "ASSET_TEXTURE";
    public const string Prefab = "ASSET_PREFAB";

    /// <summary>Generic payload for reordering within the Content Browser itself (dragging any asset OR
    /// folder onto a folder to move it there) — separate from Texture/Prefab above, which are specifically
    /// for dragging an asset out into the Viewport to instantiate it.</summary>
    public const string ContentBrowserItem = "CONTENT_BROWSER_ITEM";

    private static readonly byte[] Dummy = { 1 };
    public static string? CurrentPath { get; private set; }

    /// <summary>Wrap around your draggable widget: if this returns true, call ImGui.EndDragDropSource() when done.</summary>
    public static unsafe bool BeginSource(string payloadType, string assetRelativePath, string previewLabel)
    {
        if (!ImGui.BeginDragDropSource()) return false;

        CurrentPath = assetRelativePath;
        fixed (byte* ptr = Dummy)
            ImGui.SetDragDropPayload(payloadType, (IntPtr)ptr, 1);
        ImGui.Text(previewLabel);
        return true;
    }

    /// <summary>Call inside an already-open BeginDragDropSource() block (i.e. after BeginSource returned
    /// true) to let the same drag gesture also satisfy a second drop-target type — e.g. a texture tile is
    /// draggable both into the Viewport (Texture payload) and onto a folder to move it (ContentBrowserItem).</summary>
    public static unsafe void AddPayloadType(string payloadType)
    {
        fixed (byte* ptr = Dummy)
            ImGui.SetDragDropPayload(payloadType, (IntPtr)ptr, 1);
    }

    /// <summary>Call inside BeginDragDropTarget()/EndDragDropTarget(). Returns the dropped asset's relative path, or null.</summary>
    public static unsafe string? AcceptTarget(string payloadType)
    {
        var payload = ImGui.AcceptDragDropPayload(payloadType);
        if (payload.NativePtr != null && payload.IsDelivery())
            return CurrentPath;
        return null;
    }
}
