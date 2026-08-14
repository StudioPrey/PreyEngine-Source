namespace MyEngine.Editor.UndoSystem;

/// <summary>
/// One undoable editor action. Commands are expected to already be "applied" (the change has happened)
/// at the moment they're pushed onto the UndoStack — pushing just means "remember how to undo/redo this."
/// </summary>
public interface IEditorCommand
{
    /// <summary>Short, human-readable label shown next to Undo/Redo in the Edit menu (e.g. "Move Player").</summary>
    string Description { get; }

    /// <summary>Re-applies the change. Called on Redo (never called for the initial apply — the caller does that itself).</summary>
    void Execute();

    /// <summary>Reverses the change.</summary>
    void Undo();
}
