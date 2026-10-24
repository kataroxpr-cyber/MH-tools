using System.IO;
using System.Globalization;

namespace MH4UModern.Services;

public sealed class BackupService
{
    public string CreateBackup(string savePath)
    {
        if (!File.Exists(savePath))
            throw new FileNotFoundException("The source save file no longer exists.", savePath);

        var sourceDirectory = Path.GetDirectoryName(savePath) ?? AppContext.BaseDirectory;
        var backupDirectory = Path.Combine(sourceDirectory, "MH4U Modern Backups");
        Directory.CreateDirectory(backupDirectory);

        var fileName = Path.GetFileName(savePath);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        var backupPath = Path.Combine(backupDirectory, $"{fileName}.{stamp}.bak");
        File.Copy(savePath, backupPath, overwrite: false);
        return backupPath;
    }

    public IReadOnlyList<FileInfo> GetBackups(string? savePath)
    {
        if (string.IsNullOrWhiteSpace(savePath))
            return Array.Empty<FileInfo>();

        var sourceDirectory = Path.GetDirectoryName(savePath) ?? AppContext.BaseDirectory;
        var backupDirectory = Path.Combine(sourceDirectory, "MH4U Modern Backups");
        if (!Directory.Exists(backupDirectory))
            return Array.Empty<FileInfo>();

        var saveName = Path.GetFileName(savePath);
        return Directory.EnumerateFiles(backupDirectory, $"{saveName}.*.bak")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTime)
            .ToArray();
    }

    public void RestoreBackup(string backupPath, string savePath)
    {
        if (!File.Exists(backupPath))
            throw new FileNotFoundException("The selected backup file no longer exists.", backupPath);

        File.Copy(backupPath, savePath, overwrite: true);
    }

    public string? GetBackupDirectory(string? savePath)
    {
        if (string.IsNullOrWhiteSpace(savePath)) return null;
        var sourceDirectory = Path.GetDirectoryName(savePath) ?? AppContext.BaseDirectory;
        return Path.Combine(sourceDirectory, "MH4U Modern Backups");
    }
}
