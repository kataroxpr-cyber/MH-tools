using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using mh4edit;

namespace MH4UModern.Services;

public static class ArmorRarityService
{
    private static readonly Lazy<Dictionary<string, int>> Catalog = new(LoadCatalog);
    private static readonly ConcurrentDictionary<string, ImageSource> Images = new(StringComparer.OrdinalIgnoreCase);

    public static int GetDisplayRarity(MonHunEquip equip)
    {
        if (equip is not MonHunArmor armor) return 0;
        var name = SafeName(equip);

        // v0.11: prefer the exact normal-armor rarity from the MH4U Database catalog.
        // This avoids interpreting unrelated bytes in crafted armor as a catalog rarity.
        var database = GameCatalogService.GetArmor(name);
        if (database is not null && database.Rarity is >= 1 and <= 10) return database.Rarity;

        // Keep the older local catalog and stored-byte fallbacks for relic/event edge cases.
        if (Catalog.Value.TryGetValue(Normalize(name), out var rarity)) return rarity;
        if (armor.Rarity is > 0 and < 10) return armor.Rarity + 1;
        return InferRarity(name);
    }

    public static ImageSource? ResolveArmorImage(MonHunEquip equip)
    {
        if (equip is not MonHunArmor) return null;
        var rarity = GetDisplayRarity(equip);
        if (rarity is < 1 or > 10) return null;
        var folder = equip.Type switch
        {
            1 => "body",
            2 => "arms",
            3 => "waist",
            4 => "legs",
            5 => "head",
            _ => null
        };
        if (folder is null) return null;
        var key = $"{folder}:{rarity}";
        if (Images.TryGetValue(key, out var cached)) return cached;
        var loaded = LoadImage(Path.Combine(AppContext.BaseDirectory, "images", "armors", folder, $"{folder}{rarity}.png"));
        if (loaded is not null) Images.TryAdd(key, loaded);
        return loaded;
    }


