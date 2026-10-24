using mh4edit;

namespace MH4UModern.Services;

/// <summary>
/// Repairs the upstream mh4edit waist-name table before MonHunEquip builds its
/// static equipment lists. The original table begins at save ID 12, so every
/// waist ID was displayed 11 entries too far forward.
///
/// This only fixes metadata/name lookup. It does not rewrite equipment IDs in
/// the save file.
/// </summary>
public static class WaistArmorIdFixService
{
    private static bool _applied;

    private static readonly string[] MissingPrefix =
    {
        "Derring Belt",          // save ID 1
        "Derring Faulds S",      // save ID 2
        "Derring Coat S",        // save ID 3
        "Leather Belt",          // save ID 4
        "Leather Faulds S",      // save ID 5
        "Leather Coat S",        // save ID 6
        "Chainmail Belt",        // save ID 7
        "Chainmail Faulds S",    // save ID 8
        "Chainmail Coat S",      // save ID 9
        "Hunter's Faulds",       // save ID 10
        "Hunter's Coat",         // save ID 11
    };

    public static void Apply()
    {
        if (_applied) return;

        var original = MonHunEquipStatic.allEqpWaist;
        if (original.Length == 0 || original[0] != "(None)")
            throw new InvalidOperationException("Unexpected mh4edit waist table layout.");

        // Idempotence guard for future core updates / repeated calls.
        if (original.Length > 12 && original[1] == "Derring Belt" && original[12] == "Hunter's Faulds S")
        {
            _applied = true;
            return;
        }

        // The known-bad upstream table starts with Hunter's Faulds S at index 1.
        if (original.Length <= 2 || original[1] != "Hunter's Faulds S" || original[2] != "Hunter's Coat S")
            throw new InvalidOperationException("mh4edit waist table no longer matches the expected 1.0.2 layout; refusing to apply an unsafe ID correction.");

        var corrected = new string[original.Length + MissingPrefix.Length];
        corrected[0] = original[0];
        Array.Copy(MissingPrefix, 0, corrected, 1, MissingPrefix.Length);
        Array.Copy(original, 1, corrected, 1 + MissingPrefix.Length, original.Length - 1);
        MonHunEquipStatic.allEqpWaist = corrected;

        // Sanity checks against known set-aligned IDs from real saves.
        if (corrected[51] != "Golden Lunecoil" ||
            corrected[59] != "Rathalos Faulds" ||
            corrected[141] != "Kecha Faulds" ||
            corrected[151] != "Tetsucabra Faulds")
        {
            throw new InvalidOperationException("Waist ID correction failed its regression checks.");
        }

        _applied = true;
    }
}
