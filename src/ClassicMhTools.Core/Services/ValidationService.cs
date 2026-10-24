using mh4edit;

namespace MH4UModern.Services;

public sealed class ValidationService
{
    public IReadOnlyList<string> Validate(MonHunCharacter character)
    {
        var warnings = new List<string>();

        if (character.ItemBox.Length != 1400)
            warnings.Add($"Item box has {character.ItemBox.Length} entries; expected 1400.");
        if (character.EquipBox.Length != 1400)
            warnings.Add($"Equipment box has {character.EquipBox.Length} entries; expected 1400.");
        if (character.GuildQuests.Length != 10)
            warnings.Add($"Guild quest list has {character.GuildQuests.Length} entries; expected 10.");
        if (character.Palicos.Length != 56)
            warnings.Add($"Palico list has {character.Palicos.Length} entries; expected 56.");

        if (character.gender > 1)
            warnings.Add($"Character gender byte is {character.gender}; expected 0 (Male) or 1 (Female).");

        for (var i = 0; i < character.ItemBox.Length; i++)
        {
            var item = character.ItemBox[i];
            if (item.Count > 99)
                warnings.Add($"Item slot {i + 1} has a count above 99.");
            if (item.ID >= MonHunItem.Names.Length && item.ID != 0)
                warnings.Add($"Item slot {i + 1} uses unknown item ID {item.ID}.");
        }

        for (var i = 0; i < character.EquipBox.Length; i++)
        {
            var equip = character.EquipBox[i];
            if (equip.Type is < 0 or > 20)
                warnings.Add($"Equipment slot {i + 1} uses unknown equipment type {equip.Type}.");
        }

        for (var i = 0; i < character.GuildQuests.Length; i++)
        {
            var quest = character.GuildQuests[i];
            if (quest.InitialLevel > 140 || quest.CurrentLevel > 140)
                warnings.Add($"Guild quest {i + 1} has a level above 140.");
            if (quest.MonsterAId >= GuildQuestStatic.allMonsters.Length)
                warnings.Add($"Guild quest {i + 1} uses unknown Monster 1 ID {quest.MonsterAId}.");
            if (quest.MonsterBId >= GuildQuestStatic.allMonsters.Length)
                warnings.Add($"Guild quest {i + 1} uses unknown Monster 2 ID {quest.MonsterBId}.");
        }

        for (var i = 0; i < character.Palicos.Length; i++)
        {
            var palicoName = character.Palicos[i].Name ?? string.Empty;
            if (palicoName.Length > 12)
                warnings.Add($"Palico {i + 1} name is longer than 12 characters and will be truncated.");
        }

        return warnings;
    }
}
