namespace MH4UModern.Models;

public sealed record ChangeRecord(string Area, string Description, string? Before = null, string? After = null)
{
    public string Display => Before is null && After is null
        ? Description
        : $"{Description}: {Before ?? "—"}  →  {After ?? "—"}";
}
