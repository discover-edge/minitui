namespace MiniTui;

/// <summary>What an edit did, for grouping undo steps (1.x spec §10.6).</summary>
internal enum EditKind
{
    /// <summary>A typed character.</summary>
    Type,

    /// <summary>Backspace or Ctrl+Backspace.</summary>
    DeleteBackward,

    /// <summary>Delete.</summary>
    DeleteForward,

    /// <summary>A one-off edit that never joins another: paste, cut, Enter, deleting a selection, Ctrl+W/U/K.</summary>
    Other,
}

/// <summary>The undo and redo stacks of a text state: a snapshot from before each edit group (1.x spec §10.6).</summary>
internal sealed class EditHistory<T>
{
    private const int Limit = 100;

    private readonly List<T> _undo = [];
    private readonly List<T> _redo = [];
    private EditKind? _open;
    private bool _typedSpace;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>Forget everything: the text was replaced.</summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _open = null;
    }

    /// <summary>End the open group: the cursor or selection moved.</summary>
    public void Break() => _open = null;

    /// <summary>
    /// Call before an edit that changes the text. It joins the open group if it's the same kind (not
    /// <see cref="EditKind.Other"/>), except that a typed non-space after a typed space starts a new one.
    /// </summary>
    public void Record(EditKind kind, Func<T> before, bool typedSpace = false)
    {
        var joins = _open == kind && kind != EditKind.Other && !(kind == EditKind.Type && _typedSpace && !typedSpace);
        if (!joins)
        {
            _undo.Add(before());
            if (_undo.Count > Limit) _undo.RemoveAt(0);
        }
        _redo.Clear();
        _open = kind;
        _typedSpace = typedSpace;
    }

    /// <summary>Take back the last group: <paramref name="restore"/> gets the snapshot from before it.</summary>
    public bool Undo(T current, out T restore) => Move(_undo, _redo, current, out restore);

    /// <summary>Do again the last group undone.</summary>
    public bool Redo(T current, out T restore) => Move(_redo, _undo, current, out restore);

    private bool Move(List<T> from, List<T> to, T current, out T restore)
    {
        _open = null;
        if (from.Count == 0)
        {
            restore = current;
            return false;
        }
        restore = from[^1];
        from.RemoveAt(from.Count - 1);
        to.Add(current);
        return true;
    }
}
