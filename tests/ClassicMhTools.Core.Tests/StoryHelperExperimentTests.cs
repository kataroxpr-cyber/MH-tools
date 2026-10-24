using System.Buffers.Binary;
using System.IO.Hashing;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using ClassicMhTools.Core.CustomQuests;

namespace ClassicMhTools.Core.Tests;

public sealed class StoryHelperExperimentTests
{
    private static QuestReferenceCatalog Catalog() => QuestReferenceCatalog.Load(Path.Combine(AppContext.BaseDirectory, "assets", "quests", "reference.json"));

    [Fact] public void RathianPresetHasSupportedIdentityAndSourceOnlyHelperIntent()
    {
        var catalog = Catalog();
        var draft = StoryHelperExperiment.CreateRathian(QuestCreationPolicy.AssignEventId(catalog, [], []), catalog);
        Assert.True(draft.ExperimentalStoryHelpers);
        Assert.Equal((byte)19, draft.Map); Assert.Equal((uint?)3, draft.Faints);
        var monster = Assert.Single(draft.Monsters);
        Assert.Equal(1u, monster.Id); Assert.Equal((byte)3, monster.Area);
        Assert.Equal(1133f, monster.X); Assert.Equal(-2519f, monster.Z);
        Assert.NotEmpty(draft.Supplies); Assert.NotEmpty(draft.Rewards);
        var raw = draft.Build().ToArray();
        Assert.True(QuestCreationDraft.FromJson(draft.ToJson()).ExperimentalStoryHelpers);
        draft.ExperimentalStoryHelpers = false;
        Assert.DoesNotContain("ExperimentalStoryHelpers", draft.ToJson());
        Assert.Equal(raw, draft.Build().ToArray());
        Assert.False(new QuestCreationDraft().Clone().ExperimentalStoryHelpers);
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void ExperimentRejectsUnsupportedScenarios(int scenario)
    {
        var draft = StoryHelperExperiment.CreateRathian(60013, Catalog());
        switch (scenario)
        {
            case 0: draft.Map = 11; break;
            case 1: draft.Monsters[0].Area = 1; break;
            case 2: draft.Monsters[0].Quantity = 2; break;
            case 3: draft.Monsters.Add(new() { Id = 2, Area = 3 }); break;
            case 4: draft.Type = 4; break;
        }
        Assert.Throws<InvalidDataException>(draft.Validate);
    }

    [Fact] public void PublicExporterRejectsUnknownAndModifiedCodeWithoutMutatingInput()
    {
        Assert.Throws<InvalidDataException>(() => StoryHelperExperiment.PatchCode([], 60013));
        var synthetic = SyntheticCode();
        var digest = Crc32.HashToUInt32(synthetic);
        Assert.Throws<InvalidDataException>(() => StoryHelperExperiment.PatchCode(synthetic, 60013));
        Assert.Throws<InvalidDataException>(() => StoryHelperExperiment.BuildBundle(StoryHelperExperiment.CreateRathian(60013, Catalog()), synthetic, Catalog()));
        Assert.Equal(digest, Crc32.HashToUInt32(synthetic));
    }

    [Fact] public void PatchChangesOnlyTwoGatesAndUnusedCodePadding()
    {
        var source = SyntheticCode(); var sourceCrc = Crc32.HashToUInt32(source);
        var target = StoryHelperExperiment.PatchVerifiedLayout(source, 60013);
        Assert.Equal(sourceCrc, Crc32.HashToUInt32(source));
        for (var i = 0; i < source.Length; i++)
            if (source[i] != target[i])
                Assert.True(i is >= 0x59D2A4 and < 0x59D2A8 or >= 0x5B32A8 and < 0x5B32AC or >= 0xCEC740 and < 0xCEC7E0);
        Assert.Equal(0xDEC740, BranchTarget(target, 0x69D2A4));
        Assert.Equal(0xDEC790, BranchTarget(target, 0x6B32A8));
        foreach (var (block, site) in new[] { (0xDEC740, 0x69D2A4), (0xDEC790, 0x6B32A8) })
        {
            Assert.Equal(site + 8, BranchTarget(target, block + 52));
            Assert.Equal(site + 8, BranchTarget(target, block + 68));
            Assert.Equal(60013u, Read(target, block - 0x100000 + 72));
            Assert.Equal(0xFB6B7Cu, Read(target, block - 0x100000 + 76));
        }
        var bps = StoryHelperExperiment.BuildBps(source, target);
        Assert.InRange(bps.Length, 50, 1024);
        Assert.Equal(target, ApplyBps(source, bps));
    }

    [Fact] public void LayoutGuardsRejectStoryIdsChangedInstructionsAndOccupiedPadding()
    {
        var code = SyntheticCode();
        Assert.Throws<InvalidDataException>(() => StoryHelperExperiment.PatchVerifiedLayout(code, 1002));
        code[0x59D2AC] ^= 1;
        Assert.Throws<InvalidDataException>(() => StoryHelperExperiment.PatchVerifiedLayout(code, 60013));
        code = SyntheticCode(); code[0xCEC7DF] = 1;
        Assert.Throws<InvalidDataException>(() => StoryHelperExperiment.PatchVerifiedLayout(code, 60013));
    }

    [Fact] public void OptInInspectedCodeProducesSelfConsistentQuestAndPatchBundle()
    {
        var path = Environment.GetEnvironmentVariable("CLASSIC_MH_HELPER_CODE");
        if (string.IsNullOrWhiteSpace(path)) { Assert.Skip("Private code verification is opt-in via CLASSIC_MH_HELPER_CODE."); return; }
        var source = File.ReadAllBytes(path);
        var catalog = Catalog(); var draft = StoryHelperExperiment.CreateRathian(60016, catalog);
        var bytes = StoryHelperExperiment.BuildBundle(draft, source, catalog);
        using var zip = new ZipArchive(new MemoryStream(bytes));
        Assert.Equal(5, zip.Entries.Count);
        Assert.All(zip.Entries, e => Assert.InRange(e.Length, 1, 20000));
        byte[] Entry(string name)
        {
            using var input = zip.GetEntry(name)!.Open(); using var buffer = new MemoryStream();
            input.CopyTo(buffer); return buffer.ToArray();
        }
        var patch = Entry($"mods/{StoryHelperExperiment.TitleId}/exefs/code.bps");
        var target = ApplyBps(source, patch);
        Assert.Equal(StoryHelperExperiment.PatchCode(source, draft.Id), target);
        Assert.Equal(draft.Build().ToArray(), Entry("quest/m60016.mib"));
        var restored = QuestCreationDraft.FromJson(System.Text.Encoding.UTF8.GetString(Entry("quest/m60016.mhquest")));
        Assert.True(restored.ExperimentalStoryHelpers); Assert.Equal(draft.Build().ToArray(), restored.Build().ToArray());
        using var manifest = JsonDocument.Parse(Entry("manifest.json"));
        Assert.Equal(draft.Id, manifest.RootElement.GetProperty("QuestId").GetUInt16());
        Assert.Equal(Convert.ToHexString(SHA256.HashData(target)).ToLowerInvariant(), manifest.RootElement.GetProperty("TargetCodeSha256").GetString());
        Assert.Equal(Convert.ToHexString(SHA256.HashData(patch)).ToLowerInvariant(), manifest.RootElement.GetProperty("PatchSha256").GetString());
        Assert.Equal(StoryHelperExperiment.CodeSha256, Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant());
        source[0] ^= 1;
        Assert.Throws<InvalidDataException>(() => StoryHelperExperiment.BuildBundle(draft, source, catalog));
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(127)] [InlineData(128)] [InlineData(16384)] [InlineData(100000)]
    public void BpsLengthAndCommandVarintsRoundTripWithIndependentReader(int length)
    {
        var source = new byte[length]; new Random(42).NextBytes(source);
        var target = (byte[])source.Clone();
        for (var i = 0; i < length; i++) if (i % 257 < 40) target[i] ^= 0xFF;
        Assert.Equal(target, ApplyBps(source, StoryHelperExperiment.BuildBps(source, target)));
        Assert.Equal(source, ApplyBps(source, StoryHelperExperiment.BuildBps(source, source)));
        Assert.Throws<ArgumentException>(() => StoryHelperExperiment.BuildBps(source, new byte[length + 1]));
    }

    private static byte[] SyntheticCode()
    {
        var code = new byte[StoryHelperExperiment.CodeLength];
        foreach (var site in new[] { 0x59D2A4, 0x5B32A8 })
        {
            Write(code, site, 0xE2401C03); Write(code, site + 4, 0xE25110EA);
            Write(code, site + 8, site == 0x59D2A4 ? 0x03570003u : 0x03550003u);
            Write(code, site + 12, site == 0x59D2A4 ? 0x0A000002u : 0x1A00001Bu);
        }
        return code;
    }

    // Independent BPS reader also verifies all three footer CRCs and full consumption.
    internal static byte[] ApplyBps(byte[] source, byte[] patch)
    {
        Assert.Equal("BPS1"u8.ToArray(), patch.AsSpan(0, 4).ToArray());
        Assert.Equal(Crc32.HashToUInt32(source), Read(patch, patch.Length - 12));
        Assert.Equal(Crc32.HashToUInt32(patch.AsSpan(0, patch.Length - 4)), Read(patch, patch.Length - 4));
        var cursor = 4;
        ulong Variable()
        {
            ulong result = 0, shift = 1;
            for (var i = 0; i < 10; i++)
            {
                var next = patch[cursor++]; result += (ulong)(next & 127) * shift;
                if ((next & 128) != 0) return result;
                shift <<= 7; result += shift;
            }
            throw new InvalidDataException("Invalid variable integer.");
        }
        Assert.Equal((ulong)source.Length, Variable());
        var target = new byte[checked((int)Variable())]; Assert.Equal(0ul, Variable());
        var output = 0;
        while (output < target.Length)
        {
            var command = Variable(); var count = checked((int)(command >> 2) + 1);
            switch (command & 3)
            {
                case 0: source.AsSpan(output, count).CopyTo(target.AsSpan(output)); break;
                case 1: patch.AsSpan(cursor, count).CopyTo(target.AsSpan(output)); cursor += count; break;
                default: throw new InvalidDataException("Unexpected BPS command.");
            }
            output += count;
        }
        Assert.Equal(patch.Length - 12, cursor);
        Assert.Equal(Crc32.HashToUInt32(target), Read(patch, patch.Length - 8));
        return target;
    }

    private static int BranchTarget(byte[] code, int at) => at + 8 + ((int)(Read(code, at - 0x100000) << 8) >> 6);
    private static uint Read(byte[] bytes, int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
    private static void Write(byte[] bytes, int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), value);
}
