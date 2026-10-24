using System.Buffers.Binary;
using ClassicMhTools.Core.CustomQuests;

namespace ClassicMhTools.Core.Tests;

public sealed class QuestExpansionTests
{
    private static int P(byte[] b, int at) => checked((int)BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at)));
    private static ushort W(byte[] b, int at) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at));

    [Fact] public void IndependentArtworkMatchesGameHeaderWithoutChangingSpawns()
    {
        var d = new QuestCreationDraft { PreviewArt = [84, 88, 0, 88, 84] };
        var doc = d.Build(); var b = doc.ToArray(); var h = P(b, 0);
        Assert.Equal(d.PreviewArt, doc.PreviewArt);
        Assert.Equal(new uint[] { 1 }, doc.MonsterIds);
        Assert.Equal((ushort)84, W(b, h + 0x48)); Assert.Equal((ushort)84, W(b, h + 0x50));
        Assert.Equal(b, QuestCreationDraft.FromJson(d.ToJson()).Build().ToArray());
        var art = doc.NumberFields.Single(f => f.Name == "Preview 3");
        var patched = doc.Patch(new Dictionary<int, uint> { [art.Offset] = 123 }, new Dictionary<int, string>());
        Assert.Equal((ushort)123, patched.PreviewArt[2]);
        Assert.Equal(doc.MonsterIds, patched.MonsterIds);
    }

    [Theory] [InlineData(3000u, 3u, 1000u)] [InlineData(3001u, 3u, 1001u)] [InlineData(3000u, 1u, 3000u)] [InlineData(3000u, 5u, 600u)]
    public void FaintCountCalculatesExactFailureThreshold(uint reward, uint faints, uint penalty)
    {
        Assert.Equal(penalty, QuestCreationDraft.PenaltyFor(reward, faints));
        var d = new QuestCreationDraft { Reward = reward, Faints = faints };
        var b = d.Build().ToArray(); Assert.Equal(penalty, BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(P(b, 0) + 12)));
    }

    [Fact] public void UnrepresentableFaintCountsAndInvalidArtAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => QuestCreationDraft.PenaltyFor(5, 4));
        Assert.Throws<InvalidDataException>(() => QuestCreationDraft.PenaltyFor(3000, 0));
        Assert.Throws<InvalidDataException>(() => new QuestCreationDraft { PreviewArt = [1] }.Build());
        Assert.Throws<InvalidDataException>(() => new QuestCreationDraft { PreviewArt = [1, 2, 3, 4, 124] }.Build());
    }

    [Fact] public void IntruderAndSmallMonsterTablesHaveIndependentTerminatedNativeLayouts()
    {
        var d = new QuestCreationDraft { IntruderTimer = 5, IntruderChance = 70, IntruderStats = new() { Hp = 45 }, SmallStats = new() { Hp = 12 } };
        d.Monsters.Add(new() { Kind = 1, Id = 2, Area = 1, Chance = 65, X = 22 });
        d.Monsters.Add(new() { Kind = 1, Id = 7, Area = 1, Chance = 35 });
        d.Monsters.Add(new() { Kind = 2, Id = 50, Area = 1, Quantity = 8 });
        d.Monsters.Add(new() { Kind = 2, Id = 51, Area = 1, Wave = 3, Quantity = 4 });
        d.SmallConditions[1] = new() { Type = 1, Target = 50, Quantity = 8 };
        var doc = d.Build(); var b = doc.ToArray(); var intruder = P(b, 0x30);
        Assert.Equal((ushort)65, W(b, intruder)); Assert.Equal(2, P(b, intruder + 4));
        Assert.Equal((ushort)35, W(b, intruder + 0x2C)); Assert.Equal(7, P(b, intruder + 0x30));
        Assert.Equal(ushort.MaxValue, W(b, intruder + 0x58));
        Assert.Equal((byte)45, b[0x47]); Assert.Equal((byte)12, b[0x5F]);
        Assert.Equal((byte)2, b[P(b, 0) + 1]); Assert.Equal((byte)5, b[0x80]); Assert.Equal((byte)70, b[0x82]);
        var top = P(b, 0x2C); var wave1 = P(b, P(b, top)); var wave3 = P(b, P(b, top + 8));
        Assert.Equal(50, P(b, wave1)); Assert.Equal(8, P(b, wave1 + 4)); Assert.Equal(51, P(b, wave3));
        Assert.Equal((ushort)50, W(b, 0x70)); Assert.Equal((byte)8, b[0x72]); Assert.Equal((byte)2, b[0x73]);
        Assert.Contains(50u, doc.MonsterIds); Assert.Contains(2u, doc.MonsterIds);
        Assert.Equal(b, doc.Patch(new Dictionary<int, uint>(), new Dictionary<int, string>()).ToArray());
        Assert.Equal(b, QuestCreationDraft.FromJson(d.ToJson()).Build().ToArray());
    }

    [Fact] public void UnsafeMonsterTableAssignmentsAndIntruderCombinationsAreRejected()
    {
        void Reject(QuestMonsterDraft m) { var d = new QuestCreationDraft(); d.Monsters.Add(m); Assert.Throws<InvalidDataException>(() => d.Build()); }
        Reject(new() { Kind = 2, Id = 1 }); Reject(new() { Kind = 1, Id = 50 });
        Reject(new() { Kind = 2, Id = 50, Wave = 4 }); Reject(new() { Kind = 1, Id = 2, Chance = 101 });
        Assert.Throws<InvalidDataException>(() => new QuestCreationDraft { IntruderChance = 101 }.Build());
        Assert.Throws<InvalidDataException>(() => new QuestCreationDraft { SmallStats = new() { Size = 0 } }.Build());
        Assert.Throws<InvalidDataException>(() => new QuestCreationDraft { IntruderStats = new() { Size = 0 } }.Build());
        var d = new QuestCreationDraft(); d.Monsters.Add(new() { Id = 2 }); d.Monsters.Add(new() { Kind = 1, Id = 7 });
        Assert.Throws<InvalidDataException>(() => d.Build());
    }

    [Fact] public void ExpandedReferenceContainsNamedArtworkMapZonesAndRankMatchedMaterialPresets()
    {
        var c = QuestReferenceCatalog.Load(Path.Combine(AppContext.BaseDirectory, "assets", "quests", "reference.json"));
        Assert.Equal("Danger", c.PreviewNames[84]); Assert.Equal("None", c.PreviewNames[98]);
        Assert.Equal(new byte[] { 1 }, c.MapZones[11].Select(z => z.Id));
        Assert.Equal(11, c.MapZones[17].Count(z => z.Id > 0));
        Assert.All(c.ItemPresets, p => { Assert.InRange(p.Rank, (byte)1, (byte)3); Assert.All(p.Rewards, r => Assert.InRange(r.Chance, (ushort)1, (ushort)100)); });
        var low = c.ItemPresets.Single(p => p.Monster == 1 && p.Rank == 1);
        var high = c.ItemPresets.Single(p => p.Monster == 1 && p.Rank == 2);
        Assert.Contains(low.Rewards, r => r.Item == 487); Assert.Contains(high.Rewards, r => r.Item == 488);
        Assert.All(c.ItemPresets.Where(p => p.Monster == 0), p => Assert.InRange(p.Supplies.Length, 1, 40));
    }
    [Fact] public void CombinedRewardPresetsIncludeAllFiveSpeciesWithNormalizedChances()
    {
        var c = new QuestReferenceCatalog { ItemPresets = Enumerable.Range(1, 5).Select(i => new QuestItemPreset(1, (uint)i, [], [new() { Item = (ushort)(100 + i), Chance = 99 }, new() { Item = (ushort)(200 + i), Chance = 1 }])).ToArray() };
        var rewards = QuestPresetPolicy.Rewards(c, [1, 2, 3, 4, 5], 1);
        Assert.Equal(10, rewards.Count); Assert.Equal(5, rewards.Count(r => r.Item is > 100 and < 106));
        Assert.All(rewards.GroupBy(r => r.Table), g => Assert.Equal(100, g.Sum(r => r.Chance)));
        Assert.All(rewards, r => Assert.InRange(r.Chance, (ushort)1, (ushort)100));
        Assert.Empty(QuestPresetPolicy.Rewards(c, [1], 2));
    }
}
