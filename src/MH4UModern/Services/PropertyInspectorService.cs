using System.Reflection;
using MH4UModern.ViewModels;
using mh4edit;

namespace MH4UModern.Services;

public sealed class PropertyInspectorService
{
    private readonly UndoManager _undo;
    private readonly DirtyTracker _dirty;

    public PropertyInspectorService(UndoManager undo, DirtyTracker dirty)
    {
        _undo = undo;
        _dirty = dirty;
    }

    public IReadOnlyList<EditablePropertyViewModel> ForEquipment(MonHunEquip equipment, Action afterChange)
    {
        string[] names = equipment switch
        {
            MonHunWeapon => new[] { "Slot1", "Slot1Fixed", "Slot2", "Slot2Fixed", "Slot3", "Slot3Fixed", "KinsectLevel", "ElementType", "ElementValue", "Sharpness", "Modifier", "Upgrade", "Special", "NumSlots", "Polished", "Glow", "Honing", "Rarity", "PolishReq" },
            MonHunArmor => new[] { "Slot1", "Slot1Fixed", "Slot2", "Slot2Fixed", "Slot3", "Slot3Fixed", "Upgrade", "Resistances", "Defense", "NumSlots", "Polished", "Glow", "Rarity", "PolishReq" },
            MonHunTalisman => new[] { "NumSlots", "Slot0", "Slot1", "Slot2", "Skill1ID", "Skill1Amount", "Skill2ID", "Skill2Amount" },
            _ => new[] { "Slot1", "Slot1Fixed", "Slot2", "Slot2Fixed", "Slot3", "Slot3Fixed" }
        };

        return Build(equipment, names, afterChange);
    }

    public IReadOnlyList<EditablePropertyViewModel> ForQuest(GuildQuest quest, Action afterChange)
        => Build(quest, new[] { "Owner", "Id", "InitialLevel", "CurrentLevel", "WeaponBias", "ArmorSeriesBias", "ArmorTypeBias", "MonsterAId", "MonsterBId" }, afterChange);

    public IReadOnlyList<EditablePropertyViewModel> ForPalico(Palico palico, Action afterChange)
        => Build(palico, new[] { "Name", "Exp" }, afterChange);

    private IReadOnlyList<EditablePropertyViewModel> Build(object target, IEnumerable<string> names, Action afterChange)
    {
        var fields = new List<EditablePropertyViewModel>();
        foreach (var name in names)
        {
            var targetType = target.GetType();
            var property = targetType
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.Name == name)
                .OrderByDescending(p => p.DeclaringType == targetType)
                .FirstOrDefault();
            if (property is null || !property.CanRead || !property.CanWrite) continue;
            if (!IsSimple(property.PropertyType)) continue;
            fields.Add(new EditablePropertyViewModel(target, property, Friendly(name, target), _undo, _dirty, afterChange, HelpFor(name)));
        }
        return fields;
    }

    private static bool IsSimple(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return t == typeof(string) || t == typeof(bool) || t.IsPrimitive || t.IsEnum || t == typeof(decimal);
    }

    private static string Friendly(string name, object target)
    {
        if (target is MonHunTalisman)
        {
            return name switch
            {
                "Slot0" => "Decoration Slot 1",
                "Slot1" => "Decoration Slot 2",
                "Slot2" => "Decoration Slot 3",
                _ => FriendlyDefault(name)
            };
        }

        return FriendlyDefault(name);
    }

    private static string FriendlyDefault(string name) => name switch
    {
        "Slot0" => "Decoration Slot 1",
        "Slot1" => "Decoration Slot 1",
        "Slot2" => "Decoration Slot 2",
        "Slot3" => "Decoration Slot 3",
        "Slot1Fixed" => "Slot 1 Fixed",
        "Slot2Fixed" => "Slot 2 Fixed",
        "Slot3Fixed" => "Slot 3 Fixed",
        "KinsectLevel" => "Kinsect Level",
        "ElementType" => "Element Type",
        "ElementValue" => "Element Value",
        "NumSlots" => "Relic Slot Count",
        "PolishReq" => "Polish Requirement",
        "Skill1ID" => "Skill 1 ID",
        "Skill1Amount" => "Skill 1 Amount",
        "Skill2ID" => "Skill 2 ID",
        "Skill2Amount" => "Skill 2 Amount",
        "InitialLevel" => "Initial Level",
        "CurrentLevel" => "Current Level",
        "WeaponBias" => "Weapon Bias",
        "ArmorSeriesBias" => "Armor Series Bias",
        "ArmorTypeBias" => "Armor Type Bias",
        "MonsterAId" => "Monster 1 ID",
        "MonsterBId" => "Monster 2 ID",
        "Exp" => "Experience",
        _ => SplitCamelCase(name)
    };

    private static string? HelpFor(string name) => name switch
    {
        "MonsterAId" or "MonsterBId" => "Raw monster ID. A named selector is planned for the next pass.",
        "ElementType" => "Raw element/status ID from the original editor.",
        "Sharpness" or "Modifier" or "Special" => "Advanced encoded value. Keep a backup before changing this.",
        "Rarity" or "PolishReq" or "Glow" or "Polished" => "Relic-specific field. Using relic fields on a non-relic item may make the game discard it.",
        _ => null
    };

    private static string SplitCamelCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        var chars = new List<char> { value[0] };
        for (var i = 1; i < value.Length; i++)
        {
            if (char.IsUpper(value[i]) && !char.IsUpper(value[i - 1])) chars.Add(' ');
            chars.Add(value[i]);
        }
        return new string(chars.ToArray());
    }
}
