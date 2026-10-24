using MH4UModern.Services;

namespace ClassicMhTools.Core.Tests;

public sealed class UndoManagerTests
{
    [Fact]
    public void UndoAndRedoRestoreEditsInOrder()
    {
        var manager = new UndoManager();
        var value = 2;
        manager.Record(() => value = 0, () => value = 1, "first edit");
        manager.Record(() => value = 1, () => value = 2, "second edit");

        manager.Undo();
        Assert.Equal(1, value);
        manager.Undo();
        Assert.Equal(0, value);
        Assert.False(manager.CanUndo);
        manager.Redo();
        manager.Redo();
        Assert.Equal(2, value);
        Assert.False(manager.CanRedo);
    }

    [Fact]
    public void NewEditAfterUndoDiscardsTheOldRedoBranch()
    {
        var manager = new UndoManager();
        manager.Record(() => { }, () => { }, "old edit");
        manager.Undo();
        Assert.True(manager.CanRedo);
        manager.Record(() => { }, () => { }, "replacement edit");
        Assert.False(manager.CanRedo);
        Assert.Equal("Undo replacement edit", manager.UndoLabel);
    }

    [Fact]
    public void ReentrantChangesDoNotRecordExtraUndoActions()
    {
        var manager = new UndoManager();
        manager.Record(
            () => manager.Record(() => { }, () => { }, "binding callback"),
            () => { },
            "original edit");
        manager.Undo();
        Assert.False(manager.CanUndo);
        Assert.True(manager.CanRedo);
        Assert.False(manager.IsApplying);
    }
}
