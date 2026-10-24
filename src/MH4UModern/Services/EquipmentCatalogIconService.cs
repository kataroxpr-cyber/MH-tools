using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using mh4edit;

namespace MH4UModern.Services;

public static class EquipmentCatalogIconService
{
    private static readonly ConcurrentDictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static int GetRarity(MonHunEquip equip)
    {
        if (equip.Type == 0) return 0;
        var name = SafeName(equip);

        if (equip is MonHunWeapon weapon)
        {
            if (weapon.IsRelic && weapon.Rarity is < 10) return weapon.Rarity + 1;
            return GameCatalogService.GetWeapon(name)?.Rarity ?? 0;
        }

        if (equip is MonHunArmor armor)
        {
            var catalog = GameCatalogService.GetArmor(name);
            if (catalog is not null) return catalog.Rarity;
            if (armor.Rarity is < 10) return armor.Rarity + 1;
        }

        return 0;
    }

    public static ImageSource? Resolve(MonHunEquip equip)
    {
        var rarity = GetRarity(equip);
        if (rarity is < 1 or > 10) return null;

        var relative = equip.Type switch
        {
            1 => $"armors/body/body{rarity}.png",
            2 => $"armors/arms/arms{rarity}.png",
            3 => $"armors/waist/waist{rarity}.png",
            4 => $"armors/legs/legs{rarity}.png",
            5 => $"armors/head/head{rarity}.png",
            7 => $"weapons/great_sword/great_sword{rarity}.png",
            8 => $"weapons/sword_and_shield/sword_and_shield{rarity}.png",
            9 => $"weapons/hammer/hammer{rarity}.png",
            10 => $"weapons/lance/lance{rarity}.png",
            11 => $"weapons/light_bowgun/light_bowgun{rarity}.png",
            12 => $"weapons/heavy_bowgun/heavy_bowgun{rarity}.png",
            13 => $"weapons/long_sword/long_sword{rarity}.png",
            14 => $"weapons/switch_axe/switch_axe{rarity}.png",
            15 => $"weapons/gunlance/gunlance{rarity}.png",
            16 => $"weapons/bow/bow{rarity}.png",
            17 => $"weapons/dual_blades/dual_blades{rarity}.png",
            18 => $"weapons/hunting_horn/hunting_horn{rarity}.png",
            19 => $"weapons/insect_glaive/insect_glaive{rarity}.png",
            20 => $"weapons/charge_blade/charge_blade{rarity}.png",
            _ => null
        };
        if (relative is null) return null;

        if (Cache.TryGetValue(relative, out var cached)) return cached;
        var path = Path.Combine(AppContext.BaseDirectory, "images", relative.Replace('/', Path.DirectorySeparatorChar));
        var loaded = LoadImage(path);
        if (loaded is not null) Cache.TryAdd(relative, loaded);
        return loaded;
    }

    private static string SafeName(MonHunEquip equip)
    {
        try { return equip.IDString; }
        catch { return string.Empty; }
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
}
