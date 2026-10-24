using System.Buffers.Binary;
using System.Text;

namespace ClassicMhTools.Core.CustomQuests;

public sealed record QuestNumberField(string Section, string Name, int Offset, int Width, uint Value);
public sealed record QuestTextField(string Language, string Name, int Offset, int Capacity, string Value);

// Patch verified fields in place: unexposed tables, padding and official metadata survive unchanged.
public sealed class QuestDocument
{
    private readonly byte[] _data;
    private readonly int _header;
    private static readonly Encoding TextEncoding = new UnicodeEncoding(false, false, true);
    public int Length => _data.Length;
    public ushort Id => (ushort)Read(_header + 0x20, 2);
    public ushort Rank => (ushort)Read(_header + 0x22, 2);
    public byte MapId => (byte)Read(_header + 0x24, 1);
    public uint RewardZenny => Read(_header + 8, 4);
    public IReadOnlyList<uint> MonsterIds { get; }
    public IReadOnlyList<ushort> PreviewArt { get; }
    public string Category => CategoryFor(Id);
    public string Title => TextFields.FirstOrDefault(f => f.Name == "Title" && !string.IsNullOrWhiteSpace(f.Value))?.Value ?? $"Quest {Id}";
    public IReadOnlyList<QuestNumberField> NumberFields { get; }
    public IReadOnlyList<QuestTextField> TextFields { get; }

