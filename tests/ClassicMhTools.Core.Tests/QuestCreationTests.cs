using System.Buffers.Binary;
using ClassicMhTools.Core.CustomQuests;

namespace ClassicMhTools.Core.Tests;

public sealed class QuestCreationTests
{
    private static uint D(byte[] bytes, int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
    private static ushort W(byte[] bytes, int at) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at));

    [Fact] public void NewQuestIsGeneratedWithoutAnyTemplateAndHasCompleteTerminatedTables()
    {
        var draft = new QuestCreationDraft();
        var document = draft.Build();
        var raw = document.ToArray();
        Assert.InRange(raw.Length, 0x100, 0x2000);
        Assert.Equal(0xA0u, D(raw, 0));
        Assert.Equal("v005"u8.ToArray(), raw.AsSpan(4, 4).ToArray());
        Assert.Equal((ushort)60013, document.Id); Assert.Equal("Event", document.Category);
        Assert.Equal("New quest", document.Title); Assert.Equal(35, document.TextFields.Count);
        Assert.Equal(new uint[] { 1 }, document.MonsterIds);
        Assert.Equal((byte)11, document.MapId); Assert.Equal(3000u, document.RewardZenny);
        Assert.Equal(4, document.NumberFields.Count(f => f.Section == "Supplies"));
        Assert.DoesNotContain(document.NumberFields, f => f.Section == "Rewards");
        foreach (var at in new[] { 0, 8, 0x1C, 0x20, 0x24, 0x28, 0x2C, 0x30 }) Assert.Equal(0u, D(raw, at) % 16);
        var waves = (int)D(raw, 0x28); var monsters = (int)D(raw, waves);
        Assert.Equal(uint.MaxValue, D(raw, monsters + 0x28)); Assert.Equal(0u, D(raw, waves + 4));
        var smallTop = (int)D(raw, 0x2C);
        Assert.Equal(0u, D(raw, smallTop + 12));
        for (var group = 0; group < 3; group++)
        {
            var sub = (int)D(raw, smallTop + group * 4); Assert.Equal(0u, D(raw, sub + 4));
            Assert.Equal(uint.MaxValue, D(raw, (int)D(raw, sub)));
        }
        Assert.Equal(ushort.MaxValue, W(raw, (int)D(raw, 0x30)));
        Assert.Equal(raw, QuestCrypto.DecodeMib(QuestCrypto.EncodeMib(raw)));
        Assert.Equal(raw, draft.Build().ToArray());
    }

    [Fact] public void MultipleWavesStatsObjectivesSuppliesAndRewardGroupsHaveCorrectNativeOffsets()
    {
        var draft = new QuestCreationDraft();
        draft.Monsters.Add(new() { Id = 2, Picture = 4, Wave = 2, X = 12.5f, Y = -20, Z = 30, Rotation = 45, Size = 125, Hp = 44 });
        draft.Objectives[1] = new() { Type = 129, Target = 2, Quantity = 1 };
        draft.Objectives[2] = new() { Type = 2, Target = 8, Quantity = 3 };
        draft.Supplies.Add(new() { Box = 3, Item = 8, Quantity = 5 });
        draft.Rewards = [new() { Table = 0, Item = 8, Quantity = 2, Chance = 70 }, new() { Table = 0, Item = 785, Chance = 30 }, new() { Table = 2, Item = 768 }];
        var doc = draft.Build(); var raw = doc.ToArray(); var h = (int)D(raw, 0);
        Assert.Equal((byte)0x40, raw[h + 2]); Assert.Equal((byte)2, raw[h + 0x2B]);
        Assert.Equal(129u, D(raw, h + 0x34)); Assert.Equal((ushort)2, W(raw, h + 0x38));
        Assert.Equal(2u, D(raw, h + 0x3C)); Assert.Equal((ushort)8, W(raw, h + 0x40));
        Assert.Equal((ushort)125, W(raw, 0x3C)); Assert.Equal((byte)44, raw[0x3F]);
        Assert.Equal((ushort)4, W(raw, h + 0x4A)); Assert.Equal((ushort)98, W(raw, h + 0x4C));
        var waves = (int)D(raw, 0x28); var second = (int)D(raw, waves + 4);
        Assert.Equal(2u, D(raw, second)); Assert.Equal(12.5f, BinaryPrimitives.ReadSingleLittleEndian(raw.AsSpan(second + 16)));
        Assert.Equal(45u, D(raw, second + 32)); Assert.Equal(0u, D(raw, waves + 8));
        var supplies = (int)D(raw, 8); Assert.Equal((byte)3, raw[supplies + 24]); Assert.Equal((byte)255, raw[supplies + 32]);
        var rewards = (int)D(raw, 0x1C); var items = (int)D(raw, rewards + 4);
        Assert.Equal(0x8000u, D(raw, rewards)); Assert.Equal(65535u, D(raw, rewards + 8));
        Assert.Equal((ushort)70, W(raw, items)); Assert.Equal((ushort)785, W(raw, items + 8)); Assert.Equal(ushort.MaxValue, W(raw, items + 12));
        Assert.Equal(6, doc.NumberFields.Count(f => f.Section == "Supplies"));
        Assert.Equal(9, doc.NumberFields.Count(f => f.Section == "Rewards"));
    }

    [Fact] public void GrowingMultilingualTextIsReallocatedAndAuthoringSourceRoundTrips()
    {
        var draft = new QuestCreationDraft();
        draft.Languages[0].Title = "An independently authored quest";
        draft.Languages[0].Description = new string('x', 1000);
        draft.Languages[1].Title = "Une nouvelle quete";
        draft.Languages[2].Description = "Unicode: \u00f1 \u6f22 \ud83c\udf0d";
        var before = draft.Build().ToArray();
        var loaded = QuestCreationDraft.FromJson(draft.ToJson());
        Assert.Equal(before, loaded.Build().ToArray());
        Assert.Equal(draft.Languages[2].Description, loaded.Build().TextFields.Single(f => f.Language == "Spanish" && f.Name == "Description").Value);
        loaded.Monsters.Clear(); loaded.Objectives[0] = new() { Type = 2, Target = 8, Quantity = 2 }; loaded.Type = 4;
        Assert.Empty(loaded.Build().MonsterIds); Assert.Single(draft.Monsters);
        Assert.Single(draft.Clone().Monsters);
    }

    [Theory]
    [InlineData(59999)] [InlineData(61000)] [InlineData(62000)] [InlineData(65535)]
    public void CreatorRejectsNonEventIds(ushort id) => Assert.Throws<InvalidDataException>(() => new QuestCreationDraft { Id = id }.Build());

    [Fact] public void OversizeInvalidUtf16AndNullTextAreRejectedWithoutTruncation()
    {
        var draft = new QuestCreationDraft();
        draft.Languages[0].Description = new string('x', 4096);
        Assert.Throws<InvalidDataException>(() => draft.Build());
        draft.Languages[0].Description = "bad\0text"; Assert.Throws<InvalidDataException>(() => draft.Build());
        draft.Languages[0].Description = "\ud800"; Assert.Throws<InvalidDataException>(() => draft.Build());
        draft.Languages[0].Description = null!; Assert.Throws<InvalidDataException>(() => draft.Build());
    }

    [Fact] public void InvalidSpawnCountsWavesCoordinatesAndObjectivesAreRejected()
    {
        void Reject(Action<QuestCreationDraft> edit) { var d = new QuestCreationDraft(); edit(d); Assert.Throws<InvalidDataException>(() => d.Build()); }
        Reject(d => d.Monsters[0].Wave = 2); Reject(d => d.Monsters[0].Quantity = 6); Reject(d => d.Monsters[0].X = float.NaN);
        Reject(d => d.Monsters[0].Id = 50); Reject(d => d.Monsters[0].Area = 0); Reject(d => d.Monsters.Clear());
        Reject(d => d.Objectives[0].Target = 2); Reject(d => d.Objectives[0].Quantity = 2); Reject(d => d.Objectives[0].Type = 0);
        Reject(d => d.Monsters.AddRange(Enumerable.Range(0, 5).Select(_ => new QuestMonsterDraft())));
        Reject(d => d.Map = 13); Reject(d => d.Penalty = d.Reward + 1);
    }

    [Fact] public void InvalidItemsChancesTablesAndSourceVersionAreRejected()
    {
        void Reject(Action<QuestCreationDraft> edit) { var d = new QuestCreationDraft(); edit(d); Assert.Throws<InvalidDataException>(() => d.Build()); }
        Reject(d => d.Supplies[0].Box = 4); Reject(d => d.Supplies[0].Quantity = 0); Reject(d => d.Supplies[0].Item = 0);
        Reject(d => d.Supplies = Enumerable.Range(0, 41).Select(_ => new QuestSupplyDraft()).ToList());
        Reject(d => d.Rewards.Add(new() { Chance = 101 })); Reject(d => d.Rewards.Add(new() { Table = 3 }));
        Reject(d => d.FormatVersion = 2); Reject(d => d.Languages = []); Reject(d => d.Objectives = []);
        Assert.Throws<InvalidDataException>(() => QuestCreationDraft.FromJson("{\"FormatVersion\":2}"));
        Assert.Throws<InvalidDataException>(() => QuestCreationDraft.FromJson("{broken"));
    }

    [Fact] public void CreatorLargeMonsterSetMatchesRecoveredCatalog()
    {
        var catalog = QuestReferenceCatalog.Load(Path.Combine(AppContext.BaseDirectory, "assets", "quests", "reference.json"));
        Assert.Equal(catalog.LargeMonsterIds, Enumerable.Range(1, 123).Select(i => (uint)i).Where(QuestCreationDraft.IsLargeMonster).ToArray());
    }

    [Fact] public void CreatorOptionsRetainRecoveredIdsIncludingDifferentArenaRankValues()
    {
        var catalog = QuestReferenceCatalog.Load(Path.Combine(AppContext.BaseDirectory, "assets", "quests", "reference.json"));
        Assert.Equal(Enumerable.Range(0, 37).Select(i => (uint)i), catalog.Options["requirements"].Keys.Order());
        Assert.Contains("G1", catalog.Options["requirements"][32]);
        Assert.Equal("Arena", catalog.Options["carve_ranks"][0]);
        Assert.Equal("Arena", catalog.Options["gather_ranks"][4]);
        Assert.Equal(new uint[] { 0, 1, 2, 8, 9, 22 }, catalog.Options["frenzy"].Keys.Order());
        Assert.Equal(new uint[] { 0, 1, 2 }, catalog.Options["spawn_areas"].Keys.Order());
        Assert.Equal(15, catalog.Options["ranks"].Count);
        Assert.Equal(7, catalog.Options["monster_ai"].Count);
    }

    [Fact] public void NamedChoiceIdsAndUnknownValuesSurviveSourceAndNativeRoundTrips()
    {
        var draft = new QuestCreationDraft { Requirement1 = 32, Requirement2 = 36, GatherRank = 4, CarveRank = 0, MonsterAi = 6, HunterSpawn = 2 };
        draft.Monsters[0].Infection = 22;
        var loaded = QuestCreationDraft.FromJson(draft.ToJson());
        var raw = loaded.Build().ToArray(); var header = (int)D(raw, 0);
        Assert.Equal((byte)32, raw[header + 0x25]); Assert.Equal((byte)36, raw[header + 0x26]);
        Assert.Equal(draft.Build().ToArray(), raw);
        draft.Requirement2 = 250;
        Assert.Equal((byte)250, QuestCreationDraft.FromJson(draft.ToJson()).Requirement2);
        Assert.Equal(draft.Build().ToArray(), QuestCreationDraft.FromJson(draft.ToJson()).Build().ToArray());
    }

    [Fact] public void UnrestrictedIdAllocationAvoidsUnknownRestrictedAndLocalIds()
    {
        var catalog = QuestReferenceCatalog.Load(Path.Combine(AppContext.BaseDirectory, "assets", "quests", "reference.json"));
        Assert.Contains((ushort)60013, catalog.UnrestrictedEventIds);
        Assert.DoesNotContain((ushort)60000, catalog.UnrestrictedEventIds);
        Assert.DoesNotContain((ushort)60004, catalog.UnrestrictedEventIds);
        var first = QuestCreationPolicy.AssignEventId(catalog, [], []);
        Assert.Equal((ushort)60013, first);
        Assert.Equal((ushort)60015, QuestCreationPolicy.AssignEventId(catalog, [60013], [60014]));
        Assert.Equal((ushort)60014, QuestCreationPolicy.AssignEventId(catalog, [60013], catalog.UnrestrictedEventIds));
        Assert.Throws<InvalidOperationException>(() => QuestCreationPolicy.AssignEventId(catalog, catalog.UnrestrictedEventIds, []));
    }

    [Fact] public void InstallPolicyRejectsUnsafeIdsAndFirstWaveCutsceneMapMismatch()
    {
        var catalog = QuestReferenceCatalog.Load(Path.Combine(AppContext.BaseDirectory, "assets", "quests", "reference.json"));
        QuestCreationPolicy.ValidateForInstallation(new(), catalog);
        Assert.Throws<InvalidDataException>(() => QuestCreationPolicy.ValidateForInstallation(new() { Id = 60000 }, catalog));
        Assert.Throws<InvalidDataException>(() => QuestCreationPolicy.ValidateForInstallation(new() { Id = 60004 }, catalog));
        var draft = new QuestCreationDraft(); draft.Monsters[0].Id = 116; draft.Objectives[0].Target = 116;
        Assert.Throws<InvalidDataException>(() => QuestCreationPolicy.ValidateForInstallation(draft, catalog));
        draft.Map = 20; QuestCreationPolicy.ValidateForInstallation(draft, catalog);
    }

    [Theory] [InlineData(11, true)] [InlineData(12, false)] [InlineData(16, false)] [InlineData(19, false)]
    public void FenceSettingsOnlyApplyToTheRecoveredArenaStage(byte map, bool enabled)
    {
        var draft = new QuestCreationDraft { Map = map, Fence = 1, FenceState = 1, FenceUptime = 30, FenceCooldown = 20 };
        Assert.Equal(enabled, QuestCreationPolicy.HasFence(map));
        var bytes = draft.Build().ToArray();
        Assert.Equal(enabled ? new byte[] { 1, 1, 30, 20 } : new byte[4], bytes.AsSpan(0x8D, 4).ToArray());
    }

    [Fact] public void CreatedQuestExportsToDerivedExtdataWithoutChangingTheInputAndCanBeInstalledAndRemoved()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ClassicMhCreator-" + Guid.NewGuid().ToString("N"));
        try
        {
            var input = Path.Combine(folder, "input"); Directory.CreateDirectory(input);
            for (var i = 1; i <= 5; i++) File.WriteAllBytes(Path.Combine(input, $"quest{i}"),
                QuestCrypto.EncodeContainer(new(new byte[QuestCrypto.PayloadSize], 0x12340010, Enumerable.Repeat((byte)i, 256).ToArray())));
            var originals = Enumerable.Range(1, 5).Select(i => File.ReadAllBytes(Path.Combine(input, $"quest{i}"))).ToArray();
            var set = QuestExtdataSet.Load(input); var doc = new QuestCreationDraft().Build(); var plan = set.PlanInstall(doc);
            var output = Path.Combine(folder, "output"); set.ExportPatchedCopy(plan, output);
            for (var i = 1; i <= 5; i++) Assert.Equal(originals[i - 1], File.ReadAllBytes(Path.Combine(input, $"quest{i}")));
            var reopened = QuestExtdataSet.Load(output); Assert.Single(reopened.Quests);
            Assert.Equal(doc.ToArray(), reopened.Quests[0].Document.ToArray().AsSpan(0, doc.Length).ToArray());
            Assert.Equal(doc.Title, reopened.Quests[0].Document.Title);
            var backup = set.Commit(plan); Assert.Equal(5, Directory.GetFiles(backup, "quest?").Length);
            for (var i = 2; i <= 5; i++) Assert.Equal(originals[i - 1], File.ReadAllBytes(Path.Combine(input, $"quest{i}")));
            var installed = QuestExtdataSet.Load(input); installed.Commit(installed.PlanRemove(doc.Id));
            Assert.Empty(QuestExtdataSet.Load(input).Quests);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
