namespace ClassicMhTools.Core.CustomQuests;

public static class QuestPresetPolicy
{
    public static IReadOnlyList<QuestRewardDraft> Rewards(QuestReferenceCatalog catalog, IEnumerable<uint> monsters, byte rank)
    {
        var pools = new List<QuestRewardDraft>[] { [], [], [] };
        foreach (var (monster, index) in monsters.Where(m => m != 0).Distinct().Select((m, i) => (m, i)))
            pools[index % 3].AddRange(catalog.ItemPresets.FirstOrDefault(p => p.Monster == monster && p.Rank == rank)?.Rewards ?? []);
        var output = new List<QuestRewardDraft>();
        for (byte table = 0; table < 3; table++)
        {
            var items = pools[table].GroupBy(r => (r.Item, r.Quantity)).Select(g => (g.Key, Weight: g.Sum(r => (int)r.Chance))).ToArray();
            if (items.Length == 0) continue;
            if (items.Length > 96 - output.Count) throw new InvalidDataException("The combined reward preset exceeds the 96-entry limit.");
            var total = items.Sum(r => r.Weight);
            // Allocate 100 points across all included species, keeping even rare materials selectable.
            var chances = items.Select(r => Math.Max(1, r.Weight * 100 / total)).ToArray();
            while (chances.Sum() < 100) chances[Enumerable.Range(0, items.Length).MaxBy(i => items[i].Weight * 100d / total - chances[i])]++;
            while (chances.Sum() > 100) chances[Enumerable.Range(0, items.Length).Where(i => chances[i] > 1).MaxBy(i => chances[i] - items[i].Weight * 100d / total)]--;
            for (var i = 0; i < items.Length; i++) output.Add(new() { Table = table, Item = items[i].Key.Item, Quantity = items[i].Key.Quantity, Chance = (ushort)chances[i] });
        }
        return output;
    }
}
