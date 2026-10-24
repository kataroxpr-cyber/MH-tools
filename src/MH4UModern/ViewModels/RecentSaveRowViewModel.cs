using System.IO;
using mh4edit;

namespace MH4UModern.ViewModels;

public sealed class RecentSaveRowViewModel
{
    public RecentSaveRowViewModel(string path)
    {
        Path = path;
        FileName = System.IO.Path.GetFileName(path);
        PathSnippet = BuildPathSnippet(path);

        try
        {
            var save = new MonHunSave(path);
            var character = save.slot;
            Name = string.IsNullOrWhiteSpace(character.Name) ? FileName : character.Name;
            HunterRank = character.HunterRank;
            Funds = character.Funds;
            IsReadable = true;
        }
        catch
        {
            Name = FileName;
            IsReadable = false;
        }
    }

    public string Path { get; }
    public string FileName { get; }
    public string Name { get; }
    public uint HunterRank { get; }
    public uint Funds { get; }
    public bool IsReadable { get; }
    public string PathSnippet { get; }
    public string Stats => IsReadable ? $"HR {HunterRank:N0}   ·   {Funds:N0}z" : "Save details unavailable";

    private static string BuildPathSnippet(string path)
    {
        try
        {
            var file = new FileInfo(path);
            var parent = file.Directory;
            var grandParent = parent?.Parent;
            if (parent is null) return FileNameOnly(path);
            if (grandParent is null) return $"{parent.Name}  ›  {file.Name}";
            return $"…  {grandParent.Name}  ›  {parent.Name}  ›  {file.Name}";
        }
        catch
        {
            return FileNameOnly(path);
        }
    }

    private static string FileNameOnly(string path)
    {
        try { return System.IO.Path.GetFileName(path); }
        catch { return path; }
    }
}
