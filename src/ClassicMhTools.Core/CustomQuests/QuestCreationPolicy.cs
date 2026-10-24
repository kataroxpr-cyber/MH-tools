namespace ClassicMhTools.Core.CustomQuests;

public static class QuestCreationPolicy
{
    public static bool HasFence(byte map) => map == 11;

    public static ushort AssignEventId(QuestReferenceCatalog catalog, IEnumerable<ushort> reserved, IEnumerable<ushort> installed)
    {
        var local = reserved.ToHashSet();
        var game = installed.ToHashSet();
        var candidates = catalog.UnrestrictedEventIds.Where(id => id is >= 60000 and <= 60999 && !local.Contains(id)).Order().ToArray();
        var unused = candidates.FirstOrDefault(id => !game.Contains(id));
        if (unused != 0) return unused;
        // A populated game can occupy every supported ID; installation still needs explicit replacement consent.
        if (candidates.Length != 0) return candidates[0];
        throw new InvalidOperationException("No supported Event quest IDs remain available. All unrestricted IDs are already used by local quests.");
    }

    public static void ValidateForInstallation(QuestCreationDraft draft, QuestReferenceCatalog catalog)
    {
        draft.Validate();
        if (!catalog.UnrestrictedEventIds.Contains(draft.Id))
            throw new InvalidDataException("This native quest needs an unrestricted Event ID. Save it to assign a supported ID before installing.");
        foreach (var monster in draft.Monsters.Where(m => m.Kind == 0 && m.Wave == 1))
            if (catalog.FirstWaveMaps.TryGetValue(monster.Id, out var map) && map != draft.Map)
                throw new InvalidDataException($"Monster {catalog.MonsterNames.GetValueOrDefault(monster.Id)} needs {catalog.MapNames.GetValueOrDefault(map)} in the first wave because of its opening cutscene. Change the stage or move it to a later wave.");
    }
}
