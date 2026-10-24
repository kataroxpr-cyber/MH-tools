using System.IO;
using MH4UModern.Models;
using mh4edit;

namespace MH4UModern.Services;

public sealed class EmulatorSaveService
{
    private static readonly (string Id, string Region)[] Titles = { ("00126300", "USA"), ("00126100", "Europe") };

    public string ResolveStoragePath(string selectedPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedPath);
        var path = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(selectedPath));
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException("The emulator folder is unavailable.");
        foreach (var candidate in new[] { path, System.IO.Path.Combine(path, "sdmc"), System.IO.Path.Combine(path, "user", "sdmc") })
        {
            if (!Directory.Exists(candidate)) continue;
            if (Directory.Exists(System.IO.Path.Combine(candidate, "Nintendo 3DS")) ||
                string.Equals(System.IO.Path.GetFileName(candidate), "sdmc", StringComparison.OrdinalIgnoreCase))
                return candidate;
        }
        if (string.Equals(System.IO.Path.GetFileName(path), "Nintendo 3DS", StringComparison.OrdinalIgnoreCase))
            return Directory.GetParent(path)!.FullName;
        throw new InvalidDataException("No SDMC folder was found. Select Azahar's user folder or its SDMC storage folder.");
    }

    public IReadOnlyList<string> DetectLocations(IEnumerable<string> candidates)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            try { found.Add(ResolveStoragePath(candidate)); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        }
        return found.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public EmulatorScanResult Scan(string selectedPath)
    {
        var storage = ResolveStoragePath(selectedPath);
        var saves = new List<EmulatorSaveInfo>();
        var warnings = new List<string>();
        var nintendo = System.IO.Path.Combine(storage, "Nintendo 3DS");
        // Only traverse the two profile levels and known MH4U title save folders, never backups/extdata.
        foreach (var id0 in ReadDirectories(nintendo, warnings).Where(IsProfileDirectory))
        foreach (var id1 in ReadDirectories(id0, warnings).Where(IsProfileDirectory))
        foreach (var title in Titles)
        {
            var data = System.IO.Path.Combine(id1, "title", "00040000", title.Id, "data", "00000001");
            if (!Directory.Exists(data)) continue;
            foreach (var slot in new[] { "user1", "user2", "user3" })
            {
                var path = System.IO.Path.Combine(data, slot);
                try
                {
                    var file = new FileInfo(path);
                    if (!file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    var length = file.Length;
                    var modified = file.LastWriteTime;
                    SaveIdentity? identity = null;
                    string? error = null;
                    try
                    {
                        if (length is < 32 or > 1024 * 1024) throw new InvalidDataException("The file size is not a supported character save.");
                        var save = new MonHunSave(path);
                        try { identity = SaveIdentity.FromCharacter(save.slot); }
                        finally { save.ms.Dispose(); }
                    }
                    catch (Exception ex) { error = "Save details unavailable: " + ex.GetBaseException().Message; }
                    saves.Add(new EmulatorSaveInfo(path, title.Region, length, modified, identity, error));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    warnings.Add($"{path}: {ex.Message}");
                }
            }
        }
        return new EmulatorScanResult(storage, saves.OrderByDescending(save => save.Modified).ThenBy(save => save.Path).ToArray(), warnings);
    }

    private static bool IsProfileDirectory(string path)
    {
        var name = System.IO.Path.GetFileName(path);
        return name.Length == 32 && name.All(Uri.IsHexDigit);
    }

    private static string[] ReadDirectories(string path, List<string> warnings)
    {
        if (!Directory.Exists(path)) return Array.Empty<string>();
        try
        {
            return Directory.GetDirectories(path, "*", new EnumerationOptions
            {
                RecurseSubdirectories = false,
                IgnoreInaccessible = false,
                AttributesToSkip = FileAttributes.ReparsePoint
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"{path}: {ex.Message}");
            return Array.Empty<string>();
        }
    }
}
