using MH4UModern.Services;

namespace ClassicMhTools.Core.Tests;

public sealed class BackupServiceTests
{
    [Fact]
    public void BackupAndRestoreRecoverOriginalBytesAndLeaveTheBackupIntact()
    {
        var scratch = Path.Combine(AppContext.BaseDirectory, "test-scratch", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var savePath = Path.Combine(scratch, "user1");
            var original = new byte[] { 0x10, 0x20, 0x30, 0x40 };
            File.WriteAllBytes(savePath, original);
            var service = new BackupService();
            var backup = service.CreateBackup(savePath);
            File.WriteAllBytes(savePath, new byte[] { 0xFF });
            service.RestoreBackup(backup, savePath);
            Assert.Equal(original, File.ReadAllBytes(savePath));
            Assert.Equal(original, File.ReadAllBytes(backup));
            Assert.Equal(backup, Assert.Single(service.GetBackups(savePath)).FullName);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Fact]
    public void MissingSourceCannotProduceAnEmptyBackup()
    {
        var absent = Path.Combine(AppContext.BaseDirectory, Guid.NewGuid().ToString("N"), "user1");
        Assert.Throws<FileNotFoundException>(() => new BackupService().CreateBackup(absent));
    }
}
