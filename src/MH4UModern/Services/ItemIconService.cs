using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MH4UModern.Services;

public static class ItemIconService
{
    // Item IDs are stable for MH4U. Cache by ID so every slot that references the same item
    // reuses one frozen BitmapImage instead of repeatedly decoding the same PNG.
    private static readonly ConcurrentDictionary<ushort, ImageSource> Cache = new();
    private static readonly Lazy<Dictionary<ushort, string>> ExplicitMap = new(LoadMap);

    public static ImageSource? Resolve(ushort id, string name)
    {
        if (id == 0) return null;
        if (Cache.TryGetValue(id, out var cached)) return cached;
        var loaded = Load(id, name);
        if (loaded is not null) Cache.TryAdd(id, loaded);
        return loaded;
    }

    // Optional targeted warm-up. Do not preload the entire 1,400-slot
    // inventory when a save opens; visible icons are loaded lazily and then reused from Cache.
    public static void Prewarm(IEnumerable<(ushort Id, string Name)> items)
    {
        var unique = items
            .Where(x => x.Id != 0)
            .GroupBy(x => x.Id)
            .Select(g => g.First())
            .ToArray();

        _ = Task.Run(() =>
        {
            try
            {
                foreach (var item in unique) Resolve(item.Id, item.Name);
            }
            catch
            {
                // Prewarming is only a performance optimization. Normal lazy loading remains available.
            }
        });
    }

    private static ImageSource? Load(ushort id, string name)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "assets", "items");
        var cleanRoot = Path.Combine(AppContext.BaseDirectory, "assets", "items-clean");
        var candidates = new List<string>();

        // v0.11: the MH4U Database catalog gives us the exact icon family/color for 1,887
        // useful entries in the original mh4edit item list. 1,876 of those are redrawn from
        // the user's separated HD Remix symbols; the exact named APK icon is the fallback.
        var catalogHdRoot = Path.Combine(GameCatalogService.CatalogRoot, "item-icons-hd");
        candidates.Add(Path.Combine(catalogHdRoot, $"{id}.png"));

        var catalog = GameCatalogService.GetItem(id);
        if (catalog is not null && !string.IsNullOrWhiteSpace(catalog.IconName))
            candidates.Add(Path.Combine(GameCatalogService.ItemIconRoot, catalog.IconName));

        // Keep the older v0.10 generated assets behind the database-backed mapping.
        candidates.Add(Path.Combine(cleanRoot, $"{id}.png"));
        if (ExplicitMap.Value.TryGetValue(id, out var mapped) && !string.IsNullOrWhiteSpace(mapped))
            candidates.Add(Path.Combine(root, mapped.Replace('/', Path.DirectorySeparatorChar)));

        candidates.Add(Path.Combine(root, $"{id}.png"));
        candidates.Add(Path.Combine(root, $"{id:D4}.png"));
        candidates.Add(Path.Combine(root, $"{id:D5}.png"));
        candidates.Add(Path.Combine(root, Sanitize(name) + ".png"));

        foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var image = LoadImage(path);
            if (image is not null) return image;
        }

        var fallback = Path.Combine(
            AppContext.BaseDirectory,
            "assets", "ui", "item-fallbacks",
            GetFallbackFile(id, name));
        return LoadImage(fallback);
    }

    private static string GetFallbackFile(ushort id, string name)
    {
        var n = name ?? string.Empty;

        if (ContainsAny(n, "Fish", "Tuna", "Arowana", "Wanchovy", "Speartuna"))
            return "fish.png";
        if (ContainsAny(n, "Bug", "Beetle", "Cricket", "Hopper", "Hornfly", "Butterfly", "Kumori", "Larva"))
            return "bugs.png";
        if (ContainsAny(n, "Pickaxe", "Bug Net"))
            return "gathering.png";

        return ItemCategoryService.GetCategory(id, n) switch
        {
            "Consumables & tools" => "consumables.png",
            "Ammo & coatings" => "ammo-coatings.png",
            "Nectar" => "nectar.png",
            "Decorations" => "decorations.png",
            "Tickets & coins" => "tickets-coins.png",
            "Palico scraps" => "palico-scraps.png",
            "Quest & supply" => "quest-supply.png",
            "Materials & other" => "materials-other.png",
            _ => "unknown.png"
        };
    }

    private static bool ContainsAny(string value, params string[] needles)
        => needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static ImageSource? LoadImage(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            // Keep only enough decoded pixels for the compact slot footprint.
            image.DecodePixelWidth = 32;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<ushort, string> LoadMap()
    {
        var map = new Dictionary<ushort, string>();
        var path = Path.Combine(AppContext.BaseDirectory, "assets", "items", "item-icons.tsv");
        if (!File.Exists(path)) return map;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var parts = line.Split('\t', 2);
            if (parts.Length == 2 && ushort.TryParse(parts[0], out var id)) map[id] = parts[1].Trim();
        }
        return map;
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value) sb.Append(invalid.Contains(ch) ? '_' : ch);
        return sb.ToString().Trim();
    }
}
