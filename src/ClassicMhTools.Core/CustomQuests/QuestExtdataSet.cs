using System.Security.Cryptography;
using System.Text;

namespace ClassicMhTools.Core.CustomQuests;

public sealed record InstalledQuest(int FileNumber, int Slot, string FileName, QuestDocument Document)
{
    public ushort Id => Document.Id;
    public string Title => Document.Title;
    public string Category => Document.Category;
    public ushort Rank => Document.Rank;
    public string Location => $"quest{FileNumber} / slot {Slot + 1}";
}

public sealed class QuestPatch
{
    public int FileNumber { get; }
    public int Slot { get; }
    public string Description { get; }
    internal byte[] FileBytes { get; }
    internal QuestExtdataSet Owner { get; }
    internal QuestPatch(QuestExtdataSet owner, int fileNumber, int slot, byte[] bytes, string description)
        => (Owner, FileNumber, Slot, FileBytes, Description) = (owner, fileNumber, slot, bytes, description);
    public byte[] ToArray() => (byte[])FileBytes.Clone();
}

public sealed class QuestExtdataSet
{
    public const int SlotCount = 40;
    public const int SlotSize = 0x2010;
    public const int QuestSize = 0x2000;
    private readonly byte[][] _original;
    private readonly DecodedQuestContainer[] _containers;
    public string Folder { get; }
    public IReadOnlyList<InstalledQuest> Quests { get; }
    public int FreeSlots => 200 - Quests.Count;

    private QuestExtdataSet(string folder, byte[][] original)
    {
        Folder = Path.GetFullPath(folder);
        _original = original;
        _containers = original.Select(b => QuestCrypto.DecodeContainer(b)).ToArray();
        var quests = new List<InstalledQuest>();
        for (var file = 0; file < 5; file++)
        for (var slot = 0; slot < SlotCount; slot++)
        {
            var data = _containers[file].Payload.AsSpan(slot * SlotSize, SlotSize);
            if (IsEmpty(data)) continue;
            var nameBytes = data[QuestSize..];
            var end = nameBytes.IndexOf((byte)0);
            if (end <= 0 || nameBytes[..end].ContainsAnyExceptInRange((byte)32, (byte)126))
                throw new InvalidDataException($"Unrecognized slot in quest{file + 1}, slot {slot + 1}. Writes are blocked.");
            var name = Encoding.ASCII.GetString(nameBytes[..end]);
            var quest = new QuestDocument(data[..QuestSize].ToArray());
            if (name != $"m{quest.Id}.mib" || !AllowedFiles(quest.Id).Contains(file + 1))
                throw new InvalidDataException($"Quest ID/filename/category mismatch in quest{file + 1}, slot {slot + 1}.");
            quests.Add(new(file + 1, slot, name, quest));
        }
        if (quests.GroupBy(q => q.Id).Any(g => g.Count() > 1)) throw new InvalidDataException("Duplicate installed quest IDs; writes are blocked.");
        Quests = quests;
    }

    public static QuestExtdataSet Load(string folder)
    {
        var original = Enumerable.Range(1, 5).Select(n => ReadContainerFile(Path.Combine(folder, $"quest{n}"))).ToArray();
        return new(folder, original);
    }

    public static QuestExtdataSet FromFiles(IReadOnlyList<byte[]> files)
    {
        if (files.Count != 5 || files.Any(file => file is null || file.Length != QuestCrypto.ContainerFileSize))
            throw new InvalidDataException("Expected exactly five complete quest containers.");
        return new("Extdata snapshot", files.Select(file => (byte[])file.Clone()).ToArray());
    }

    public bool MatchesFiles(IReadOnlyList<byte[]> files) => files.Count == 5 &&
        _original.Select((bytes, index) => bytes.AsSpan().SequenceEqual(files[index])).All(equal => equal);

