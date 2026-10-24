using MH4UModern.Models;
using mh4edit;

namespace MH4UModern.Services;

public sealed class SaveSnapshot
{
    private readonly string _name;
    private readonly uint _hr;
    private readonly uint _hrPoints;
    private readonly uint _funds;
    private readonly byte _gender;
    private readonly byte _face;
    private readonly byte _hairStyle;
    private readonly byte _clothingType;
    private readonly byte _voice;
    private readonly byte _eyeColor;
    private readonly byte _featuresType;
    private readonly byte[] _featuresColor;
    private readonly byte[] _hairColor;
    private readonly byte[] _clothingColor;
    private readonly byte[] _skinColor;
    private readonly (ushort Id, ushort Count)[] _items;
    private readonly byte[][] _equipment;
    private readonly byte[][] _quests;
    private readonly byte[][] _palicos;

    private SaveSnapshot(MonHunCharacter character, IReadOnlyList<MonHunEquip>? extraEquipment)
    {
        _name = character.Name;
        _hr = character.HunterRank;
        _hrPoints = character.HunterRankPoints;
        _funds = character.Funds;
        _gender = character.gender;
        _face = character.face;
        _hairStyle = character.hairStyle;
        _clothingType = character.clothingType;
        _voice = character.voice;
        _eyeColor = character.eyeColor;
        _featuresType = character.featuresType;
        _featuresColor = character.featuresColor.ToArray();
        _hairColor = character.hairColor.ToArray();
        _clothingColor = character.clothingColor.ToArray();
        _skinColor = character.skinColor.ToArray();
        _items = character.ItemBox.Select(i => (i.ID, i.Count)).ToArray();
        _equipment = EnumerateEquipment(character, extraEquipment).Select(e => e.Serialize().ToArray()).ToArray();
        _quests = character.GuildQuests.Select(q => q.Serialize().ToArray()).ToArray();
        _palicos = character.Palicos.Select(p => p.Serialize().ToArray()).ToArray();
    }

    public static SaveSnapshot Capture(MonHunCharacter character, IReadOnlyList<MonHunEquip>? extraEquipment = null)
        => new(character, extraEquipment);

    public IReadOnlyList<ChangeRecord> Compare(MonHunCharacter character, IReadOnlyList<MonHunEquip>? extraEquipment = null)
    {
        var changes = new List<ChangeRecord>();

        if (!string.Equals(_name, character.Name, StringComparison.Ordinal))
            changes.Add(new("Character", "Name", _name, character.Name));
        if (_hr != character.HunterRank)
            changes.Add(new("Character", "Hunter Rank", _hr.ToString(), character.HunterRank.ToString()));
        if (_hrPoints != character.HunterRankPoints)
            changes.Add(new("Character", "HR Points", _hrPoints.ToString("N0"), character.HunterRankPoints.ToString("N0")));
        if (_funds != character.Funds)
            changes.Add(new("Character", "Funds", _funds.ToString("N0"), character.Funds.ToString("N0")));
        if (_gender != character.gender)
            changes.Add(new("Character", "Gender", GenderName(_gender), GenderName(character.gender)));
        if (_face != character.face)
            changes.Add(new("Character", "Face", _face.ToString(), character.face.ToString()));
        if (_hairStyle != character.hairStyle)
            changes.Add(new("Character", "Hair Style", _hairStyle.ToString(), character.hairStyle.ToString()));
        if (_clothingType != character.clothingType)
            changes.Add(new("Character", "Clothing Type", _clothingType.ToString(), character.clothingType.ToString()));
        if (_voice != character.voice)
            changes.Add(new("Character", "Voice", _voice.ToString(), character.voice.ToString()));
        if (_eyeColor != character.eyeColor)
            changes.Add(new("Character", "Eye Color", _eyeColor.ToString(), character.eyeColor.ToString()));
        if (_featuresType != character.featuresType)
            changes.Add(new("Character", "Features Type", _featuresType.ToString(), character.featuresType.ToString()));
        AddColorChange(changes, "Features Color", _featuresColor, character.featuresColor);
        AddColorChange(changes, "Hair Color", _hairColor, character.hairColor);
        AddColorChange(changes, "Clothing Color", _clothingColor, character.clothingColor);
        AddColorChange(changes, "Skin Color", _skinColor, character.skinColor);

        for (var i = 0; i < Math.Min(_items.Length, character.ItemBox.Length); i++)
        {
            var before = _items[i];
            var after = character.ItemBox[i];
            if (before.Id == after.ID && before.Count == after.Count) continue;

            var beforeName = NameForItem(before.Id);
            var afterName = NameForItem(after.ID);
            var beforeText = before.Id == 0 ? "Empty" : $"{beforeName} ×{before.Count}";
            var afterText = after.ID == 0 ? "Empty" : $"{afterName} ×{after.Count}";
            changes.Add(new("Items", $"Slot {i + 1}", beforeText, afterText));
        }

        var currentEquipment = EnumerateEquipment(character, extraEquipment).ToArray();
        for (var i = 0; i < Math.Min(_equipment.Length, currentEquipment.Length); i++)
        {
            var current = currentEquipment[i].Serialize();
            if (_equipment[i].SequenceEqual(current)) continue;

            var beforeEquip = MonHunEquip.Create(_equipment[i]);
            var afterEquip = currentEquipment[i];
            var beforeText = SafeEquipText(beforeEquip);
            var afterText = SafeEquipText(afterEquip);
            if (beforeText == afterText) afterText += " (properties changed)";
            changes.Add(new("Equipment", $"Slot {i + 1}", beforeText, afterText));
        }

        for (var i = 0; i < Math.Min(_quests.Length, character.GuildQuests.Length); i++)
        {
            var current = character.GuildQuests[i].Serialize();
            if (!_quests[i].SequenceEqual(current))
                changes.Add(new("Guild Quests", $"Quest {i + 1}", "Original values", "Edited"));
        }

        for (var i = 0; i < Math.Min(_palicos.Length, character.Palicos.Length); i++)
        {
            var current = character.Palicos[i].Serialize();
            if (!_palicos[i].SequenceEqual(current))
                changes.Add(new("Palicos", $"Palico {i + 1}", "Original values", "Edited"));
        }

        return changes;
    }

    private static IEnumerable<MonHunEquip> EnumerateEquipment(MonHunCharacter character, IReadOnlyList<MonHunEquip>? extraEquipment)
    {
        foreach (var equipment in character.EquipBox) yield return equipment;
        if (extraEquipment is null) yield break;
        foreach (var equipment in extraEquipment) yield return equipment;
    }

    private static string SafeEquipText(MonHunEquip equipment)
    {
        if (equipment.Type == 0) return "Empty";
        try { return $"{equipment.TypeString}: {equipment.IDString}"; }
        catch { return $"Type {equipment.Type}: ID {equipment.ID}"; }
    }

    private static string GenderName(byte value) => value switch
    {
        0 => "Male",
        1 => "Female",
        _ => $"Raw {value}"
    };

    private static void AddColorChange(List<ChangeRecord> changes, string label, byte[] before, byte[] after)
    {
        if (before.SequenceEqual(after)) return;
        static string Format(byte[] value) => value.Length >= 3 ? $"RGB {value[0]}, {value[1]}, {value[2]}" : string.Join(", ", value);
        changes.Add(new("Character", label, Format(before), Format(after)));
    }

    private static string NameForItem(ushort id)
    {
        if (id < MonHunItem.Names.Length)
            return MonHunItem.Names[id];
        return $"Item {id}";
    }
}
