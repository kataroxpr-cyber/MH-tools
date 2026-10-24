using System.Globalization;
using System.IO;
using System.Text;

namespace MH4UModern.Services;

public sealed record ItemCatalogEntry(
    ushort EditorId,
    int DatabaseId,
    string Name,
    string Type,
    string SubType,
    int Rarity,
    int CarryCapacity,
    int? Buy,
    int? Sell,
    string IconName,
    string Description)
{
    public string Category => Type switch
    {
        "Ammo" or "Coating" => "Ammo & coatings",
        "Nectar" => "Nectar",
        "Decoration" => "Decorations",
        "Coin/Ticket" => "Tickets & coins",
        "Scrap" => "Palico scraps",
        "Supply" => "Quest & supply",
        "Consumable" or "Tool" or "Book" or "Meat" or "Bait" or "Plant" or "Wystone" => "Consumables & tools",
        _ => "Materials & other"
    };

    public string RarityText => Rarity > 0 ? $"Rarity {Rarity}" : string.Empty;

    public string EconomyText
    {
        get
        {
            var parts = new List<string>();
            if (CarryCapacity > 0) parts.Add($"Carry {CarryCapacity}");
            if (Sell.HasValue && Sell.Value > 0) parts.Add($"Sell {Sell.Value:N0}z");
            if (Buy.HasValue && Buy.Value > 0) parts.Add($"Buy {Buy.Value:N0}z");
            return string.Join(" · ", parts);
        }
    }
}

public sealed record WeaponCatalogEntry(
    string Name,
    int DatabaseId,
    string WeaponType,
    int Rarity,
    int Attack,
    int? MaxAttack,
    string Element,
    int? ElementAttack,
    string Element2,
    int? Element2Attack,
    string Awaken,
    int? AwakenAttack,
    int Defense,
    string Affinity,
    int NumSlots,
    bool Final,
    string Sharpness,
    string HornNotes,
    string ShellingType,
    string Phial,
    string Charges,
    string Coatings,
    string Recoil,
    string ReloadSpeed,
    string RapidFire,
    string Deviation,
    string Ammo,
    string SpecialAmmo)
{
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (Attack > 0) parts.Add($"ATK {Attack}");
            if (!string.IsNullOrWhiteSpace(Affinity)) parts.Add($"Affinity {Affinity}%");
            if (!string.IsNullOrWhiteSpace(Element))
                parts.Add(ElementAttack.HasValue && ElementAttack.Value > 0 ? $"{Element} {ElementAttack.Value}" : Element);
            else if (!string.IsNullOrWhiteSpace(Awaken))
                parts.Add(AwakenAttack.HasValue && AwakenAttack.Value > 0 ? $"Awaken {Awaken} {AwakenAttack.Value}" : $"Awaken {Awaken}");
            if (Defense != 0) parts.Add($"DEF {Defense:+#;-#;0}");
            parts.Add($"Slots {NumSlots}");
            return string.Join(" · ", parts);
        }
    }
}

public sealed record ArmorCatalogEntry(
    string Name,
    int DatabaseId,
    string Slot,
    int Rarity,
    int Defense,
    int? MaxDefense,
    int FireRes,
    int WaterRes,
    int ThunderRes,
    int IceRes,
    int DragonRes,
    string Gender,
    string HunterType,
    int NumSlots)
{
    public string Summary
    {
        get
        {
            var defense = MaxDefense.HasValue && MaxDefense.Value > 0 && MaxDefense.Value != Defense ? $"DEF {Defense}→{MaxDefense.Value}" : $"DEF {Defense}";
            return $"{defense} · Slots {NumSlots} · F/W/T/I/D {FireRes:+#;-#;0}/{WaterRes:+#;-#;0}/{ThunderRes:+#;-#;0}/{IceRes:+#;-#;0}/{DragonRes:+#;-#;0}";
        }
    }
}

/// <summary>
/// Read-only MH4U reference catalog exported from the user-provided MH4U Database 1.08 APK.
/// The editor intentionally does not require SQLite at runtime: the relevant tables are shipped as TSV.
/// </summary>
public static class GameCatalogService
{
    private static readonly Lazy<Dictionary<ushort, ItemCatalogEntry>> Items = new(LoadItems);
    private static readonly Lazy<Dictionary<string, WeaponCatalogEntry>> Weapons = new(LoadWeapons);
    private static readonly Lazy<Dictionary<string, ArmorCatalogEntry>> Armor = new(LoadArmor);

