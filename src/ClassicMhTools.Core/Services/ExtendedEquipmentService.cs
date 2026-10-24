using System.IO;
using mh4edit;

namespace MH4UModern.Services;

/// <summary>
/// mh4edit 1.0.2 parses 1,400 equipment slots, but the MH4U user save actually
/// reserves 1,500 slots (15 boxes of 100). Keep the upstream core DLL untouched
/// and bridge the final 100 slots through the core's decrypted MemoryStream.
/// </summary>
public static class ExtendedEquipmentService
{
    public const int LegacySlotCount = 1400;
    public const int TotalSlotCount = 1500;
    public const int ExtraSlotCount = TotalSlotCount - LegacySlotCount;
    public const int EntrySize = 28;
    public const int EquipmentOffset = 0x173E;
    public const int ExtraOffset = EquipmentOffset + (LegacySlotCount * EntrySize); // 0xB05E

    public static MonHunEquip[] ReadTail(MonHunSave save)
    {
        var result = new MonHunEquip[ExtraSlotCount];
        var stream = save.ms ?? throw new InvalidOperationException("The decrypted save stream is unavailable.");
        var oldPosition = stream.Position;
        try
        {
            stream.Seek(ExtraOffset, SeekOrigin.Begin);
            var buffer = new byte[EntrySize];
            for (var i = 0; i < result.Length; i++)
            {
                Array.Clear(buffer, 0, buffer.Length);
                var read = 0;
                while (read < buffer.Length)
                {
                    var n = stream.Read(buffer, read, buffer.Length - read);
                    if (n <= 0) break;
                    read += n;
                }

                result[i] = MonHunEquip.Create(buffer.ToArray());
            }
        }
        finally
        {
            stream.Position = oldPosition;
        }

        return result;
    }


    /// <summary>
    /// Saves through the untouched mh4edit core while temporarily exposing all 1,500
    /// equipment slots to MonHunCharacter.SerializeCharacterEquip().  This matters when
    /// the currently equipped weapon/armor/talisman lives in Box 15: the upstream core
    /// keeps the equipped-slot indices, but its public EquipBox array only contains the
    /// first 1,400 slots, so direct Save() would throw IndexOutOfRangeException.
    /// </summary>
    public static void SaveWithTail(MonHunSave save, MonHunCharacter character, IReadOnlyList<MonHunEquip> tail)
    {
        if (tail.Count != ExtraSlotCount)
            throw new InvalidOperationException($"Expected {ExtraSlotCount} extra equipment slots, got {tail.Count}.");
        if (character.EquipBox.Length < LegacySlotCount)
            throw new InvalidOperationException($"Equipment box has only {character.EquipBox.Length} legacy slots; expected at least {LegacySlotCount}.");

        // Preserve the exact legacy array object because the UI view-models reference the
        // existing MonHunEquip instances.  Only the array container is swapped temporarily.
        var legacyBox = character.EquipBox;
        var combined = new MonHunEquip[TotalSlotCount];
        Array.Copy(legacyBox, 0, combined, 0, LegacySlotCount);
        for (var i = 0; i < ExtraSlotCount; i++)
            combined[LegacySlotCount + i] = tail[i];

        try
        {
            // Keep the real Box 15 bytes in the decrypted stream.  The upstream Save()
            // serializes only slots 0..1399 for the main equipment block, so this tail
            // region is intentionally bridged separately.
            WriteTail(save, tail);

            // SerializeCharacterEquip() can now safely resolve equipped indices 1400..1499.
            character.EquipBox = combined;
            save.Save();
        }
        finally
        {
            // Restore the 1,400-slot model expected everywhere else in the modern UI.
            character.EquipBox = legacyBox;
        }
    }
    public static void WriteTail(MonHunSave save, IReadOnlyList<MonHunEquip> tail)
    {
        if (tail.Count != ExtraSlotCount)
            throw new InvalidOperationException($"Expected {ExtraSlotCount} extra equipment slots, got {tail.Count}.");

        var stream = save.ms ?? throw new InvalidOperationException("The decrypted save stream is unavailable.");
        var oldPosition = stream.Position;
        try
        {
            stream.Seek(ExtraOffset, SeekOrigin.Begin);
            for (var i = 0; i < tail.Count; i++)
            {
                var bytes = tail[i].Serialize();
                if (bytes.Length != EntrySize)
                    throw new InvalidOperationException($"Equipment slot {LegacySlotCount + i + 1} serialized to {bytes.Length} bytes instead of {EntrySize}.");
                stream.Write(bytes, 0, bytes.Length);
            }
        }
        finally
        {
            stream.Position = oldPosition;
        }
    }
}