    // Offline fallback for the normal armor catalog. The optional TSV always wins when present.
    // MH4U's armor lists are heavily grouped by rank/set, so this covers the standard catalog
    // while keeping unknown/event edge cases neutral instead of inventing a color.
    private static int InferRarity(string rawName)
    {
        var name = Normalize(rawName);
        if (name.Length == 0 || name == "(NONE)") return 0;

        string[] r1 = { "LEATHER", "DERRING", "CHAINMAIL", "HUNTER'S", "BONE", "ALLOY", "LOC LAC", "LIGHT BELT", "IRON BELT" };
        string[] r2 = { "BNAHABRA", "SLAGTOTH", "RHENOPLOS", "REMOBRA", "JAGGI", "GENPREY", "IOPREY", "LAGOMBI", "KECHA", "CONGA", "GYPCEROS", "TETSUCABRA", "NERSCYLLA", "ZAMTRIOS", "NAJARALA", "KHEZU", "RATHIAN", "VELOCIPREY", "KUT-KU", "BASARIOS", "ARZUROS", "LUDROTH", "BARROTH", "NIBELSNARF", "CEANATAUR", "HIGH METAL", "BATTLE" };
        string[] r3 = { "GORE", "RATHALOS", "TIGREX", "ZINOGRE", "SELTAS", "GRAVIOS", "BRACHYDIOS", "GLOAMING", "DAYSPRING", "ADAMANCE", "EPOCH", "ARC", "STORGE", "GARUGA", "KIRIN", "NARGACUGA", "URAGAAN", "DURAMBOROS", "LAVA", "DIABLOS", "AUROROS", "GENESIS", "BOREALIS", "GLYPH", "ARTIAN", "INGOT" };

        string[] r5S = { "GYPCEROS", "TETSUCABRA", "NERSCYLLA", "ZAMTRIOS", "NAJARALA", "KHEZU", "RATHIAN", "RATHALOS", "TIGREX", "ZINOGRE", "SELTAS", "GRAVIOS", "BASARIOS", "GARUGA", "MONO", "BLANGO", "GOBUL", "BARIOTH", "HIGH METAL", "ARTIAN", "CHAKRA" };
        string[] r6 = { "GORE", "REGIOS", "RATH HEART", "RATH SOUL", "BRACHYDIOS", "VANGIS", "KIRIN", "LAGIACRUS", "AGNAKTOR", "DOBER", "HAWK", "LECTURER", "GUILD KNIGHT", "MAIDEN", "YUKUMO", "RANGER", "ARCHER", "BLACK BELT", "SAILOR" };
        string[] r7 = { "ARC", "STORGE", "GOLDEN", "PUPPETEER", "DIVINE IRE", "CRUSHER", "KUSHALA", "KAISER", "GLOAMING", "DAYSPRING", "ADAMANCE", "EPOCH", "REBELLION", "RIOT", "AKANTOR", "SILVER SOL", "GOLDEN LUNE", "ESURIENT", "EXCELLO", "DRAGON", "FATALIS", "UKANLOS", "INDRA", "STEADFAST", "YAKSHA", "CARNAGE", "EMPRESS", "MIZUHA", "CHAM", "HELIOS", "SELENE", "ESCADORA", "EX ", "YAMATO", "MUTSU", "HYUGA", "AMAGI", "SHINOBI", "BLUE STAR", "CAPTAIN", "LEGEND", "BOUYA", "UNIQLO", "STAR ROOK", "ZODIAS", "HERO'S", "OGRE'S", "PATISSIER" };

        string[] g9 = { "REGIOS", "RATHALOS", "RATH SOUL", "TIGREX", "ZINOGRE", "SELTAS", "GRAVIOS", "DIABLOS", "BRACHYDIOS", "VANGIS", "CHAOS", "NEPHILIM", "GARUGA", "MONO", "MONODEVIL", "GORE", "NARGACUGA", "LAGIACRUS", "DURAMBOROS", "LAVA", "ARTIAN", "ACE", "SORORAL", "G. KNIGHT", "MAIDEN'S", "EXCLUSIVE" };
        string[] g10 = { "ARC", "STORGE", "GRAND ", "KUSHALA", "KUJULA", "KAISER", "GOGMA", "REBELLION", "RIOT", "AKANTOR", "UKANLOS", "ESURIENT", "EXCELLO", "BRACHYDIUM", "WROTH", "KIRIN", "YAKSHA", "CARNAGE", "YAMATO", "MUTSU", "HYUGA", "AMAGI", "EMPRESS", "HELIOS", "SELENE", "EX AUROROS", "EX GENESIS", "EX BOREALIS", "EX GLYPH", "TEMPEST", "WELKIN", "MIRALIS", "EX ESCADORA", "STAR KNIGHT", "PIRATE KING", "CHAMPION", "STRONGMAN", "VARIA", "ZERO SUIT", "GOLDEN LUNE", "SILVER SOL", "DRAGON", "WHITE FATALIS", "GX ", "MAIL OF ", "VEST OF ", "GREAVES OF ", "LEGGINGS OF " };

        bool StartsAny(string[] values) => values.Any(v => name.StartsWith(v, StringComparison.Ordinal));
        var hasXorZ = name.EndsWith(" X", StringComparison.Ordinal) || name.EndsWith(" Z", StringComparison.Ordinal)
                       || name.Contains(" X ", StringComparison.Ordinal) || name.Contains(" Z ", StringComparison.Ordinal);
        if (hasXorZ)
        {
            if (StartsAny(g10)) return 10;
            if (StartsAny(g9)) return 9;
            return 8;
        }

        var hasSorU = name.EndsWith(" S", StringComparison.Ordinal) || name.EndsWith(" U", StringComparison.Ordinal)
                      || name.Contains(" S ", StringComparison.Ordinal) || name.Contains(" U ", StringComparison.Ordinal);
        if (hasSorU)
        {
            if (StartsAny(r7)) return 7;
            if (StartsAny(r6)) return 6;
            if (StartsAny(r5S)) return 5;
            return 4;
        }

        if (StartsAny(r7)) return 7;
        if (StartsAny(r6)) return 6;
        if (StartsAny(r3)) return 3;
        if (StartsAny(r2)) return 2;
        if (StartsAny(r1)) return 1;
        return 0;
    }

    private static ImageSource? LoadImage(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch { return null; }
    }

    private static Dictionary<string, int> LoadCatalog()
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var path = Path.Combine(AppContext.BaseDirectory, "assets", "metadata", "armor-rarity.tsv");
        if (!File.Exists(path)) return result;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var parts = line.Split('\t', 2);
            if (parts.Length != 2 || !int.TryParse(parts[1], out var rarity) || rarity is < 1 or > 10) continue;
            result[Normalize(parts[0])] = rarity;
        }
        return result;
    }

    private static string SafeName(MonHunEquip equip)
    {
        try { return equip.IDString; }
        catch { return string.Empty; }
    }

    private static string Normalize(string value)
        => value.Trim().Replace("  ", " ", StringComparison.Ordinal).ToUpperInvariant();
}
