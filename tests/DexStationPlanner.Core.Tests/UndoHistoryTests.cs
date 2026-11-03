using DexStationPlanner.Core.Editing;

namespace DexStationPlanner.Core.Tests;

public class UndoHistoryTests
{
    private string _state = "a";

    private UndoHistory Create()
    {
        var h = new UndoHistory(() => _state, s => _state = s);
        h.Reset();
        return h;
    }

    [Fact]
    public void Undo_and_redo_walk_checkpoints()
    {
        var h = Create();
        _state = "b"; h.Checkpoint();
        _state = "c"; h.Checkpoint();

        h.Undo();
        Assert.Equal("b", _state);
        h.Undo();
        Assert.Equal("a", _state);
        Assert.False(h.CanUndo);

        h.Redo();
        h.Redo();
        Assert.Equal("c", _state);
        Assert.False(h.CanRedo);
    }

    [Fact]
    public void Identical_checkpoints_are_ignored()
    {
        var h = Create();
        Assert.False(h.Checkpoint());
        _state = "b";
        Assert.True(h.Checkpoint());
        Assert.False(h.Checkpoint());
        h.Undo();
        Assert.Equal("a", _state);
        Assert.False(h.CanUndo);
    }

    [Fact]
    public void Undo_captures_uncheckpointed_edits_first()
    {
        var h = Create();
        _state = "b"; // edited but not yet checkpointed (e.g. debounce pending)
        h.Undo();
        Assert.Equal("a", _state);
        h.Redo();
        Assert.Equal("b", _state);
    }

    [Fact]
    public void New_edit_clears_redo()
    {
        var h = Create();
        _state = "b"; h.Checkpoint();
        h.Undo();
        _state = "x"; h.Checkpoint();
        Assert.False(h.CanRedo);
    }
}
