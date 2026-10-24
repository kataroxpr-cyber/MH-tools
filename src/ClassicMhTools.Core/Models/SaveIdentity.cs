using mh4edit;

namespace MH4UModern.Models;

public sealed record SaveIdentity(string Name, byte Gender, uint HunterRank, uint Funds)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Unnamed hunter" : Name;
    public string GenderLabel => Gender switch { 0 => "Male", 1 => "Female", _ => "Unknown gender" };
    public string GenderSymbol => Gender switch { 0 => "\u2642", 1 => "\u2640", _ => "?" };
    public string RankLabel => $"HR {HunterRank:N0}";

    public static SaveIdentity FromCharacter(MonHunCharacter character)
        => new(character.Name, character.gender, character.HunterRank, character.Funds);
}

public sealed record EmulatorSaveInfo(string Path, string Region, long Bytes, DateTime Modified,
    SaveIdentity? Character, string? Error)
{
    public string FileName => System.IO.Path.GetFileName(Path);
    public string Name => Character?.DisplayName ?? FileName;
    public string GenderSymbol => Character?.GenderSymbol ?? string.Empty;
    public string GenderLabel => Character?.GenderLabel ?? string.Empty;
    public string RankLabel => Character?.RankLabel ?? "Unreadable save";
    public string Details => $"{FileName}  |  MH4U {Region}  |  {Modified:g}  |  {Bytes:N0} bytes";
    public bool IsReadable => Character is not null;
    public string ToolTip => Error is null ? Path : $"{Path}\n{Error}";
}

public sealed record EmulatorScanResult(string StoragePath, IReadOnlyList<EmulatorSaveInfo> Saves,
    IReadOnlyList<string> Warnings);