    public QuestDocument(byte[] data)
    {
        if (data.Length < 0x100 || data.Length > 0x2000 || !data.AsSpan(4, 4).SequenceEqual("v005"u8))
            throw new InvalidDataException("Expected an MH4U v005 quest of at most 8192 bytes.");
        _data = (byte[])data.Clone();
        _header = Pointer(0, 0x54);
        if (_header < 0xA0) throw new InvalidDataException("Invalid dynamic quest header.");
        var fields = new List<QuestNumberField>();
        void Add(string section, string name, int offset, int width = 1) => fields.Add(new(section, name, offset, width, Read(offset, width)));
        Add("General", "Quest ID", _header + 0x20, 2);
        Add("General", "Stars", _header + 0x22, 2);
        Add("General", "Map ID", _header + 0x24);
        Add("General", "Time (minutes)", _header + 0x14, 4);
        Add("General", "Entry fee", _header + 4, 4);
        Add("General", "Main reward (zenny)", _header + 8, 4);
        Add("General", "Faint penalty (zenny)", _header + 0x0C, 4);
        Add("General", "Subquest reward (zenny)", _header + 0x10, 4);
        Add("General", "HR points", 0x74, 4);
        Add("General", "HR points faint penalty", 0x78, 4);
        Add("General", "HR points subquest", 0x7C, 4);
        Add("General", "Requirement 1 ID", _header + 0x25);
        Add("General", "Requirement 2 ID", _header + 0x26);
        Add("Objectives", "Objective count", _header + 0x2B);
        foreach (var (name, offset) in new[] { ("Main 1", 0x2C), ("Main 2", 0x34), ("Subquest", 0x3C) })
        {
            Add("Objectives", name + " type", _header + offset, 4);
            Add("Objectives", name + " target ID", _header + offset + 4, 2);
            Add("Objectives", name + " quantity", _header + offset + 6, 2);
        }
        for (var i = 0; i < 5; i++) Add("Artwork", $"Preview {i + 1}", _header + 0x48 + i * 2, 2);
        PreviewArt = fields.Where(f => f.Section == "Artwork").Select(f => (ushort)f.Value).ToArray();
        for (var i = 0; i < 5; i++)
        {
            var at = 0x34 + 8 * i;
            Add("Large Monsters", $"Stats {i + 1}: size %", at, 2);
            foreach (var (label, delta) in new[] { ("size variation", 2), ("HP table index", 3), ("attack table index", 4), ("break resistance", 5), ("stamina", 6), ("status resistance", 7) })
                Add("Large Monsters", $"Stats {i + 1}: {label}", at + delta);
        }
        var monsters = Pointer(0x28, 4, true);
        if (monsters != 0)
        {
            var ended = false;
            for (var group = 0; group < 5; group++)
            {
                var ptr = Pointer(monsters + group * 4, 4, true);
                if (ptr == 0) { ended = true; break; }
                for (var i = 0; i < 5; i++)
                {
                    var at = ptr + i * 0x28;
                    if (Read(at, 4) == uint.MaxValue) break;
                    Check(at, 0x28);
                    var label = $"Wave {group + 1}, monster {i + 1}";
                    Add("Large Monsters", label + ": monster ID", at, 4);
                    Add("Large Monsters", label + ": quantity", at + 4, 4);
                    Add("Large Monsters", label + ": spawn area", at + 9);
                    Add("Large Monsters", label + ": frenzy / apex ID", at + 0x0F);
                }
            }
            if (!ended && Read(monsters + 20, 4) != 0) throw new InvalidDataException("Too many monster waves.");
        }
        AddItemTables(fields);
        Add("Advanced", "Quest type", _header);
        Add("Advanced", "Flags byte 1", _header + 1);
        Add("Advanced", "Flags byte 2", _header + 2);
        Add("Advanced", "Flags byte 3", _header + 3);
        foreach (var (name, at) in new[] { ("Intruder timer", 0x80), ("Intruder chance", 0x82), ("Gather rank", 0x89), ("Carve rank", 0x8A), ("Monster AI", 0x8B), ("Hunter spawn area", 0x8C), ("Arena fence", 0x8D), ("Fence initial state", 0x8E), ("Fence uptime", 0x8F), ("Fence cooldown", 0x90) })
            Add("Advanced", name, at);
        NumberFields = fields;
        TextFields = ReadTextFields();
        var monsterIds = fields.Where(f => f.Name.EndsWith(": monster ID", StringComparison.Ordinal))
            .Select(f => f.Value).Where(id => id != 0).ToList();
        foreach (var offset in new[] { 0x2C, 0x34, 0x3C })
        {
            var type = Read(_header + offset, 4);
            var target = Read(_header + offset + 4, 2);
            if (target != 0 && type is 1 or 129 or 257 or 516 or 8196 or 16388) monsterIds.Add(target);
        }
        var intruders = Pointer(0x30, 2, true);
        if (intruders != 0)
        {
            var terminated = false;
            for (var i = 0; i < QuestExtdataSet.QuestSize / 0x2C; i++)
            {
                var at = intruders + i * 0x2C;
                if (Read(at, 2) == ushort.MaxValue) { terminated = true; break; }
                Check(at, 0x2C);
                Add("Intruders", $"Intruder {i + 1}: chance", at, 2);
                Add("Intruders", $"Intruder {i + 1}: monster ID", at + 4, 4);
                Add("Intruders", $"Intruder {i + 1}: quantity", at + 8, 4);
                Add("Intruders", $"Intruder {i + 1}: spawn area", at + 13);
                Add("Intruders", $"Intruder {i + 1}: frenzy / apex ID", at + 19);
                var id = Read(at + 4, 4);
                if (id != 0) monsterIds.Add(id);
            }
            if (!terminated) throw new InvalidDataException("Unterminated intruder table.");
        }
        var small = Pointer(0x2C, 4, true);
        if (small != 0)
        {
            foreach (var (group, wave) in ReadPointers(small, 3).Select((p, i) => (p, i)))
                foreach (var (list, index) in ReadPointers(group, 100).Select((p, i) => (p, i)))
                {
                    var ended = false;
                    for (var i = 0; i < 100; i++)
                    {
                        var at = list + i * 0x28;
                        if (Read(at, 4) == uint.MaxValue) { ended = true; break; }
                        Check(at, 0x28);
                        var label = $"Wave {wave + 1}, list {index + 1}, small {i + 1}";
                        Add("Small Monsters", label + ": monster ID", at, 4);
                        Add("Small Monsters", label + ": quantity", at + 4, 4);
                        Add("Small Monsters", label + ": spawn area", at + 9);
                        Add("Small Monsters", label + ": frenzy / apex ID", at + 15);
                        if (Read(at, 4) != 0) monsterIds.Add(Read(at, 4));
                    }
                    if (!ended) throw new InvalidDataException("Unterminated small monster table.");
                }
        }
        for (var i = 0; i < 2; i++)
        {
            Add("Small Monsters", $"Wave {i + 2} condition", 0x64 + i * 8);
            Add("Small Monsters", $"Wave {i + 2} target ID", 0x68 + i * 8, 2);
            Add("Small Monsters", $"Wave {i + 2} target quantity", 0x6A + i * 8);
        }
        Add("Small Monsters", "Shared size %", 0x5C, 2);
        foreach (var (name, offset) in new[] { ("HP table index", 0x5F), ("attack table index", 0x60), ("break resistance", 0x61), ("stamina", 0x62), ("status resistance", 0x63) }) Add("Small Monsters", "Shared " + name, offset);
        MonsterIds = monsterIds.Distinct().ToArray();
    }

    private IEnumerable<int> ReadPointers(int table, int max)
    {
        for (var i = 0; i <= max; i++)
        {
            var ptr = Pointer(table + i * 4, 4, true);
            if (ptr == 0) yield break;
            if (i == max) throw new InvalidDataException("Too many monster table groups.");
            yield return ptr;
        }
    }

    public static string CategoryFor(int id) => id switch
    {
        >= 60000 and <= 60999 => "Event",
        >= 61000 and <= 61999 => "Challenge",
        >= 62000 and <= 62999 => "Episodic",
        _ => "Other"
    };

    public byte[] ToArray() => (byte[])_data.Clone();

