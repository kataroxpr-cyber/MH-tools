using System.Text;
using mh4edit;

namespace MH4UModern.ViewModels;

public sealed class PalicoRowViewModel : BindableBase
{
    public PalicoRowViewModel(int index, Palico model)
    {
        Index = index;
        Model = model;
    }

    public int Index { get; }
    public int Slot => Index + 1;
    public Palico Model { get; }

    // Unused reserve records can contain bytes that decode to control characters or malformed
    // UTF-16. Keep the raw model untouched, but never hand those code units directly to WPF.
    public string Name
    {
        get
        {
            var safe = SanitizeForDisplay(Model.Name);
            return string.IsNullOrWhiteSpace(safe) ? "(Empty)" : safe;
        }
    }

    public string ExpText
    {
        get
        {
            try { return Model.Exp.ToString("N0"); }
            catch { return Model.Exp.ToString(); }
        }
    }

    public void Refresh()
    {
        Raise(nameof(Name));
        Raise(nameof(ExpText));
    }

    private static string SanitizeForDisplay(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (char.IsHighSurrogate(ch))
            {
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    sb.Append(ch);
                    sb.Append(value[++i]);
                }
                continue;
            }
            if (char.IsLowSurrogate(ch)) continue;
            if (char.IsControl(ch) && !char.IsWhiteSpace(ch)) continue;
            sb.Append(ch);
        }
        return sb.ToString().Trim();
    }
}
