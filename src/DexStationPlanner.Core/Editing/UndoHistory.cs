namespace DexStationPlanner.Core.Editing;

/// <summary>
/// Snapshot-based undo/redo. Each checkpoint stores the serialized state; identical consecutive
/// snapshots are ignored so callers can checkpoint freely.
/// </summary>
public sealed class UndoHistory(Func<string> capture, Action<string> restore, int limit = 200)
{
    private readonly List<string> _undo = [];
    private readonly List<string> _redo = [];
    private string? _current;

    public event EventHandler? Changed;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Starts over with the current state as the baseline.</summary>
    public void Reset()
    {
        _undo.Clear();
        _redo.Clear();
        _current = capture();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Records the current state if it differs from the last checkpoint. Returns true if recorded.</summary>
    public bool Checkpoint()
    {
        var state = capture();
        if (state == _current) return false;
        if (_current is not null)
        {
            _undo.Add(_current);
            if (_undo.Count > limit) _undo.RemoveAt(0);
        }
        _redo.Clear();
        _current = state;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Undo()
    {
        Checkpoint(); // Don't lose edits made since the last checkpoint.
        if (_undo.Count == 0) return;
        _redo.Add(_current!);
        _current = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        restore(_current);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Add(_current!);
        _current = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        restore(_current);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
