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

    /// <summary>Call inside BeginDragDropTarget()/EndDragDropTarget(). Returns the dropped asset's relative path, or null.</summary>
    public static unsafe string? AcceptTarget(string payloadType)
    {
        var payload = ImGui.AcceptDragDropPayload(payloadType);
        if (payload.NativePtr != null && payload.IsDelivery())
            return CurrentPath;
        return null;
    }
}