    public static ItemCatalogEntry? GetItem(ushort editorId)
        => Items.Value.TryGetValue(editorId, out var value) ? value : null;

    public static WeaponCatalogEntry? GetWeapon(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return Weapons.Value.TryGetValue(NormalizeName(name), out var value) ? value : null;
    }

    public static ArmorCatalogEntry? GetArmor(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return Armor.Value.TryGetValue(NormalizeName(name), out var value) ? value : null;
    }

    public static string CatalogRoot => Path.Combine(AppContext.BaseDirectory, "assets", "catalog");
    public static string ItemIconRoot => Path.Combine(CatalogRoot, "item-icons");

    private static Dictionary<ushort, ItemCatalogEntry> LoadItems()
    {
        var result = new Dictionary<ushort, ItemCatalogEntry>();
        foreach (var p in ReadTable("items.tsv"))
        {
            if (p.Length < 11 || !ushort.TryParse(p[0], out var editorId)) continue;
            result[editorId] = new ItemCatalogEntry(
                editorId,
                Int(p[1]),
                p[2], p[3], p[4],
                Int(p[5]), Int(p[6]), NullableInt(p[7]), NullableInt(p[8]),
                p[9], p[10]);
        }
        return result;
    }

    private static Dictionary<string, WeaponCatalogEntry> LoadWeapons()
    {
        var result = new Dictionary<string, WeaponCatalogEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in ReadTable("weapons.tsv"))
        {
            if (p.Length < 28 || string.IsNullOrWhiteSpace(p[0])) continue;
            var entry = new WeaponCatalogEntry(
                p[0], Int(p[1]), p[2], Int(p[3]), Int(p[4]), NullableInt(p[5]),
                p[6], NullableInt(p[7]), p[8], NullableInt(p[9]), p[10], NullableInt(p[11]),
                Int(p[12]), p[13], Int(p[14]), Int(p[15]) != 0,
                p[16], p[17], p[18], p[19], p[20], p[21], p[22], p[23], p[24], p[25], p[26], p[27]);
            result[NormalizeName(entry.Name)] = entry;
        }

        // Small compatibility aliases for spelling differences in the original mh4edit lists.
        AddAlias(result, "Devistator Blade", "Devastator Blade");
        return result;
    }

    private static Dictionary<string, ArmorCatalogEntry> LoadArmor()
    {
        var result = new Dictionary<string, ArmorCatalogEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in ReadTable("armor.tsv"))
        {
            if (p.Length < 14 || string.IsNullOrWhiteSpace(p[0])) continue;
            var entry = new ArmorCatalogEntry(
                p[0], Int(p[1]), p[2], Int(p[3]), Int(p[4]), NullableInt(p[5]),
                Int(p[6]), Int(p[7]), Int(p[8]), Int(p[9]), Int(p[10]),
                p[11], p[12], Int(p[13]));
            result[NormalizeName(entry.Name)] = entry;
        }
        return result;
    }

    private static void AddAlias<T>(Dictionary<string, T> table, string alias, string canonical)
    {
        if (table.TryGetValue(NormalizeName(canonical), out var entry))
            table[NormalizeName(alias)] = entry;
    }

    private static IEnumerable<string[]> ReadTable(string fileName)
    {
        var path = Path.Combine(CatalogRoot, fileName);
        if (!File.Exists(path)) yield break;
        var first = true;
        foreach (var raw in File.ReadLines(path, Encoding.UTF8))
        {
            if (first) { first = false; continue; }
            if (string.IsNullOrWhiteSpace(raw)) continue;
            yield return SplitTsv(raw);
        }
    }

    private static string[] SplitTsv(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else quoted = !quoted;
            }
            else if (ch == '\t' && !quoted)
            {
                fields.Add(sb.ToString());
                sb.Clear();
            }
            else sb.Append(ch);
        }
        fields.Add(sb.ToString());
        return fields.ToArray();
    }

    private static int Int(string value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

    private static int? NullableInt(string value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static string NormalizeName(string value)
        => value.Trim().Replace("  ", " ", StringComparison.Ordinal).ToUpperInvariant();
}
