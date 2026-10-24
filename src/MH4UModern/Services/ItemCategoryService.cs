namespace MH4UModern.Services;

public static class ItemCategoryService
{
    public static readonly string[] Categories =
    {
        "All categories",
        "Consumables & tools",
        "Ammo & coatings",
        "Nectar",
        "Decorations",
        "Tickets & coins",
        "Palico scraps",
        "Quest & supply",
        "Materials & other"
    };

    public static string GetCategory(ushort id, string name)
    {
        if (id == 0) return "Materials & other";

        if (id is >= 85 and <= 133)
            return "Ammo & coatings";

        if (name.Contains("Nectar", StringComparison.OrdinalIgnoreCase))
            return "Nectar";

        if (name.Contains("Jewel", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Jwl", StringComparison.OrdinalIgnoreCase))
            return "Decorations";

        if (name.Contains("Ticket", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Coin", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Voucher", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Badge", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Pass", StringComparison.OrdinalIgnoreCase))
            return "Tickets & coins";

        if (name.Contains("Scrap", StringComparison.OrdinalIgnoreCase))
            return "Palico scraps";

        if (id is >= 768 and <= 837)
            return "Quest & supply";

        if (id <= 84)
            return "Consumables & tools";

        return "Materials & other";
    }
}
