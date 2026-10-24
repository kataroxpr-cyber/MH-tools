namespace ClassicMhTools.Core.CustomQuests;

public static class QuestExtdataLocator
{
    private static readonly (string Title, string Extdata)[] Regions = [("00126300", "00001263"), ("00126100", "00001261")];

    public static IReadOnlyList<string> FindLocations(string storage, string? activeSave = null)
    {
        if (activeSave != null && File.Exists(activeSave))
        {
            var data = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(activeSave))!);
            var title = data.Parent?.Parent;
            var profile = title?.Parent?.Parent?.Parent;
            var region = Regions.FirstOrDefault(r => r.Title == title?.Name);
            if (data.Name == "00000001" && data.Parent?.Name == "data" && title?.Parent?.Name == "00040000" &&
                title.Parent.Parent?.Name == "title" && profile != null && IsProfile(profile.FullName) &&
                profile.Parent != null && IsProfile(profile.Parent.FullName) && region.Extdata != null)
            {
                var folder = QuestFolder(profile.FullName, region.Extdata);
                if (File.Exists(Path.Combine(folder, "quest1"))) return [folder];
            }
        }
        var nintendo = Path.Combine(Path.GetFullPath(storage), "Nintendo 3DS");
        if (!Directory.Exists(nintendo)) return [];
        var options = new EnumerationOptions { RecurseSubdirectories = false, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
        var found = new List<string>();
        foreach (var id0 in Directory.EnumerateDirectories(nintendo, "*", options).Where(IsProfile))
        foreach (var id1 in Directory.EnumerateDirectories(id0, "*", options).Where(IsProfile))
        foreach (var region in Regions)
        {
            var folder = QuestFolder(id1, region.Extdata);
            if (File.Exists(Path.Combine(folder, "quest1"))) found.Add(folder);
        }
        return found.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string QuestFolder(string profile, string region) => Path.Combine(profile, "extdata", "00000000", region, "user");
    private static bool IsProfile(string path) => Path.GetFileName(path) is { Length: 32 } name && name.All(Uri.IsHexDigit);
}
