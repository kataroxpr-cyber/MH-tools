namespace MH4UModern.Services;

public sealed class UndoManager
{
    private readonly Stack<EditAction> _undo = new();
    private readonly Stack<EditAction> _redo = new();

    public bool IsApplying { get; private set; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string UndoLabel => CanUndo ? $"Undo {_undo.Peek().Description}" : "Undo";
    public string RedoLabel => CanRedo ? $"Redo {_redo.Peek().Description}" : "Redo";

    public event EventHandler? StateChanged;

    public void Record(Action undo, Action redo, string description)
    {
        if (IsApplying)
            return;
        _undo.Push(new EditAction(undo, redo, description));
        _redo.Clear();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        var action = _undo.Pop();
        try
        {
            IsApplying = true;
            action.Undo();
        }
        finally
        {
            IsApplying = false;
        }
        _redo.Push(action);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        var action = _redo.Pop();
        try
        {
            IsApplying = true;
            action.Redo();
        }
        finally
        {
            IsApplying = false;
        }
        _undo.Push(action);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed record EditAction(Action Undo, Action Redo, string Description);
}
