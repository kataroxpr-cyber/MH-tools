namespace MH4UModern.Services;

public sealed class DirtyTracker
{
    public bool IsDirty { get; private set; }
    public event EventHandler? Changed;

    public void MarkDirty()
    {
        if (IsDirty) return;
        IsDirty = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void MarkClean()
    {
        if (!IsDirty) return;
        IsDirty = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
