using System.Security.Cryptography;
using System.Text.Json;

namespace ClassicMhTools.Core.CustomQuests;

public sealed record OfficialQuestReference(ushort Id, int Length, string Sha256);
public sealed record QuestSpritePosition(int X, int Y);
public sealed record QuestMapZone(byte Id, float MinX, float MaxX, float MinZ, float MaxZ);
public sealed record QuestItemPreset(byte Rank, uint Monster, QuestSupplyDraft[] Supplies, QuestRewardDraft[] Rewards);

public sealed class QuestReferenceCatalog
{
    public Dictionary<uint, string> MonsterNames { get; init; } = new();
    public Dictionary<uint, int> MonsterIcons { get; init; } = new();
    public Dictionary<ushort, string> PreviewNames { get; init; } = new();
    public Dictionary<byte, QuestMapZone[]> MapZones { get; init; } = new();
    public QuestItemPreset[] ItemPresets { get; init; } = [];
    public uint[] LargeMonsterIds { get; init; } = [];
    public ushort[] UnrestrictedEventIds { get; init; } = [];
    public Dictionary<uint, byte> FirstWaveMaps { get; init; } = new();
    public Dictionary<string, Dictionary<uint, string>> Options { get; init; } = new();
    public Dictionary<byte, string> MapNames { get; init; } = new();
    public Dictionary<int, QuestSpritePosition> SpritePositions { get; init; } = new();
    public OfficialQuestReference[] OfficialQuests { get; init; } = [];

    public static QuestReferenceCatalog Load(string path) =>
        JsonSerializer.Deserialize<QuestReferenceCatalog>(File.ReadAllText(path)) ??
        throw new InvalidDataException("The quest reference catalog is empty.");

    public bool IsOfficial(QuestDocument document)
    {
        var reference = OfficialQuests.FirstOrDefault(q => q.Id == document.Id);
        if (reference == null || reference.Length < 0x100 || reference.Length > document.Length) return false;
        // Installed slots may contain additional game padding; compare the known MIB prefix.
        var digest = SHA256.HashData(document.ToArray().AsSpan(0, reference.Length));
        return Convert.ToHexString(digest).Equals(reference.Sha256, StringComparison.OrdinalIgnoreCase);
    }
}
