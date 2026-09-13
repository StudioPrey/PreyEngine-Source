namespace MyEngine.Editor.UndoSystem;

/// <summary>
/// Classic two-stack undo/redo. The person doing an edit is responsible for applying it immediately
/// (e.g. setting a property, creating a GameObject) and then calling Push with a command that knows
/// how to reverse/reapply it — Push itself does not execute anything.
/// </summary>
public sealed class UndoStack
{
    private const int MaxHistory = 200;

    private readonly List<IEditorCommand> _undo = new();
    private readonly List<IEditorCommand> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? NextUndoDescription => _undo.Count > 0 ? _undo[^1].Description : null;
    public string? NextRedoDescription => _redo.Count > 0 ? _redo[^1].Description : null;

    /// <summary>Fires after Push, Undo, or Redo — i.e. any time the scene's actual content just changed.
    /// EditorState listens to this to maintain its IsDirty flag, so "does this scene have unsaved changes"
    /// stays correct without every single call site that mutates the scene needing to remember to say so.</summary>
    public event Action? Changed;

    /// <summary>Records a command that has already been applied. Clears the redo stack (a fresh action
    /// invalidates any "future" the user had undone into).</summary>
    public void Push(IEditorCommand command)
    {
        _undo.Add(command);
        if (_undo.Count > MaxHistory)
            _undo.RemoveAt(0);
        _redo.Clear();
        Changed?.Invoke();
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        var command = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        command.Undo();
        _redo.Add(command);
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        var command = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        command.Execute();
        _undo.Add(command);
        Changed?.Invoke();
    }

    /// <summary>Wipes all history. Call this whenever the underlying Scene reference changes (New/Open/Play/Stop) —
    /// commands captured against a scene that no longer exists would corrupt a different one if replayed.
    /// Deliberately does NOT fire Changed — clearing history isn't a content change by itself.</summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
