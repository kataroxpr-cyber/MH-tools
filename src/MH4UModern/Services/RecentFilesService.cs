using System.IO;
using System.Text.Json;

namespace MH4UModern.Services;

public sealed class RecentFilesService
{
    private readonly string _settingsPath;

    public RecentFilesService(string? settingsDirectory = null)
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var folder = settingsDirectory ?? Path.Combine(root, "MH4UModern");
        Directory.CreateDirectory(folder);
        _settingsPath = Path.Combine(folder, "recent.json");
    }

    public IReadOnlyList<string> Load()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return Array.Empty<string>();
            var json = File.ReadAllText(_settingsPath);
            var files = JsonSerializer.Deserialize<List<string>>(json) ?? new();
            return files.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public void Add(string path)
    {
        var files = Load().ToList();
        files.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        files.Insert(0, path);
        files = files.Take(8).ToList();
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(files, new JsonSerializerOptions { WriteIndented = true }));
    }
}
