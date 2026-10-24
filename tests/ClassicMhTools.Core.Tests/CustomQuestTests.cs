using System.Buffers.Binary;
using ClassicMhTools.Core.CustomQuests;

namespace ClassicMhTools.Core.Tests;

public sealed class CustomQuestTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ClassicMhQuestTests-" + Guid.NewGuid().ToString("N"));
    internal static byte[] SyntheticQuest(ushort id = 60013)
    {
        var bytes = new byte[0x200];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0xA0);
        "v005"u8.CopyTo(bytes.AsSpan(4));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0xC0), id);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0xC2), 10);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0xBC), 0x100);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x100), 0x120);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x120), 0x140);
        System.Text.Encoding.Unicode.GetBytes("Synthetic quest").CopyTo(bytes, 0x140);
        return bytes;
    }

    private QuestExtdataSet EmptySet()
    {
        Directory.CreateDirectory(_folder);
        for (var i = 1; i <= 5; i++)
        {
            var payload = new byte[QuestCrypto.PayloadSize];
            payload[^1] = 43;
            File.WriteAllBytes(Path.Combine(_folder, $"quest{i}"), QuestCrypto.EncodeContainer(new(payload, 0x12340010, new byte[256])));
        }
        return QuestExtdataSet.Load(_folder);
    }

    [Fact] public void QuestSummaryReadsMonstersIntrudersMapAndRewardWithoutMutation()
    {
        var bytes = new byte[0x400];
        SyntheticQuest().CopyTo(bytes, 0);
        bytes[0xC4] = 11;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0xA8), 389999);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x28), 0x200);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x200), 0x240);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x240), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x268), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x290), uint.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x30), 0x300);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x300), 100);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x304), 19);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x32C), ushort.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0xCC), 257);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0xD0), 60);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0xD4), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0xD8), 785);
        var document = new QuestDocument(bytes);
        Assert.Equal(new uint[] { 1, 60, 19 }, document.MonsterIds);
        Assert.Equal((byte)11, document.MapId);
        Assert.Equal(389999u, document.RewardZenny);
        Assert.Equal(bytes, document.ToArray());
    }

    [Fact] public void InvalidIntruderTablesAreRejected()
    {
        var bytes = SyntheticQuest();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x30), 0x1FE);
        Assert.Throws<InvalidDataException>(() => new QuestDocument(bytes));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x30), 0x7FFF);
        Assert.Throws<InvalidDataException>(() => new QuestDocument(bytes));
    }

    [Fact] public void OfficialOriginRequiresKnownContentNotJustQuestId()
    {
        var raw = SyntheticQuest();
        var catalog = new QuestReferenceCatalog { OfficialQuests = [new(60013, raw.Length,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(raw)))] };
        Assert.True(catalog.IsOfficial(new(raw)));
        var padded = new byte[QuestExtdataSet.QuestSize];
        raw.CopyTo(padded, 0);
        padded[^1] = 42;
        Assert.True(catalog.IsOfficial(new(padded)));
        padded[0xA8] = 1;
        Assert.False(catalog.IsOfficial(new(padded)));
        Assert.False(catalog.IsOfficial(new(SyntheticQuest(60014))));
        Assert.False(new QuestReferenceCatalog().IsOfficial(new(raw)));
    }

    [Fact] public void BundledQuestReferenceMetadataHasVerifiedIdsAndSecondSpriteBlock()
    {
        var catalog = QuestReferenceCatalog.Load(Path.Combine(AppContext.BaseDirectory, "assets", "quests", "reference.json"));
        Assert.Equal(108, catalog.OfficialQuests.Length);
        Assert.Equal(108, catalog.OfficialQuests.Select(q => q.Id).Distinct().Count());
        Assert.All(catalog.OfficialQuests, q => { Assert.InRange(q.Length, 0x100, 0x2000); Assert.Equal(64, q.Sha256.Length); });
        Assert.Equal("Rathian", catalog.MonsterNames[1]);
        Assert.Equal("Arena", catalog.MapNames[11]);
        Assert.Equal(4, catalog.MonsterIcons[2]);
        Assert.Equal(new QuestSpritePosition(256, 0), catalog.SpritePositions[99]);
        Assert.Equal(new QuestSpritePosition(36, 0), catalog.SpritePositions[1]);
    }

    [Fact] public void MibEncryptionAuthenticatesAndRoundTrips()
    {
        var quest = SyntheticQuest();
        var encrypted = QuestCrypto.EncodeMib(quest);
        Assert.Equal(quest, QuestCrypto.DecodeMib(encrypted));
        encrypted[15] ^= 1;
        Assert.Throws<InvalidDataException>(() => QuestCrypto.DecodeMib(encrypted));
    }

    [Fact] public void ContainerPreservesSeedPaddingAndFooter()
    {
        var decoded = new DecodedQuestContainer(new byte[QuestCrypto.PayloadSize], 0x00000010, Enumerable.Range(0, 256).Select(i => (byte)i).ToArray());
        decoded.Payload[^1] = 91;
        var encrypted = QuestCrypto.EncodeContainer(decoded);
        var reopened = QuestCrypto.DecodeContainer(encrypted);
        Assert.Equal(decoded, reopened with { Payload = decoded.Payload, Footer = decoded.Footer });
        Assert.Equal(decoded.Payload, reopened.Payload);
        Assert.Equal(decoded.Footer, reopened.Footer);
        Assert.Equal(encrypted, QuestCrypto.EncodeContainer(reopened));
        encrypted[32] ^= 1;
        Assert.Throws<InvalidDataException>(() => QuestCrypto.DecodeContainer(encrypted));
    }

    [Theory]
    [InlineData(60000, 1)] [InlineData(60999, 1)] [InlineData(61000, 4)] [InlineData(61999, 4)] [InlineData(62000, 5)] [InlineData(62999, 5)]
    public void InstallRoutesByCategoryAndTouchesExactlyOneSlot(ushort id, int file)
    {
        var set = EmptySet();
        var patch = set.PlanInstall(new(SyntheticQuest(id)));
        Assert.Equal(file, patch.FileNumber);
        var before = QuestCrypto.DecodeContainer(File.ReadAllBytes(Path.Combine(_folder, $"quest{file}"))).Payload;
        var after = QuestCrypto.DecodeContainer(patch.ToArray()).Payload;
        Assert.Equal(before.AsSpan(QuestExtdataSet.SlotSize).ToArray(), after.AsSpan(QuestExtdataSet.SlotSize).ToArray());
        var backup = set.Commit(patch);
        Assert.Equal(5, Directory.GetFiles(backup, "quest?").Length);
        var reopened = QuestExtdataSet.Load(_folder);
        Assert.Single(reopened.Quests);
        Assert.Equal(id, reopened.Quests[0].Id);
        Assert.Throws<InvalidOperationException>(() => reopened.PlanInstall(new(SyntheticQuest(id))));
        var removal = reopened.PlanRemove(id);
        reopened.Commit(removal);
        Assert.Empty(QuestExtdataSet.Load(_folder).Quests);
    }

    [Fact] public void CommitBlocksStaleSnapshotsAndForeignPatches()
    {
        var set = EmptySet();
        var other = QuestExtdataSet.Load(_folder);
        var patch = set.PlanInstall(new(SyntheticQuest()));
        Assert.Throws<InvalidOperationException>(() => other.Commit(patch));
        var changed = File.ReadAllBytes(Path.Combine(_folder, "quest5"));
        changed[^1] = 4;
        File.WriteAllBytes(Path.Combine(_folder, "quest5"), changed);
        Assert.Throws<IOException>(() => set.Commit(patch));
        Assert.Empty(QuestExtdataSet.Load(_folder).Quests);
        Assert.False(Directory.Exists(Path.Combine(_folder, "Classic MH Quest Backups")));
    }

    [Fact] public void PatchChangesOnlyDeclaredFieldsAndRejectsOverflow()
    {
        var bytes = SyntheticQuest();
        var quest = new QuestDocument(bytes);
        Assert.Equal("Synthetic quest", quest.Title);
        var updated = quest.Patch(new Dictionary<int, uint> { [0xC0] = 60014 }, new Dictionary<int, string> { [0x140] = "Short quest" });
        Assert.Equal((ushort)60014, updated.Id);
        Assert.Equal("Short quest", updated.Title);
        Assert.Equal(bytes.AsSpan(0, 0xC0).ToArray(), updated.ToArray().AsSpan(0, 0xC0).ToArray());
        Assert.Throws<InvalidDataException>(() => quest.Patch(new Dictionary<int, uint> { [0xC0] = 70000 }, new Dictionary<int, string>()));
        Assert.Throws<InvalidDataException>(() => quest.Patch(new Dictionary<int, uint>(), new Dictionary<int, string> { [0x140] = new('x', 100) }));
    }

    [Fact] public void InvalidPointersAndSizesAreRejected()
    {
        var bytes = SyntheticQuest();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0xBC), uint.MaxValue);
        Assert.Throws<InvalidDataException>(() => new QuestDocument(bytes));
        Assert.Throws<InvalidDataException>(() => QuestCrypto.DecodeContainer(new byte[20]));
        Assert.Throws<InvalidDataException>(() => new QuestDocument(new byte[8193]));
        var set = EmptySet();
        Assert.Throws<InvalidDataException>(() => set.PlanInstall(new(SyntheticQuest(59999))));
    }

    [Fact] public void FullEventFilesSpillToNextContainerAndFullCategoryIsRejected()
    {
        _ = EmptySet();
        for (var n = 1; n <= 3; n++)
        {
            var path = Path.Combine(_folder, $"quest{n}");
            var container = QuestCrypto.DecodeContainer(File.ReadAllBytes(path));
            for (var slot = 0; slot < 40; slot++)
            {
                var id = (ushort)(60000 + (n - 1) * 40 + slot);
                SyntheticQuest(id).CopyTo(container.Payload, slot * QuestExtdataSet.SlotSize);
                System.Text.Encoding.ASCII.GetBytes($"m{id}.mib").CopyTo(container.Payload, slot * QuestExtdataSet.SlotSize + QuestExtdataSet.QuestSize);
            }
            File.WriteAllBytes(path, QuestCrypto.EncodeContainer(container));
            var set = QuestExtdataSet.Load(_folder);
            if (n < 3) Assert.Equal(n + 1, set.PlanInstall(new(SyntheticQuest(60999))).FileNumber);
            else Assert.Throws<InvalidOperationException>(() => set.PlanInstall(new(SyntheticQuest(60999))));
        }
    }

    [Fact] public void ReplaceAndCopyPreserveOtherSlotsAndLeaveInputUntouched()
    {
        var set = EmptySet();
        set.Commit(set.PlanInstall(new(SyntheticQuest())));
        set = QuestExtdataSet.Load(_folder);
        set.Commit(set.PlanInstall(new(SyntheticQuest(60014))));
        set = QuestExtdataSet.Load(_folder);
        var source = File.ReadAllBytes(Path.Combine(_folder, "quest1"));
        var doc = new QuestDocument(SyntheticQuest()).Patch(new Dictionary<int, uint> { [0xC2] = 14 }, new Dictionary<int, string>());
        var patch = set.PlanInstall(doc, true);
        var output = Path.Combine(_folder, "..", Path.GetFileName(_folder) + "-copy");
        try
        {
            set.ExportPatchedCopy(patch, output);
            Assert.Equal(source, File.ReadAllBytes(Path.Combine(_folder, "quest1")));
            var copied = QuestExtdataSet.Load(output);
            Assert.Equal((ushort)14, copied.Quests.Single(q => q.Id == 60013).Rank);
            Assert.Equal(set.Quests.Single(q => q.Id == 60014).Document.ToArray(), copied.Quests.Single(q => q.Id == 60014).Document.ToArray());
            Assert.Throws<IOException>(() => set.ExportPatchedCopy(patch, _folder));
            Assert.Throws<IOException>(() => set.ExportPatchedCopy(patch, output));
            Assert.Throws<InvalidOperationException>(() => set.PlanInstall(new(SyntheticQuest(60123)), true));
        }
        finally { if (Directory.Exists(output)) Directory.Delete(output, true); }
    }

    [Fact] public void UnknownOccupiedSlotsAndDuplicateIdsBlockWrites()
    {
        _ = EmptySet();
        var path = Path.Combine(_folder, "quest1");
        var container = QuestCrypto.DecodeContainer(File.ReadAllBytes(path));
        container.Payload[0] = 1;
        File.WriteAllBytes(path, QuestCrypto.EncodeContainer(container));
        Assert.Throws<InvalidDataException>(() => QuestExtdataSet.Load(_folder));
        container.Payload[0] = 0;
        for (var slot = 0; slot < 2; slot++)
        {
            SyntheticQuest().CopyTo(container.Payload, slot * QuestExtdataSet.SlotSize);
            "m60013.mib"u8.CopyTo(container.Payload.AsSpan(slot * QuestExtdataSet.SlotSize + QuestExtdataSet.QuestSize));
        }
        File.WriteAllBytes(path, QuestCrypto.EncodeContainer(container));
        Assert.Throws<InvalidDataException>(() => QuestExtdataSet.Load(_folder));
    }

    [Theory]
    [InlineData("00126300", "00001263")]
    [InlineData("00126100", "00001261")]
    public void ExtdataDiscoveryPrefersTheLoadedCharactersProfile(string title, string region)
    {
        var profiles = new[] { new string('0', 32), new string('a', 32) };
        string? selected = null;
        string? expected = null;
        foreach (var id in profiles)
        {
            var profile = Path.Combine(_folder, "Nintendo 3DS", new string('1', 32), id);
            var extdata = Path.Combine(profile, "extdata", "00000000", region, "user");
            Directory.CreateDirectory(extdata);
            File.WriteAllBytes(Path.Combine(extdata, "quest1"), []);
            var save = Path.Combine(profile, "title", "00040000", title, "data", "00000001", "user1");
            Directory.CreateDirectory(Path.GetDirectoryName(save)!);
            File.WriteAllBytes(save, []);
            selected = save;
            expected = extdata;
        }
        Assert.Equal(2, QuestExtdataLocator.FindLocations(_folder).Count);
        Assert.Equal(expected, Assert.Single(QuestExtdataLocator.FindLocations(_folder, selected)));
        Assert.Equal(2, QuestExtdataLocator.FindLocations(_folder, Path.Combine(_folder, "user1")).Count);
    }

    [Fact] public async Task ConcurrentClassicMhWriterIsRejected()
    {
        var set = EmptySet();
        var patch = set.PlanInstall(new(SyntheticQuest()));
        var gateName = "Local\\ClassicMhTools.Quests." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(set.Folder.TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant())));
        using var release = new ManualResetEventSlim();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Mutex ownership is thread-affine; keep acquisition/release on one dedicated thread.
        var holder = new Thread(() =>
        {
            try
            {
                using var gate = new Mutex(false, gateName);
                if (!gate.WaitOne(TimeSpan.FromSeconds(5))) throw new IOException("Test could not acquire the quest write guard.");
                try { ready.SetResult(); release.Wait(TimeSpan.FromSeconds(10)); }
                finally { gate.ReleaseMutex(); }
                done.SetResult();
            }
            catch (Exception ex) { ready.TrySetException(ex); done.TrySetException(ex); }
        }) { IsBackground = true };
        holder.Start();
        try { await ready.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); Assert.Throws<IOException>(() => set.Commit(patch)); }
        finally { release.Set(); await done.Task; }
        Assert.Empty(QuestExtdataSet.Load(_folder).Quests);
    }

    [Fact] public void SuppliedFixturesHaveExactRoundTripsAndExpectedQuestCounts()
    {
        var root = Environment.GetEnvironmentVariable("CLASSIC_MH_QUEST_FIXTURES");
        if (string.IsNullOrWhiteSpace(root)) { Assert.Skip("Private fixture verification is opt-in via CLASSIC_MH_QUEST_FIXTURES."); return; }
        var summaries = new List<object>();
        foreach (var folder in new[] { "personal/user", "twilight" })
        {
            var set = QuestExtdataSet.Load(Path.Combine(root, folder));
            for (var n = 1; n <= 5; n++)
            {
                var file = File.ReadAllBytes(Path.Combine(set.Folder, $"quest{n}"));
                Assert.Equal(file, QuestCrypto.EncodeContainer(QuestCrypto.DecodeContainer(file)));
                if (folder == "personal/user") Assert.Equal(new[] { 40, 40, 14, 16, 18 }[n - 1], set.Quests.Count(q => q.FileNumber == n));
            }
            foreach (var quest in set.Quests)
            {
                Assert.Equal(quest.Document.ToArray(), QuestCrypto.DecodeMib(QuestCrypto.EncodeMib(quest.Document.ToArray())));
                var unchanged = quest.Document.Patch(quest.Document.NumberFields.GroupBy(f => f.Offset).ToDictionary(g => g.Key, g => g.First().Value),
                    quest.Document.TextFields.GroupBy(f => f.Offset).ToDictionary(g => g.Key, g => g.First().Value));
                Assert.Equal(quest.Document.ToArray(), unchanged.ToArray());
            }
            summaries.Add(new { folder, questCounts = Enumerable.Range(1, 5).Select(n => set.Quests.Count(q => q.FileNumber == n)).ToArray(), exactContainerRoundTrips = 5 });
            // Write only a derived copy, then exercise replace/remove with real populated bytes.
            var questToReplace = set.Quests.First(q => q.Document.TextFields.Any(f => f.Name == "Title" && f.Language == "English" && f.Capacity >= 8));
            var title = questToReplace.Document.TextFields.First(f => f.Name == "Title" && f.Language == "English");
            var patched = questToReplace.Document.Patch(new Dictionary<int, uint>(), new Dictionary<int, string> { [title.Offset] = "Test" });
            var patch = set.PlanInstall(patched, true);
            var output = Path.Combine(_folder, folder.Replace('/', '-'));
            set.ExportPatchedCopy(patch, output);
            var copy = QuestExtdataSet.Load(output);
            Assert.Equal("Test", copy.Quests.Single(q => q.Id == questToReplace.Id).Title);
            copy.Commit(copy.PlanRemove(questToReplace.Id));
            Assert.Equal(set.Quests.Count - 1, QuestExtdataSet.Load(output).Quests.Count);
            for (var n = 1; n <= 5; n++)
            {
                var original = File.ReadAllBytes(Path.Combine(set.Folder, $"quest{n}"));
                var after = File.ReadAllBytes(Path.Combine(output, $"quest{n}"));
                if (n != questToReplace.FileNumber) Assert.Equal(original, after);
                else
                {
                    var beforePayload = QuestCrypto.DecodeContainer(original).Payload;
                    var afterPayload = QuestCrypto.DecodeContainer(after).Payload;
                    var start = questToReplace.Slot * QuestExtdataSet.SlotSize;
                    Assert.Equal(beforePayload.AsSpan(0, start).ToArray(), afterPayload.AsSpan(0, start).ToArray());
                    Assert.Equal(beforePayload.AsSpan(start + QuestExtdataSet.SlotSize).ToArray(), afterPayload.AsSpan(start + QuestExtdataSet.SlotSize).ToArray());
                }
            }
        }
        var official = Directory.GetFiles(Path.Combine(root, "official-mibs"), "*.mib");
        Assert.Equal(108, official.Length);
        foreach (var path in official)
        {
            var bytes = File.ReadAllBytes(path);
            var decoded = QuestCrypto.DecodeMib(bytes);
            _ = new QuestDocument(decoded);
            Assert.Equal(bytes, QuestCrypto.EncodeMib(decoded));
        }
        var stock = official.ToDictionary(p => new QuestDocument(QuestCrypto.DecodeMib(File.ReadAllBytes(p))).Id, p => QuestCrypto.DecodeMib(File.ReadAllBytes(p)));
        var personal = QuestExtdataSet.Load(Path.Combine(root, "personal/user"));
        var stockMatches = personal.Quests.Count(q => stock.TryGetValue(q.Id, out var raw) && q.Document.ToArray().AsSpan().StartsWith(raw));
        Assert.Equal(93, stockMatches);
        var catalog = QuestReferenceCatalog.Load(Path.Combine(AppContext.BaseDirectory, "assets", "quests", "reference.json"));
        Assert.Equal(93, personal.Quests.Count(q => catalog.IsOfficial(q.Document)));
        Assert.All(official, p => Assert.True(catalog.IsOfficial(new QuestDocument(QuestCrypto.DecodeMib(File.ReadAllBytes(p))))));
        Assert.Contains(QuestExtdataSet.Load(Path.Combine(root, "twilight")).Quests, q => q.Id == 60252 && q.Title.Contains("Twilight", StringComparison.OrdinalIgnoreCase));
        var report = Environment.GetEnvironmentVariable("CLASSIC_MH_QUEST_REPORT");
        if (!string.IsNullOrWhiteSpace(report)) File.WriteAllText(report, System.Text.Json.JsonSerializer.Serialize(new
        {
            summaries, officialMibRoundTrips = official.Length, stockMatches, privateOrModified = personal.Quests.Count - stockMatches,
            derivedCopyReplaceRemoveVerified = true, inGameAcceptanceTested = false
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    public void Dispose() { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); }
}
