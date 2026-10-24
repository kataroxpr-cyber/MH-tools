using System.Globalization;
using System.IO;
using mh4edit;

namespace MH4UModern.Services;

public sealed record DecorationSkillEffect(string Skill, int Points);

public sealed record DecorationChoice(
    ushort EditorId,
    string Name,
    int Slots,
    int Rarity,
    IReadOnlyList<DecorationSkillEffect> Effects)
{
    public string SlotText => Slots > 0 ? $"{Slots} slot{(Slots == 1 ? string.Empty : "s")}" : "Slot cost unknown";
    public string RarityText => Rarity > 0 ? $"Rarity {Rarity}" : "Rarity —";
    public string SkillSummary => Effects.Count == 0
        ? "No skill metadata"
        : string.Join("  ·  ", Effects.Select(effect => $"{effect.Skill} {(effect.Points >= 0 ? "+" : string.Empty)}{effect.Points}"));
}

/// <summary>
/// Decoration metadata from the bundled MH4U Database catalog, joined at runtime to
/// mh4edit's decoration index. The save stores the mh4edit decoration index, so the
/// picker always returns that ID rather than the Android database ID.
/// </summary>
public static class DecorationCatalogService
{
    private sealed class Builder
    {
        public int Slots;
        public int Rarity;
        public List<DecorationSkillEffect> Effects { get; } = new();
    }

    private static readonly Lazy<IReadOnlyList<DecorationChoice>> Choices = new(LoadChoices);
    private static readonly Lazy<IReadOnlyList<string>> SkillsInternal = new(() => Choices.Value
        .SelectMany(choice => choice.Effects)
        .Where(effect => effect.Points > 0 && !string.IsNullOrWhiteSpace(effect.Skill))
        .Select(effect => effect.Skill)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(skill => skill, StringComparer.OrdinalIgnoreCase)
        .ToArray());

    public static IReadOnlyList<DecorationChoice> All => Choices.Value;
    public static IReadOnlyList<string> Skills => SkillsInternal.Value;

    public static DecorationChoice? Get(ushort editorId)
        => editorId < Choices.Value.Count ? Choices.Value[editorId] : null;

    public static IReadOnlyList<DecorationChoice> ForSkill(string? skill)
    {
        if (string.IsNullOrWhiteSpace(skill)) return Array.Empty<DecorationChoice>();
        return Choices.Value
            .Where(choice => choice.EditorId != 0 && choice.Effects.Any(effect => effect.Points > 0 && string.Equals(effect.Skill, skill, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(choice => choice.Slots)
            .ThenBy(choice => choice.Rarity)
            .ThenBy(choice => choice.EditorId)
            .ToArray();
    }

    private static IReadOnlyList<DecorationChoice> LoadChoices()
    {
        var metadata = new Dictionary<string, Builder>(StringComparer.OrdinalIgnoreCase);
        var path = Path.Combine(GameCatalogService.CatalogRoot, "decoration-skills.tsv");
        if (File.Exists(path))
        {
            var first = true;
            foreach (var raw in File.ReadLines(path))
            {
                if (first) { first = false; continue; }
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var p = raw.Split('\t');
                if (p.Length < 5) continue;
                var name = p[0].Trim();
                if (!metadata.TryGetValue(name, out var builder))
                {
                    builder = new Builder
                    {
                        Slots = Int(p[1]),
                        Rarity = Int(p[2])
                    };
                    metadata[name] = builder;
                }
                var skill = p[3].Trim();
                if (skill.Length > 0)
                    builder.Effects.Add(new DecorationSkillEffect(skill, Int(p[4])));
            }
        }

        var names = MonHunEquipStatic.allJewels;
        var result = new List<DecorationChoice>(names.Length);
        for (ushort i = 0; i < names.Length; i++)
        {
            var name = names[i];
            metadata.TryGetValue(name.Trim(), out var builder);
            result.Add(new DecorationChoice(
                i,
                name.Trim(),
                builder?.Slots ?? 0,
                builder?.Rarity ?? 0,
                builder?.Effects.ToArray() ?? Array.Empty<DecorationSkillEffect>()));
        }
        return result;
    }

    private static int Int(string value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
}
