using System.Globalization;
using System.IO;
using mh4edit;

namespace MH4UModern.Services;

public sealed record EquipmentSortPlan(
    byte[][] State,
    IReadOnlyDictionary<int, int> OldToNew,
    int ArmorSetInstances,
    int ArmorBoxes,
    int WeaponCount,
    int TalismanCount,
    int WeaponStartBox,
    int TalismanStartBox,
    string? Error = null)
{
    public bool Success => string.IsNullOrWhiteSpace(Error);
}

/// <summary>
/// Physical equipment-box layouts. Monster Sets uses the Android catalog's armor order:
/// database rows are naturally stored as Head/Body/Arms/Waist/Legs five-piece runs.
/// Two five-piece sets fit vertically in each 10-row column, matching the in-game grid.
/// </summary>
public static class EquipmentSortService
{
    private sealed record ArmorSetPiece(int SetKey, int SortId, int Rarity, int PartIndex);
    private sealed record Piece(int OldIndex, MonHunEquip Model, byte[] Bytes, string Name, int Rarity, int SortId);
    private sealed record SetInstance(int Rarity, int SortId, Piece?[] Parts);

    private static readonly Lazy<Dictionary<string, ArmorSetPiece>> ArmorSetLookup = new(LoadArmorSetLookup);

    public static EquipmentSortPlan BuildMonsterSets(IReadOnlyList<MonHunEquip> equipment)
    {
        var total = equipment.Count;
        var state = Enumerable.Range(0, total).Select(_ => new byte[28]).ToArray();
        var mapping = new Dictionary<int, int>();

        var armorGroups = new Dictionary<int, List<Piece>[]>();
        var groupMeta = new Dictionary<int, (int Rarity, int SortId)>();
        var weapons = new List<Piece>();
        var talismans = new List<Piece>();
        var other = new List<Piece>();

        for (var i = 0; i < total; i++)
        {
            var model = equipment[i];
            if (model.Type == 0) continue;
            var bytes = model.Serialize().ToArray();
            var name = SafeName(model);
            var rarity = EquipmentCatalogIconService.GetRarity(model);

            if (model.Kind == MonHunEquip.Kinds.Armor)
            {
                var part = PartIndex(model.Type);
                if (part < 0)
                {
                    other.Add(new Piece(i, model, bytes, name, rarity, model.ID));
                    continue;
                }

                ArmorSetPiece? catalog = null;
                ArmorSetLookup.Value.TryGetValue(Normalize(name), out catalog);
                var key = catalog?.SetKey ?? (2_000_000 + model.ID * 10 + part);
                var sortId = catalog?.SortId ?? (2_000_000 + model.ID);
                var setRarity = catalog?.Rarity ?? rarity;
                if (!armorGroups.TryGetValue(key, out var parts))
                {
                    parts = Enumerable.Range(0, 5).Select(_ => new List<Piece>()).ToArray();
                    armorGroups[key] = parts;
                    groupMeta[key] = (setRarity, sortId);
                }
                parts[part].Add(new Piece(i, model, bytes, name, rarity, sortId));
            }
            else if (model.Kind == MonHunEquip.Kinds.Weapon)
            {
                var dbId = GameCatalogService.GetWeapon(name)?.DatabaseId ?? model.ID;
                weapons.Add(new Piece(i, model, bytes, name, rarity, dbId));
            }
            else if (model.Kind == MonHunEquip.Kinds.Talisman)
            {
                talismans.Add(new Piece(i, model, bytes, name, rarity, model.ID));
            }
            else
            {
                other.Add(new Piece(i, model, bytes, name, rarity, model.ID));
            }
        }

        var setInstances = new List<SetInstance>();
        foreach (var pair in armorGroups)
        {
            var parts = pair.Value;
            foreach (var list in parts) list.Sort((a, b) => a.OldIndex.CompareTo(b.OldIndex));
            var copies = parts.Max(list => list.Count);
            var meta = groupMeta[pair.Key];
            for (var copy = 0; copy < copies; copy++)
            {
                var instance = new Piece?[5];
                for (var part = 0; part < 5; part++)
                    if (copy < parts[part].Count) instance[part] = parts[part][copy];
                setInstances.Add(new SetInstance(meta.Rarity, meta.SortId, instance));
            }
        }

        setInstances = setInstances
            .OrderBy(set => set.Rarity <= 0 ? int.MaxValue : set.Rarity)
            .ThenBy(set => set.SortId)
            .ToList();

        var armorBoxes = (int)Math.Ceiling(setInstances.Count / 20d);
        var armorCapacity = armorBoxes * 100;

        weapons = weapons
            .OrderBy(piece => piece.Model.Type)
            .ThenBy(piece => piece.Rarity <= 0 ? int.MaxValue : piece.Rarity)
            .ThenBy(piece => piece.SortId)
            .ThenBy(piece => piece.Model.ID)
            .ThenBy(piece => piece.OldIndex)
            .ToList();

        talismans = talismans
            .OrderBy(piece => piece.Model.ID)
            .ThenBy(piece => piece.OldIndex)
            .ToList();

        other = other.OrderBy(piece => piece.Model.Type).ThenBy(piece => piece.Model.ID).ThenBy(piece => piece.OldIndex).ToList();

        var weaponStart = armorCapacity;
        var afterWeapons = weaponStart + weapons.Count;
        var talismanStart = RoundUpToBox(afterWeapons);
        var afterTalismans = talismanStart + talismans.Count;
        var otherStart = RoundUpToBox(afterTalismans);
        var required = otherStart + other.Count;

        if (required > total)
        {
            return new EquipmentSortPlan(state, mapping, setInstances.Count, armorBoxes, weapons.Count, talismans.Count,
                weaponStart / 100 + 1, talismanStart / 100 + 1,
                $"Monster Sets needs {required} physical slots because incomplete armor sets keep their missing-piece gaps, but this save only has {total} equipment slots. No sorting was applied.");
        }

        // Two vertical five-piece sets per column:
        // set 0 -> 1/11/21/31/41, set 1 -> 51/61/71/81/91, set 2 -> column 2, etc.
        for (var block = 0; block < setInstances.Count; block++)
        {
            var page = block / 20;
            var withinPage = block % 20;
            var column = withinPage / 2;
            var lowerHalf = withinPage % 2;
            for (var part = 0; part < 5; part++)
            {
                var piece = setInstances[block].Parts[part];
                if (piece is null) continue;
                var destination = page * 100 + lowerHalf * 50 + part * 10 + column;
                Place(state, mapping, piece, destination);
            }
        }

        for (var i = 0; i < weapons.Count; i++) Place(state, mapping, weapons[i], weaponStart + i);
        for (var i = 0; i < talismans.Count; i++) Place(state, mapping, talismans[i], talismanStart + i);
        for (var i = 0; i < other.Count; i++) Place(state, mapping, other[i], otherStart + i);

        return new EquipmentSortPlan(state, mapping, setInstances.Count, armorBoxes, weapons.Count, talismans.Count,
            weaponStart / 100 + 1, talismanStart / 100 + 1);
    }