    private static byte[] ReadContainerFile(string path)
    {
        using var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length != QuestCrypto.ContainerFileSize) throw new InvalidDataException($"{Path.GetFileName(path)} has an unsupported size.");
        var bytes = new byte[QuestCrypto.ContainerFileSize];
        file.ReadExactly(bytes);
        return bytes;
    }

    private static bool IsEmpty(ReadOnlySpan<byte> slot)
    {
        foreach (var value in slot) if (value != 0) return false;
        return true;
    }

    private static int[] AllowedFiles(int id) => QuestDocument.CategoryFor(id) switch
    {
        "Event" => [1, 2, 3], "Challenge" => [4], "Episodic" => [5],
        _ => throw new InvalidDataException("Installable quest IDs must be 60000-62999.")
    };

    public QuestPatch PlanInstall(QuestDocument document, bool replace = false)
    {
        var existing = Quests.FirstOrDefault(q => q.Id == document.Id);
        if (existing != null && !replace) throw new InvalidOperationException("That quest ID is already installed. Choose Replace explicitly.");
        if (existing == null && replace) throw new InvalidOperationException("No installed quest has that ID to replace.");
        foreach (var file in AllowedFiles(document.Id))
        for (var slot = 0; slot < SlotCount; slot++)
        {
            if (existing != null ? existing.FileNumber != file || existing.Slot != slot :
                !IsEmpty(_containers[file - 1].Payload.AsSpan(slot * SlotSize, SlotSize))) continue;
            var payload = (byte[])_containers[file - 1].Payload.Clone();
            payload.AsSpan(slot * SlotSize, SlotSize).Clear();
            document.ToArray().CopyTo(payload, slot * SlotSize);
            Encoding.ASCII.GetBytes($"m{document.Id}.mib").CopyTo(payload, slot * SlotSize + QuestSize);
            return MakePatch(file, slot, payload, $"{(replace ? "Replace" : "Install")} {document.Id}: {document.Title}");
        }
        throw new InvalidOperationException($"No free {document.Category} slots remain.");
    }

    public QuestPatch PlanRemove(ushort id)
    {
        var existing = Quests.SingleOrDefault(q => q.Id == id) ?? throw new InvalidOperationException("Quest is not installed.");
        var payload = (byte[])_containers[existing.FileNumber - 1].Payload.Clone();
        payload.AsSpan(existing.Slot * SlotSize, SlotSize).Clear();
        return MakePatch(existing.FileNumber, existing.Slot, payload, $"Remove {id}: {existing.Title}");
    }

    private QuestPatch MakePatch(int file, int slot, byte[] payload, string description)
    {
        var original = _containers[file - 1];
        var bytes = QuestCrypto.EncodeContainer(original with { Payload = payload });
        if (!QuestCrypto.DecodeContainer(bytes).Payload.AsSpan().SequenceEqual(payload)) throw new InvalidDataException("Quest patch failed its verification.");
        return new(this, file, slot, bytes, description);
    }

    // One atomic file replacement; every member of the five-file set is backed up first.
    public string Commit(QuestPatch patch)
    {
        if (!ReferenceEquals(patch.Owner, this)) throw new InvalidOperationException("Patch belongs to a different extdata snapshot.");
        var gateName = "Local\\ClassicMhTools.Quests." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Folder.TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant())));
        using var gate = new Mutex(false, gateName);
        bool acquired;
        try { acquired = gate.WaitOne(0); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) throw new IOException("Another Classic MH Tools instance is writing this quest folder. Retry after reloading.");
        var target = Path.Combine(Folder, $"quest{patch.FileNumber}");
        var staging = Path.Combine(Folder, ".classic-mh-quest-" + Guid.NewGuid().ToString("N") + ".tmp");
        var backup = Path.Combine(Folder, "Classic MH Quest Backups", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..8]);
        var streams = new List<FileStream>();
        try
        {
            for (var i = 0; i < 5; i++)
            {
                var stream = File.Open(Path.Combine(Folder, $"quest{i + 1}"), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                streams.Add(stream);
                var current = new byte[_original[i].Length];
                if (stream.Length != current.Length) throw new IOException("Extdata changed since loading. Reload before editing.");
                stream.ReadExactly(current);
                if (!current.AsSpan().SequenceEqual(_original[i])) throw new IOException("Extdata changed since loading. Reload before editing.");
            }
            Directory.CreateDirectory(backup);
            for (var i = 0; i < 5; i++) File.WriteAllBytes(Path.Combine(backup, $"quest{i + 1}"), _original[i]);
            File.WriteAllText(Path.Combine(backup, "operation.txt"), patch.Description + Environment.NewLine +
                $"quest{patch.FileNumber}, slot {patch.Slot + 1}" + Environment.NewLine +
                "Original SHA-256: " + Convert.ToHexString(SHA256.HashData(_original[patch.FileNumber - 1])));
            using (var file = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(patch.FileBytes);
                file.Flush(true);
            }
            File.Replace(staging, target, null);
            return backup;
        }
        finally
        {
            try
            {
                foreach (var stream in streams) stream.Dispose();
                if (File.Exists(staging)) File.Delete(staging);
            }
            finally { gate.ReleaseMutex(); }
        }
    }

    public void ExportPatchedCopy(QuestPatch patch, string destination)
    {
        if (!ReferenceEquals(patch.Owner, this)) throw new InvalidOperationException("Patch belongs to another snapshot.");
        var dest = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar);
        var source = Folder.TrimEnd(Path.DirectorySeparatorChar);
        if (dest.Equals(source, StringComparison.OrdinalIgnoreCase) || dest.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Choose a separate, empty output folder, not the loaded extdata folder.");
        Directory.CreateDirectory(dest);
        if (Directory.EnumerateFileSystemEntries(dest).Any()) throw new IOException("Patched-copy output folder must be empty.");
        for (var i = 1; i <= 5; i++) File.WriteAllBytes(Path.Combine(dest, $"quest{i}"), i == patch.FileNumber ? patch.FileBytes : _original[i - 1]);
        _ = Load(dest);
    }
}
