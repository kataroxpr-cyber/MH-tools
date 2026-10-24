using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClassicMhTools.Core.CustomQuests;

// Two quest-specific gates, not a generic NPC spawner or a progression editor.
public static class StoryHelperExperiment
{
    public const string TitleId = "0004000000126300";
    public const int CodeLength = 16261120;
    public const string CodeSha256 = "a6d5278c851b6a6695d28626ecb1b031cbaf59e7f92d0656ba0b5b509ecdfe3f";
    private const int CodeBase = 0x100000;
    private const int Trampoline = 0xDEC740;
    private static readonly int[] Gates = [0x69D2A4, 0x6B32A8];

    public static QuestCreationDraft CreateRathian(ushort id, QuestReferenceCatalog catalog)
    {
        var draft = new QuestCreationDraft
        {
            Id = id, Map = 19, Faints = 3, ExperimentalStoryHelpers = true,
            // Original story monster's area/position. Rathian compatibility still needs a game test.
            Monsters = [new() { Id = 1, Picture = 1, Area = 3, X = 1133, Y = 0, Z = -2519 }],
            Supplies = (catalog.ItemPresets.FirstOrDefault(p => p.Rank == 1 && p.Monster == 0)?.Supplies ?? [])
                .Select(s => new QuestSupplyDraft { Box = s.Box, Item = s.Item, Quantity = s.Quantity }).ToList(),
            Rewards = QuestPresetPolicy.Rewards(catalog, [1], 1).ToList(),
            Languages = Enumerable.Range(0, 5).Select(_ => new QuestLanguageDraft
            {
                Title = "Battlequarters: Rathian", MainGoal = "Hunt a Rathian", Other = "Rathian",
                Description = "A Rathian has entered the Battlequarters.", Client = "Guild"
            }).ToArray()
        };
        QuestCreationPolicy.ValidateForInstallation(draft, catalog);
        return draft;
    }

