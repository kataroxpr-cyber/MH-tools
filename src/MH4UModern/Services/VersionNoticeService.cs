using System.IO;

namespace MH4UModern.Services;

public static class VersionNoticeService
{
    private static string StateFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MH4UModern", "last-seen-version.txt");

    public static bool ShouldShow(string version)
    {
        try { return !File.Exists(StateFile) || !string.Equals(File.ReadAllText(StateFile).Trim(), version, StringComparison.OrdinalIgnoreCase); }
        catch { return true; }
    }

    public static void MarkSeen(string version)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
            File.WriteAllText(StateFile, version);
        }
        catch { }
    }
}