    public QuestDocument Patch(IReadOnlyDictionary<int, uint> numbers, IReadOnlyDictionary<int, string> texts)
    {
        var data = ToArray();
        foreach (var field in NumberFields)
        {
            if (!numbers.TryGetValue(field.Offset, out var value)) continue;
            var max = field.Width switch { 1 => byte.MaxValue, 2 => ushort.MaxValue, _ => uint.MaxValue };
            if (value > max) throw new InvalidDataException($"{field.Name} must be between 0 and {max}.");
            switch (field.Width)
            {
                case 1: data[field.Offset] = (byte)value; break;
                case 2: BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(field.Offset), (ushort)value); break;
                default: BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(field.Offset), value); break;
            }
        }
        foreach (var field in TextFields)
        {
            if (!texts.TryGetValue(field.Offset, out var value)) continue;
            if (value.Contains('\0')) throw new InvalidDataException("Quest text cannot contain a null character.");
            var encoded = TextEncoding.GetBytes(value);
            if (encoded.Length > field.Capacity) throw new InvalidDataException($"{field.Language} {field.Name} exceeds the existing {field.Capacity / 2}-character space.");
            data.AsSpan(field.Offset, field.Capacity + 2).Clear();
            encoded.CopyTo(data, field.Offset);
        }
        return new QuestDocument(data);
    }

    private void AddItemTables(List<QuestNumberField> fields)
    {
        void Add(string section, string name, int at) => fields.Add(new(section, name, at, 2, Read(at, 2)));
        var supplies = Pointer(8, 8, true);
        if (supplies != 0)
        {
            var terminated = false;
            for (var box = 0; box < 100; box++)
            {
                var at = supplies + box * 8;
                Check(at, 8);
                if (_data[at] == 255) { terminated = true; break; }
                var count = _data[at + 1];
                var items = Pointer(at + 4, count * 4);
                for (var i = 0; i < count; i++)
                {
                    if (Read(items + i * 4, 2) == 0) break;
                    Add("Supplies", $"Box {box + 1}, item {i + 1}: item ID", items + i * 4);
                    Add("Supplies", $"Box {box + 1}, item {i + 1}: quantity", items + i * 4 + 2);
                }
            }
            if (!terminated) throw new InvalidDataException("Unterminated supply table.");
        }
        for (var reward = 0; reward < 3; reward++)
        {
            var table = Pointer(0x1C + reward * 4, 8, true);
            if (table == 0) continue;
            var terminated = false;
            for (var group = 0; group < 100; group++)
            {
                var flag = Read(table + group * 8, 4);
                if (flag is 0 or 65535) { terminated = true; break; }
                var items = Pointer(table + group * 8 + 4, 2);
                var itemsEnded = false;
                for (var i = 0; i < QuestExtdataSet.QuestSize / 6; i++)
                {
                    var at = items + i * 6;
                    if (Read(at, 2) == 65535) { itemsEnded = true; break; }
                    var label = $"{(char)('A' + reward)} / {group + 1} / {i + 1}";
                    Add("Rewards", label + ": chance", at);
                    Add("Rewards", label + ": item ID", at + 2);
                    Add("Rewards", label + ": quantity", at + 4);
                }
                if (!itemsEnded) throw new InvalidDataException("Unterminated reward items.");
            }
            if (!terminated) throw new InvalidDataException("Unterminated reward table.");
        }
    }

    private IReadOnlyList<QuestTextField> ReadTextFields()
    {
        var fields = new List<QuestTextField>();
        var table = Pointer(_header + 0x1C, 20, true);
        if (table == 0) return fields;
        string[] languages = ["English", "French", "Spanish", "German", "Italian"];
        string[] names = ["Title", "Main goal", "Failure", "Description", "Other", "Client", "Subquest goal"];
        for (var language = 0; language < 5; language++)
        {
            var strings = Pointer(table + language * 4, 28, true);
            if (strings == 0) break;
            for (var i = 0; i < 7; i++)
            {
                var at = Pointer(strings + i * 4, 2, true);
                if (at == 0) continue;
                if ((at & 1) != 0) throw new InvalidDataException("Unaligned quest text.");
                var end = at;
                while (Read(end, 2) != 0) end += 2;
                try { fields.Add(new(languages[language], names[i], at, end - at, TextEncoding.GetString(_data, at, end - at))); }
                catch (DecoderFallbackException ex) { throw new InvalidDataException("Quest text has invalid UTF-16.", ex); }
            }
        }
        return fields;
    }

    private int Pointer(int offset, int size, bool optional = false)
    {
        var value = Read(offset, 4);
        if (optional && value == 0) return 0;
        if (value > int.MaxValue) throw new InvalidDataException("Invalid quest pointer.");
        Check((int)value, size);
        return (int)value;
    }

    private uint Read(int offset, int width)
    {
        Check(offset, width);
        return width switch
        {
            1 => _data[offset],
            2 => BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(offset)),
            _ => BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(offset))
        };
    }

    private void Check(int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > _data.Length - length)
            throw new InvalidDataException("A quest table points outside the MIB data.");
    }
}