    private static void Place(byte[][] state, Dictionary<int, int> mapping, Piece piece, int destination)
    {
        if (destination < 0 || destination >= state.Length) return;
        state[destination] = piece.Bytes.ToArray();
        mapping[piece.OldIndex] = destination;
    }

    private static int RoundUpToBox(int index)
        => index <= 0 ? 0 : ((index + 99) / 100) * 100;

    private static int PartIndex(int type) => type switch
    {
        5 => 0, // Head
        1 => 1, // Chest
        2 => 2, // Arms
        3 => 3, // Waist
        4 => 4, // Legs
        _ => -1
    };

    private static Dictionary<string, ArmorSetPiece> LoadArmorSetLookup()
    {
        var path = Path.Combine(GameCatalogService.CatalogRoot, "armor.tsv");
        var result = new Dictionary<string, ArmorSetPiece>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return result;

        var rows = new List<(string Name, int Id, string Slot, int Rarity)>();
        var first = true;
        foreach (var raw in File.ReadLines(path))
        {
            if (first) { first = false; continue; }
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var p = raw.Split('\t');
            if (p.Length < 5) continue;
            rows.Add((p[0], Int(p[1]), p[2], Int(p[3])));
        }
        rows.Sort((a, b) => a.Id.CompareTo(b.Id));

        var expected = new[] { "Head", "Body", "Arms", "Waist", "Legs" };
        var setKey = 1;
        for (var i = 0; i < rows.Count;)
        {
            var fullSet = i + 4 < rows.Count
                          && Enumerable.Range(0, 5).All(j => rows[i + j].Id == rows[i].Id + j && string.Equals(rows[i + j].Slot, expected[j], StringComparison.OrdinalIgnoreCase));
            if (fullSet)
            {
                var rarity = rows[i].Rarity;
                var sortId = rows[i].Id;
                for (var part = 0; part < 5; part++)
                    result[Normalize(rows[i + part].Name)] = new ArmorSetPiece(setKey, sortId, rarity, part);
                setKey++;
                i += 5;
                continue;
            }

            // Standalone/event armor: keep it sortable and leave the other four spaces blank.
            var partIndex = Array.FindIndex(expected, slot => string.Equals(slot, rows[i].Slot, StringComparison.OrdinalIgnoreCase));
            if (partIndex >= 0)
            {
                result[Normalize(rows[i].Name)] = new ArmorSetPiece(setKey++, rows[i].Id, rows[i].Rarity, partIndex);
            }
            i++;
        }
        return result;
    }

    private static int Int(string value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

    private static string SafeName(MonHunEquip equipment)
    {
        try { return equipment.IDString; }
        catch { return $"ID {equipment.ID}"; }
    }

    private static string Normalize(string value)
        => value.Trim().Replace("  ", " ", StringComparison.Ordinal).ToUpperInvariant();
}
