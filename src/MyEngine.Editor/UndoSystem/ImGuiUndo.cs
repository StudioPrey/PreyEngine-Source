using ImGuiNET;

namespace MyEngine.Editor.UndoSystem;

/// <summary>
/// Call once per editable Inspector field, right after drawing its ImGui widget and applying any live
/// value change. Uses ImGui.IsItemActivated() / IsItemDeactivatedAfterEdit() to detect the start and end
/// of an edit (a whole drag, or a whole typing session) — since only one ImGui widget can be "active" at
/// a time, a single held "old value" slot works regardless of how many different fields call this.
/// </summary>
public static class ImGuiUndo
{
    private static object? _capturedOldValue;

    public static void Track<T>(UndoStack undo, string description, T oldValueThisFrame, T newValueThisFrame, Action<T> setter)
    {
        if (ImGui.IsItemActivated())
            _capturedOldValue = oldValueThisFrame;

        if (ImGui.IsItemDeactivatedAfterEdit() && _capturedOldValue is T oldValue)
        {
            if (!EqualityComparer<T>.Default.Equals(oldValue, newValueThisFrame))
                undo.Push(new PropertyChangeCommand<T>(description, setter, oldValue, newValueThisFrame));
            _capturedOldValue = null;
        }
    }
}
