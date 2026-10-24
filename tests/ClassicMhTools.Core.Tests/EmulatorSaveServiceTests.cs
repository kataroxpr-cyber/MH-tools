using MH4UModern.Models;
using MH4UModern.Services;

namespace ClassicMhTools.Core.Tests;

public sealed class EmulatorSaveServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "test-scratch", Guid.NewGuid().ToString("N"));
    private readonly EmulatorSaveService _service = new();
    private const string Profile = "00000000000000000000000000000000";

    public EmulatorSaveServiceTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("", "sdmc")]
    [InlineData("user", "user/sdmc")]
    public void UserAndPortableFoldersResolveToSdmc(string userFolder, string sdmc)
    {
        var storage = Directory.CreateDirectory(Path.Combine(_root, sdmc)).FullName;
        Assert.Equal(storage, _service.ResolveStoragePath(_root));
        Assert.Equal(storage, _service.ResolveStoragePath(Path.Combine(_root, userFolder)));
        Assert.Equal(storage, _service.ResolveStoragePath(storage + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void CustomStorageAndNintendo3dsFolderAreAccepted()
    {
        var storage = Directory.CreateDirectory(Path.Combine(_root, "custom card")).FullName;
        var nintendo = Directory.CreateDirectory(Path.Combine(storage, "Nintendo 3DS")).FullName;
        Assert.Equal(storage, _service.ResolveStoragePath(storage));
        Assert.Equal(storage, _service.ResolveStoragePath(nintendo));
    }

    [Fact]
    public void EmptySdmcCanBeConnectedBeforeGameCreatesSaves()
    {
        var storage = Directory.CreateDirectory(Path.Combine(_root, "sdmc")).FullName;
        var result = _service.Scan(storage);
        Assert.Equal(storage, result.StoragePath);
        Assert.Empty(result.Saves);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void MissingAndUnrelatedFoldersAreRejectedWithoutCreatingAnything()
    {
        Assert.Throws<DirectoryNotFoundException>(() => _service.Scan(Path.Combine(_root, "absent")));
        Assert.Throws<InvalidDataException>(() => _service.Scan(_root));
        Assert.Throws<ArgumentException>(() => _service.ResolveStoragePath(" "));
        Assert.Empty(Directory.GetFileSystemEntries(_root));
    }

    [Fact]
    public void DetectionDeduplicatesUserAndSdmcPathsAndIgnoresInvalidCandidates()
    {
        var storage = Directory.CreateDirectory(Path.Combine(_root, "sdmc")).FullName;
        var candidates = new[] { _root, storage, storage.ToUpperInvariant(), Path.Combine(_root, "absent"), "" };
        Assert.Equal(storage, Assert.Single(_service.DetectLocations(candidates)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExistingNonStorageCandidateDoesNotAbortDetection(bool invalidFirst)
    {
        var azahar = Directory.CreateDirectory(Path.Combine(_root, "Roaming", "Azahar")).FullName;
        var storage = Directory.CreateDirectory(Path.Combine(azahar, "sdmc")).FullName;
        var application = Directory.CreateDirectory(Path.Combine(_root, "ClassicMhTools")).FullName;
        var candidates = invalidFirst ? new[] { application, azahar } : new[] { azahar, application };
        Assert.Equal(storage, Assert.Single(_service.DetectLocations(candidates)));
    }

    [Fact]
    public void OnlyExistingNonStorageCandidatesReturnNoLocationsInsteadOfThrowing()
        => Assert.Empty(_service.DetectLocations(new[] { _root }));

    [Fact]
    public void ScanFindsExactSlotsAcrossBothRegionsAndMultipleProfiles()
    {
        var storage = Path.Combine(_root, "sdmc");
        var usa = CreateSave(storage, Profile, "00126300", "user1");
        var europe = CreateSave(storage, new string('a', 32), "00126100", "user2");
        var third = CreateSave(storage, new string('a', 32), "00126100", "user3");
        var result = _service.Scan(_root);
        Assert.Equal(3, result.Saves.Count);
        Assert.Contains(result.Saves, save => save.Path == usa && save.Region == "USA");
        Assert.Contains(result.Saves, save => save.Path == europe && save.Region == "Europe");
        Assert.Contains(result.Saves, save => save.Path == third && save.FileName == "user3");
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void ScanExcludesOtherGamesUpdatesBackupsAndNonProfileFolders()
    {
        var storage = Path.Combine(_root, "sdmc");
        var expected = CreateSave(storage, Profile, "00126300", "user1");
        CreateSave(storage, Profile, "00126300", "user1.bak");
        CreateSave(storage, Profile, "00126300", "user4");
        CreateSave(storage, Profile, "00999900", "user1");
        CreateSave(storage, "backup", "00126300", "user1");
        var backup = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(expected)!, "MH4U Modern Backups"));
        File.WriteAllBytes(Path.Combine(backup.FullName, "user2"), new byte[] { 1, 2, 3, 4 });
        var update = expected.Replace("00040000", "0004000e");
        Directory.CreateDirectory(Path.GetDirectoryName(update)!);
        File.WriteAllBytes(update, new byte[] { 1, 2, 3, 4 });
        Assert.Equal(expected, Assert.Single(_service.Scan(storage).Saves).Path);
    }

    [Fact]
    public void UnreadableSaveStillHasSlotRegionTimestampAndErrorAndIsNeverWritten()
    {
        var storage = Path.Combine(_root, "sdmc");
        var path = CreateSave(storage, Profile, "00126300", "user1");
        var original = File.ReadAllBytes(path);
        var modified = new DateTime(2026, 9, 24, 12, 30, 0);
        File.SetLastWriteTime(path, modified);
        var save = Assert.Single(_service.Scan(storage).Saves);
        Assert.False(save.IsReadable);
        Assert.Null(save.Character);
        Assert.NotNull(save.Error);
        Assert.Equal("user1", save.Name);
        Assert.Equal("Unreadable save", save.RankLabel);
        Assert.Equal(modified, save.Modified);
        Assert.Equal(4, save.Bytes);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(modified, File.GetLastWriteTime(path));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!));
    }

    [Fact]
    public void RefreshReflectsSlotsAddedAndRemovedWithoutCaching()
    {
        var storage = Path.Combine(_root, "sdmc");
        var first = CreateSave(storage, Profile, "00126300", "user1");
        Assert.Single(_service.Scan(storage).Saves);
        var second = CreateSave(storage, Profile, "00126300", "user2");
        Assert.Equal(2, _service.Scan(storage).Saves.Count);
        File.Delete(first);
        Assert.Equal(second, Assert.Single(_service.Scan(storage).Saves).Path);
    }

    [Fact]
    public void CorruptSaveCoreParseFailureDoesNotAbortScanOrModifyFiles()
    {
        var storage = Path.Combine(_root, "sdmc");
        var path = CreateSave(storage, Profile, "00126300", "user1");
        var corrupt = Enumerable.Range(0, 128).Select(value => (byte)value).ToArray();
        File.WriteAllBytes(path, corrupt);
        CreateSave(storage, Profile, "00126300", "user2");
        var result = _service.Scan(storage);
        Assert.Equal(2, result.Saves.Count);
        var save = Assert.Single(result.Saves, save => save.Path == path);
        Assert.False(save.IsReadable);
        Assert.Contains("Save details unavailable", save.Error);
        Assert.Equal(corrupt, File.ReadAllBytes(path));
    }

    [Fact]
    public void SavesAreSortedByModificationTimeNewestFirst()
    {
        var storage = Path.Combine(_root, "sdmc");
        var first = CreateSave(storage, Profile, "00126300", "user1");
        var second = CreateSave(storage, Profile, "00126300", "user2");
        File.SetLastWriteTime(first, new DateTime(2026, 9, 1));
        File.SetLastWriteTime(second, new DateTime(2026, 9, 2));
        Assert.Equal(new[] { second, first }, _service.Scan(storage).Saves.Select(save => save.Path));
    }

    [Fact]
    public void SettingsRoundTripClearAndRetainTemporarilyUnavailablePaths()
    {
        var settings = new EmulatorPathSettings(Path.Combine(_root, "settings"));
        Assert.Null(settings.Load());
        var unavailable = Path.Combine(_root, "moved emulator", "sdmc");
        settings.Save(unavailable);
        Assert.Equal(unavailable, new EmulatorPathSettings(Path.Combine(_root, "settings")).Load());
        settings.Save(null);
        Assert.Null(settings.Load());
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "settings"), "*.tmp"));
        Assert.False(Directory.Exists(unavailable));
    }

    [Theory]
    [InlineData("bad json")]
    [InlineData("null")]
    [InlineData("{}")]
    public void DamagedOrEmptySettingsDoNotPreventStartup(string json)
    {
        File.WriteAllText(Path.Combine(_root, "emulator.json"), json);
        Assert.Null(new EmulatorPathSettings(_root).Load());
    }

    [Theory]
    [InlineData(0, "Male", "\u2642")]
    [InlineData(1, "Female", "\u2640")]
    [InlineData(9, "Unknown gender", "?")]
    public void IdentityHasCorrectGenderAndHunterRank(byte gender, string label, string symbol)
    {
        var identity = new SaveIdentity("Hunter", gender, 999, 1500);
        Assert.Equal("Hunter", identity.DisplayName);
        Assert.Equal(label, identity.GenderLabel);
        Assert.Equal(symbol, identity.GenderSymbol);
        Assert.Equal("HR 999", identity.RankLabel);
        var save = new EmulatorSaveInfo("user1", "Europe", 100, new DateTime(2026, 9, 24), identity, null);
        Assert.True(save.IsReadable);
        Assert.Equal(identity.GenderSymbol, save.GenderSymbol);
        Assert.Equal(identity.RankLabel, save.RankLabel);
    }

    [Fact]
    public void BlankCharacterNameHasAnExplicitFallback()
        => Assert.Equal("Unnamed hunter", new SaveIdentity("", 0, 0, 0).DisplayName);

    private static string CreateSave(string storage, string profile, string title, string fileName)
    {
        var directory = Path.Combine(storage, "Nintendo 3DS", profile, Profile, "title", "00040000", title, "data", "00000001");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
        return path;
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