    public static byte[] BuildBundle(QuestCreationDraft draft, byte[] code, QuestReferenceCatalog catalog)
    {
        QuestCreationPolicy.ValidateForInstallation(draft, catalog);
        if (!draft.ExperimentalStoryHelpers) throw new InvalidDataException("Enable the experimental Ace Hunters patch intent first.");
        var patched = PatchCode(code, draft.Id);
        var patch = BuildBps(code, patched);
        var mib = draft.Build().ToArray();
        var manifest = JsonSerializer.Serialize(new
        {
            FormatVersion = 1, Experiment = "Ace Hunters / Battlequarters", Status = "IN-GAME UNVERIFIED",
            TitleId, QuestId = draft.Id, MapId = draft.Map, Area = 3, NpcTypes = new[] { 8, 9 },
            SourceCodeSha256 = CodeSha256, SourceCodeLength = CodeLength,
            TargetCodeSha256 = Digest(patched), PatchSha256 = Digest(patch), QuestSha256 = Digest(mib),
            Hooks = Gates.Select(g => $"0x{g:X8}").ToArray(), Trampoline = $"0x{Trampoline:X8}",
            SaveEdits = false, MibHelperBit = false, AutomaticallyInstalled = false
        }, new JsonSerializerOptions { WriteIndented = true });
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            Add(zip, $"quest/m{draft.Id}.mhquest", Encoding.UTF8.GetBytes(draft.ToJson()));
            Add(zip, $"quest/m{draft.Id}.mib", mib);
            Add(zip, $"mods/{TitleId}/exefs/code.bps", patch);
            Add(zip, "manifest.json", Encoding.UTF8.GetBytes(manifest));
            Add(zip, "README.txt", Encoding.UTF8.GetBytes(Readme(draft.Id)));
        }
        return stream.ToArray();
    }

    public static byte[] PatchCode(byte[] code, ushort questId)
    {
        if (code.Length != CodeLength || !Digest(code).Equals(CodeSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unsupported code.bin. Select the decompressed executable from the inspected USA update. Base-game, compressed, modified and other-region executables are rejected. No output was written.");
        return PatchVerifiedLayout(code, questId);
    }

    internal static byte[] PatchVerifiedLayout(byte[] code, ushort questId)
    {
        if (questId is < 60000 or > 60999) throw new InvalidDataException("The helper experiment must target a custom Event ID, never story quest 1002.");
        if (code.Length != CodeLength) throw new InvalidDataException("Unexpected executable length.");
        for (var i = 0; i < Gates.Length; i++)
        {
            var at = Gates[i] - CodeBase;
            if (Read(code, at) != 0xE2401C03 || Read(code, at + 4) != 0xE25110EA ||
                Read(code, at + 8) != (i == 0 ? 0x03570003u : 0x03550003u) ||
                Read(code, at + 12) != (i == 0 ? 0x0A000002u : 0x1A00001Bu))
                throw new InvalidDataException("The helper gate instructions do not match the inspected update.");
        }
        if (code.AsSpan(Trampoline - CodeBase, 160).ContainsAnyExcept((byte)0))
            throw new InvalidDataException("The executable padding is occupied; patches cannot be combined.");
        var target = (byte[])code.Clone();
        for (var i = 0; i < Gates.Length; i++)
        {
            var site = Gates[i];
            var block = Trampoline + i * 80;
            // Preserve scratch registers and reproduce both displaced arithmetic instructions
            // on every original path. The game's own area-3 compare remains at the return site.
            uint[] words = [
                0xE92D000C, 0xE59F203C, 0xE1500002, 0x1A000009,
                0xE59F2034, 0xE5922000, 0xE5922C8C, 0xE20220FF,
                0xE3520013, 0x1A000003, 0xE3A01000, 0xE3510000,
                0xE8BD000C, Branch(block + 13 * 4, site + 8),
                0xE2401C03, 0xE25110EA, 0xE8BD000C, Branch(block + 17 * 4, site + 8),
                questId, 0x00FB6B7C
            ];
            Write(target, site - CodeBase, Branch(site, block));
            for (var w = 0; w < words.Length; w++) Write(target, block - CodeBase + w * 4, words[w]);
        }
        return target;
    }

    private static uint Branch(int from, int to)
    {
        var displacement = to - from - 8;
        if ((displacement & 3) != 0 || displacement is < -0x2000000 or >= 0x2000000)
            throw new InvalidDataException("ARM branch target is outside range.");
        return 0xEA000000u | ((uint)(displacement >> 2) & 0xFFFFFF);
    }

    // BPS SourceRead/TargetRead only. Azahar checks the source CRC before applying it.
    // Zero metadata is required by its BPS reader. Only changed bytes enter the patch.
    internal static byte[] BuildBps(byte[] source, byte[] target)
    {
        if (source.Length != target.Length) throw new ArgumentException("Expected equal-length code images.");
        using var stream = new MemoryStream();
        stream.Write("BPS1"u8);
        Variable(stream, (ulong)source.Length); Variable(stream, (ulong)target.Length); Variable(stream, 0);
        for (var at = 0; at < source.Length;)
        {
            var start = at;
            var same = source[at] == target[at];
            while (at < source.Length && (source[at] == target[at]) == same) at++;
            Variable(stream, ((ulong)(at - start - 1) << 2) | (same ? 0ul : 1ul));
            if (!same) stream.Write(target.AsSpan(start, at - start));
        }
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(Crc32.HashToUInt32(source)); writer.Write(Crc32.HashToUInt32(target));
        writer.Write(Crc32.HashToUInt32(stream.GetBuffer().AsSpan(0, (int)stream.Length)));
        return stream.ToArray();
    }

    private static void Variable(Stream stream, ulong value)
    {
        while (true)
        {
            var part = (byte)(value & 0x7F);
            value >>= 7;
            if (value == 0) { stream.WriteByte((byte)(part | 0x80)); return; }
            stream.WriteByte(part); value--;
        }
    }

    private static uint Read(byte[] bytes, int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
    private static void Write(byte[] bytes, int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), value);
    private static string Digest(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    private static void Add(ZipArchive zip, string name, byte[] bytes)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var output = entry.Open(); output.Write(bytes);
    }

    private static string Readme(ushort id) => $"""
        ACE HUNTERS / BATTLEQUARTERS - EXPERIMENTAL, IN-GAME UNVERIFIED

        Quest {id}: Battlequarters, one large monster in area 3.
        This is a controlled single-player test, not a verified companion feature.
        Two code gates request/spawn the story Leader and Gunner (NPC types 8/9).
        They may fail to load, stand still, malfunction or crash outside their story script.
        Monster compatibility, combat AI, quest completion and saving need live testing.

        INPUT VERSION
        USA title {TitleId}; only the inspected update's decompressed code.bin:
        SHA-256 {CodeSha256}
        Source length {CodeLength}. Export checked SHA-256; Azahar checks BPS source CRC.
        A wrong update or another code mod must not be forced past these checks.
        This bundle contains no full game executable, save, or original quest template.

        TEST SETUP (OFFLINE, COPIED PROFILE ONLY)
        1. Close the game and emulator. Back up the entire emulator user folder.
           Use a separate test profile with copies of your save and all five quest files.
        2. Install the same USA update in that test profile. Do not stack code mods.
        3. Import quest/m{id}.mhquest into Classic MH Tools. Keep quest ID {id}.
           Connect the COPIED extdata folder explicitly and save/install that quest.
           Confirm the destination before accepting the normal five-file backup prompt.
        4. In Azahar, open this game's Mods Location. Put the included exefs/code.bps
           under the title's mod folder. The complete relative path from Azahar's
           user folder is load/mods/{TitleId}/exefs/code.bps.
           Back up any existing code mod first; never overwrite it with this patch.
        5. Fully restart the game. First try the quest without the patch as a control,
           then with the patch. Compare area 3, helper presence, motion, attacks,
           quest completion and emulator logs. Do not test online or on your main save.
        6. If the test crashes or behaves oddly, quit without saving and remove code.bps.

        DISABLING / EDITING
        Close the game and remove this code.bps, then restart. Disable the quest separately
        in the editor if desired. Unchecking the editor checkbox ONLY changes authoring
        intent; it does not remove an installed emulator patch. Exporting does not install.
        The .mib has no helper bit. Keep the .mhquest to preserve the experimental intent.
        The patch targets exactly quest {id} plus map 19, with the original area-3 gate.
        If its ID changes, export again. Only one helper quest patch is supported at a time.
        It preserves the original story-quest gates and does not reset story completion.

        Azahar mod layout: https://citra.azahar-emu.org/help/feature/game-modding/
        """;
}
