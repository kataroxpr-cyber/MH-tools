using System.IO;
using System.Text.Json;

namespace MH4UModern.Services;

public sealed class EmulatorPathSettings
{
    private readonly string _path;

    public EmulatorPathSettings(string? settingsDirectory = null)
    {
        var directory = settingsDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MH4UModern");
        _path = Path.Combine(directory, "emulator.json");
    }

    public string? Load()
    {
        try
        {
            return File.Exists(_path) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(_path))?.StoragePath : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    public void Save(string? storagePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Settings(storagePath), new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, _path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed record Settings(string? StoragePath);
}
