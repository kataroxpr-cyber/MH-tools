using System.Reflection;
using mh4edit;

namespace MH4UModern.Services;

public sealed record EquippedIndexState(ushort Weapon, ushort Chest, ushort Arms, ushort Waist, ushort Legs, ushort Head, ushort Talisman)
{
    public IEnumerable<ushort> All
    {
        get
        {
            yield return Weapon;
            yield return Chest;
            yield return Arms;
            yield return Waist;
            yield return Legs;
            yield return Head;
            yield return Talisman;
        }
    }
}

public static class EquipmentReferenceService
{
    private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Dictionary<string, FieldInfo> Fields = new(StringComparer.Ordinal)
    {
        ["Weapon"] = Require("idxEqpWeapon"),
        ["Chest"] = Require("idxEqpChest"),
        ["Arms"] = Require("idxEqpArms"),
        ["Waist"] = Require("idxEqpWaist"),
        ["Legs"] = Require("idxEqpLegs"),
        ["Head"] = Require("idxEqpHead"),
        ["Talisman"] = Require("idxEqpTalisman")
    };

    public static EquippedIndexState Capture(MonHunCharacter character)
        => new(Read(character, "Weapon"), Read(character, "Chest"), Read(character, "Arms"), Read(character, "Waist"), Read(character, "Legs"), Read(character, "Head"), Read(character, "Talisman"));

    public static void Apply(MonHunCharacter character, EquippedIndexState state)
    {
        Write(character, "Weapon", state.Weapon);
        Write(character, "Chest", state.Chest);
        Write(character, "Arms", state.Arms);
        Write(character, "Waist", state.Waist);
        Write(character, "Legs", state.Legs);
        Write(character, "Head", state.Head);
        Write(character, "Talisman", state.Talisman);
    }

    public static EquippedIndexState Remap(EquippedIndexState state, IReadOnlyDictionary<int, int> oldToNew)
        => new(Map(state.Weapon, oldToNew), Map(state.Chest, oldToNew), Map(state.Arms, oldToNew), Map(state.Waist, oldToNew), Map(state.Legs, oldToNew), Map(state.Head, oldToNew), Map(state.Talisman, oldToNew));

    public static bool IsEquipped(EquippedIndexState state, int index)
        => state.All.Any(value => value != ushort.MaxValue && value == index);

    private static ushort Map(ushort value, IReadOnlyDictionary<int, int> oldToNew)
    {
        if (value == ushort.MaxValue) return value;
        return oldToNew.TryGetValue(value, out var mapped) && mapped is >= 0 and <= ushort.MaxValue
            ? (ushort)mapped
            : value;
    }

    private static FieldInfo Require(string name)
        => typeof(MonHunCharacter).GetField(name, Flags)
           ?? throw new MissingFieldException(typeof(MonHunCharacter).FullName, name);

    private static ushort Read(MonHunCharacter character, string key)
        => Fields[key].GetValue(character) is ushort value ? value : ushort.MaxValue;

    private static void Write(MonHunCharacter character, string key, ushort value)
        => Fields[key].SetValue(character, value);
}
