using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClassicMhTools.Core.CustomQuests;

public sealed class QuestObjectiveDraft
{
    public uint Type { get; set; }
    public ushort Target { get; set; }
    public ushort Quantity { get; set; }
}

public sealed class QuestMonsterDraft
{
    // 0 = large, 1 = intruder, 2 = small. Intruders and small monsters have shared stats.
    public byte Kind { get; set; }
    public ushort Chance { get; set; } = 100;
    public uint Id { get; set; } = 1;
    public ushort Picture { get; set; } = 1;
    public byte Wave { get; set; } = 1;
    public uint Quantity { get; set; } = 1;
    public byte Area { get; set; } = 1;
    public byte Infection { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public uint Rotation { get; set; }
    public ushort Size { get; set; } = 100;
    public byte Variation { get; set; }
    public byte Hp { get; set; } = 30;
    public byte Attack { get; set; } = 64;
    public byte BreakResistance { get; set; } = 54;
    public byte Stamina { get; set; }
    public byte StatusResistance { get; set; }
}

public sealed class QuestSmallConditionDraft
{
    public byte Type { get; set; }
    public ushort Target { get; set; }
    public byte Quantity { get; set; }
}

public sealed class QuestSupplyDraft
{
    public byte Box { get; set; }
    public ushort Item { get; set; } = 769;
    public ushort Quantity { get; set; } = 1;
}

public sealed class QuestRewardDraft
{
    public byte Table { get; set; }
    public ushort Item { get; set; } = 8;
    public ushort Quantity { get; set; } = 1;
    public ushort Chance { get; set; } = 100;
}

public sealed class QuestLanguageDraft
{
    public string Title { get; set; } = "New quest";
    public string MainGoal { get; set; } = "Hunt a Rathian";
    public string Failure { get; set; } = "Reward hits zero, or time expires";
    public string Description { get; set; } = "";
    public string Other { get; set; } = "";
    public string Client { get; set; } = "Hunter";
    public string SubquestGoal { get; set; } = "";
    public string[] Values() => [Title, MainGoal, Failure, Description, Other, Client, SubquestGoal];
}

// Authoring data for native Event quests only. Imported MIBs are never rebuilt through this model.
public sealed class QuestCreationDraft
{
    public const int MaxSourceSize = 1024 * 1024;
    public int FormatVersion { get; set; } = 1;
    public ushort Id { get; set; } = 60013;
    public byte Type { get; set; } = 3;
    public ushort Stars { get; set; } = 1;
    public byte Map { get; set; } = 11;
    // Authoring intent only: no verified MIB helper bit exists. Requires a separate code patch.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ExperimentalStoryHelpers { get; set; }
    public uint Time { get; set; } = 50;
    public uint Fee { get; set; } = 100;
    public uint Reward { get; set; } = 3000;
    public uint Penalty { get; set; } = 1000;
    public uint? Faints { get; set; }
    public ushort[]? PreviewArt { get; set; }
    public byte IntruderTimer { get; set; } = 2;
    public byte IntruderChance { get; set; }
    public QuestMonsterDraft SmallStats { get; set; } = new();
    public QuestMonsterDraft IntruderStats { get; set; } = new();
    public QuestSmallConditionDraft[] SmallConditions { get; set; } = [new(), new()];
    public uint SubReward { get; set; }
    public uint HrPoints { get; set; } = 100;
    public uint HrPenalty { get; set; }
    public uint HrSub { get; set; }
    public byte Requirement1 { get; set; }
    public byte Requirement2 { get; set; }
    public byte GatherRank { get; set; } = 1;
    public byte CarveRank { get; set; } = 1;
    public byte MonsterAi { get; set; } = 1;
    public byte HunterSpawn { get; set; }
    public byte Fence { get; set; } = 1;
    public byte FenceState { get; set; }
    public byte FenceUptime { get; set; }
    public byte FenceCooldown { get; set; }
    public QuestObjectiveDraft[] Objectives { get; set; } = [new() { Type = 1, Target = 1, Quantity = 1 }, new(), new()];
    public List<QuestMonsterDraft> Monsters { get; set; } = [new()];
    public List<QuestSupplyDraft> Supplies { get; set; } = [new(), new() { Item = 771 }];
    public List<QuestRewardDraft> Rewards { get; set; } = [];
    public QuestLanguageDraft[] Languages { get; set; } = Enumerable.Range(0, 5).Select(_ => new QuestLanguageDraft()).ToArray();

    public QuestCreationDraft Clone() => FromJson(ToJson());
    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
    public static QuestCreationDraft FromJson(string json)
    {
        if (json.Length > MaxSourceSize) throw new InvalidDataException("Quest authoring file is too large.");
        QuestCreationDraft draft;
        try { draft = JsonSerializer.Deserialize<QuestCreationDraft>(json) ?? throw new InvalidDataException("Empty quest authoring file."); }
        catch (JsonException ex) { throw new InvalidDataException("Invalid quest authoring JSON.", ex); }
        draft.Validate();
        return draft;
    }

    public void Validate()
    {
        void Require(bool valid, string message) { if (!valid) throw new InvalidDataException(message); }
        Require(FormatVersion == 1, "Unsupported quest authoring version.");
        Require(Id is >= 60000 and <= 60999, "New quests must use an Event ID from 60000 to 60999.");
        Require(Type is 1 or 2 or 3 or 4 or 8 or 9 or 10 or 12, "Unsupported quest type.");
        Require(Stars is >= 1 and <= 14 && Time is >= 1 and <= 255, "Stars must be 1-14 and time must be 1-255 minutes.");
        Require(Map is >= 1 and <= 21 && Map != 13, "Choose a supported fixed quest map (not Everwood).");
        Require(Penalty <= Reward, "Faint penalty cannot exceed the main reward.");
        Require(Faints == null || Faints is >= 1 and <= 99 && Reward >= Faints, "Faints must be 1-99 and no greater than the reward.");
        Require(PreviewArt == null || PreviewArt.Length == 5 && PreviewArt.All(p => p <= 123), "Expected five artwork slots (IDs 0-123).");
        Require(SmallStats != null && IntruderStats != null && SmallConditions is { Length: 2 } && SmallConditions.All(c => c != null && c.Type is 0 or 1 or 2 or 4 && (c.Type == 0 || c.Target > 0 && c.Quantity > 0)), "Check the two small monster wave conditions.");
        Require(SmallStats!.Size > 0 && IntruderStats!.Size > 0 && IntruderChance <= 100, "Shared monster sizes must be positive and intruder appearance chance must be 0-100.");
        Require(Objectives is { Length: 3 } && Objectives.All(o => o != null), "Expected two main objectives and one subquest objective.");
        foreach (var objective in Objectives!)
        {
            Require(objective.Type is 0 or 1 or 2 or 129 or 257, "Unsupported objective type.");
            Require(objective.Type == 0 ? objective.Target == 0 && objective.Quantity == 0 : objective.Target > 0 && objective.Quantity > 0,
                "Active objectives need a target and positive quantity; inactive objectives must be empty.");
        }
        Require(Objectives![0].Type != 0, "A main objective is required.");
        Require(Monsters != null && Monsters.Count <= 100 && Monsters.All(m => m != null), "At most 100 monster records are supported.");
        var large = Monsters!.Where(m => m.Kind == 0).ToArray();
        Require(!ExperimentalStoryHelpers || Map == 19 && Type == 3 && Monsters!.Count == 1 &&
            large.Length == 1 && large[0].Area == 3 && large[0].Quantity == 1 && large[0].Wave == 1,
            "The experimental Ace Hunters patch requires Battlequarters, a Hunt quest, and one large monster in area 3, wave 1 (no intruders or small monsters).");
        var unstable = Monsters!.Where(m => m.Kind == 1).ToArray();
        var waves = large.Select(m => (int)m.Wave).Distinct().Order().ToArray();
        Require(waves.SequenceEqual(Enumerable.Range(1, waves.Length)), "Monster waves must start at 1 without gaps.");
        foreach (var m in Monsters!)
        {
            Require(m.Kind <= 2 && (m.Kind == 2 ? IsSmallMonster(m.Id) : IsLargeMonster(m.Id)) && m.Picture <= 123 && m.Quantity >= 1 && m.Quantity <= (m.Kind == 2 ? 99u : 5u) && m.Area is >= 1 and <= 20 && m.Size > 0 && m.Wave >= 1 && m.Wave <= (m.Kind == 2 ? 3 : 5) && m.Chance is >= 1 and <= 100,
                "Check monster type, species, quantity, wave, spawn area, size and chance.");
            Require(float.IsFinite(m.X) && float.IsFinite(m.Y) && float.IsFinite(m.Z), "Spawn coordinates must be finite numbers.");
        }
        Require(large.Sum(m => (long)m.Quantity) <= 5, "At most five large monsters in total are supported.");
        Require(unstable.Length == 0 || large.Length == 1 && large[0].Quantity == 1 && unstable.All(m => m.Quantity == 1), "Intruders require exactly one large monster, with quantity 1. Each intruder also needs quantity 1.");
        foreach (var objective in Objectives!.Where(o => o.Type is 1 or 129 or 257))
        {
            Require(objective.Type != 129 || !IsSmallMonster(objective.Target), "Small monsters cannot be capture objectives.");
            Require(Monsters.Where(m => m.Kind != 1 && m.Id == objective.Target).Sum(m => (long)m.Quantity) >= objective.Quantity,
                "A monster objective needs enough matching monsters in the spawn list.");
        }
        Require(Supplies != null && Supplies.Count <= 160 && Supplies.All(s => s != null && s.Box < 4 && s.Item > 0 && s.Quantity > 0),
            "Supplies require a valid item, positive quantity, and box 0-3 (at most 160 entries).");
        Require(Supplies!.GroupBy(s => s.Box).All(g => g.Count() <= 40), "Each supply box supports at most 40 entries.");
        Require(Rewards != null && Rewards.Count <= 96 && Rewards.All(r => r != null && r.Table < 3 && r.Item > 0 && r.Quantity > 0 && r.Chance is >= 1 and <= 100),
            "Rewards require a valid item, positive quantity, table 0-2, and chance 1-100 (at most 96 entries).");
        Require(Languages is { Length: 5 } && Languages.All(l => l != null), "Expected five quest text languages.");
        foreach (var language in Languages!)
            foreach (var value in language.Values())
            {
                Require(value != null && !value.Contains('\0') && value.Length <= 4096, "Quest text must be non-null, without null characters, and at most 4096 UTF-16 characters per field.");
                try { _ = new UnicodeEncoding(false, false, true).GetByteCount(value!); }
                catch (EncoderFallbackException ex) { throw new InvalidDataException("Quest text contains invalid UTF-16.", ex); }
            }
        Require(Languages!.All(l => !string.IsNullOrWhiteSpace(l.Title)), "Every language needs a quest title.");
    }

    // IDs classified as large in the recovered MH4U constants, not a map/quest compatibility whitelist.
    public static bool IsLargeMonster(uint id) => id is >= 1 and <= 49 or >= 77 and <= 80 or 82 or 83 or >= 88 and <= 103
        or 105 or 107 or 108 or >= 110 and <= 117 or >= 119 and <= 122;
    public static bool IsSmallMonster(uint id) => id is >= 50 and <= 76 or 81 or >= 84 and <= 87 or 104 or 106 or 109 or 118 or 123;
    public static uint PenaltyFor(uint reward, uint faints)
    {
        if (faints is < 1 or > 99 || reward < faints) throw new InvalidDataException("Choose 1-99 faints and a reward at least as large as that count.");
        var penalty = (uint)(((ulong)reward + faints - 1) / faints);
        if (((ulong)reward + penalty - 1) / penalty != faints) throw new InvalidDataException("This reward cannot represent that faint count exactly. Increase the reward.");
        return penalty;
    }

    public QuestDocument Build()
    {
        Validate();
        var b = new QuestBuffer();
        b.Allocate(0xA0);
        b.Bytes(4, "v005"u8);
        var header = b.Allocate(0x60);
        b.Dword(0, (uint)header);
        b.Byte(header, Type);
        b.Byte(header + 1, Monsters.Any(m => m.Kind == 1) ? (byte)2 : (byte)0);
        b.Byte(header + 2, Objectives[2].Type == 0 ? (byte)0 : (byte)0x40);
        b.Dword(header + 4, Fee); b.Dword(header + 8, Reward); b.Dword(header + 12, Faints is { } faints ? PenaltyFor(Reward, faints) : Penalty);
        b.Dword(header + 16, SubReward); b.Dword(header + 20, Time); b.Dword(header + 24, 100);
        b.Word(header + 0x20, Id); b.Word(header + 0x22, Stars); b.Byte(header + 0x24, Map);
        b.Byte(header + 0x25, Requirement1); b.Byte(header + 0x26, Requirement2);
        b.Byte(header + 0x2B, (byte)(Objectives[1].Type == 0 ? 1 : 2));
        for (var i = 0; i < 3; i++)
        {
            var at = header + 0x2C + i * 8;
            b.Dword(at, Objectives[i].Type); b.Word(at + 4, Objectives[i].Target); b.Word(at + 6, Objectives[i].Quantity);
        }
        var monsters = Monsters.Where(m => m.Kind == 0).OrderBy(m => m.Wave).ToArray();
        for (var i = 0; i < 5; i++)
        {
            var m = i == 2 && Monsters.Any(m => m.Kind == 1) ? IntruderStats : i < monsters.Length ? monsters[i] : new QuestMonsterDraft();
            b.Word(0x34 + i * 8, m.Size);
            b.Bytes(0x36 + i * 8, [m.Variation, m.Hp, m.Attack, m.BreakResistance, m.Stamina, m.StatusResistance]);
            b.Word(header + 0x48 + i * 2, PreviewArt?[i] ?? (i < monsters.Length ? monsters[i].Picture : (ushort)98));
        }
        b.Word(0x5C, SmallStats.Size); b.Bytes(0x5E, [0, SmallStats.Hp, SmallStats.Attack, SmallStats.BreakResistance, SmallStats.Stamina, SmallStats.StatusResistance]);
        for (var i = 0; i < 2; i++) { var at = 0x64 + i * 8; var c = SmallConditions[i]; b.Byte(at, c.Type); b.Word(at + 4, c.Type == 0 ? (ushort)0 : c.Target); b.Byte(at + 6, c.Type == 0 ? (byte)0 : c.Quantity); b.Byte(at + 7, (byte)(i + 1)); }
        b.Dword(0x74, HrPoints); b.Dword(0x78, HrPenalty); b.Dword(0x7C, HrSub);
        b.Byte(0x80, IntruderTimer); b.Byte(0x82, IntruderChance);
        var fence = QuestCreationPolicy.HasFence(Map);
        b.Bytes(0x89, [GatherRank, CarveRank, MonsterAi, HunterSpawn, fence ? Fence : (byte)0, fence ? FenceState : (byte)0, fence ? FenceUptime : (byte)0, fence ? FenceCooldown : (byte)0]);

        var waveAddresses = new List<int>();
        foreach (var wave in monsters.GroupBy(m => m.Wave))
        {
            var entries = wave.ToArray();
            var at = b.Allocate(entries.Length * 0x28 + 9);
            waveAddresses.Add(at);
            for (var i = 0; i < entries.Length; i++)
            {
                var m = entries[i]; var p = at + i * 0x28;
                b.Dword(p, m.Id); b.Dword(p + 4, m.Quantity);
                b.Byte(p + 8, 255); b.Byte(p + 9, m.Area); b.Byte(p + 15, m.Infection);
                b.Float(p + 16, m.X); b.Float(p + 20, m.Y); b.Float(p + 24, m.Z); b.Dword(p + 32, m.Rotation);
            }
            WriteMonsterEnd(b, at + entries.Length * 0x28);
        }
        b.Dword(0x28, (uint)b.Pointers(waveAddresses));
        // Three small-monster waves, each with a terminated list of native spawn records.
        var smallGroups = new List<int>();
        for (var group = 0; group < 3; group++)
        {
            var lists = new List<int>();
            var entries = Monsters.Where(m => m.Kind == 2 && m.Wave == group + 1).ToArray();
            var at = b.Allocate(entries.Length * 0x28 + 9);
            for (var i = 0; i < entries.Length; i++) WriteMonster(b, at + i * 0x28, entries[i]);
            WriteMonsterEnd(b, at + entries.Length * 0x28); lists.Add(at);
            smallGroups.Add(b.Pointers(lists));
        }
        b.Dword(0x2C, (uint)b.Pointers(smallGroups));
        var unstable = Monsters.Where(m => m.Kind == 1).ToArray();
        var intruders = b.Allocate(unstable.Length * 0x2C + 13);
        for (var i = 0; i < unstable.Length; i++) { b.Word(intruders + i * 0x2C, unstable[i].Chance); WriteMonster(b, intruders + i * 0x2C + 4, unstable[i]); }
        b.Word(intruders + unstable.Length * 0x2C, ushort.MaxValue); WriteMonsterEnd(b, intruders + unstable.Length * 0x2C + 4);
        b.Dword(0x30, (uint)intruders);

        var boxes = new List<(byte Id, int Count, int Address)>();
        for (byte box = 0; box <= (Supplies.Count == 0 ? 0 : Supplies.Max(s => s.Box)); box++)
        {
            var entries = Supplies.Where(s => s.Box == box).ToArray();
            var at = b.Allocate(Math.Max(4, entries.Length * 4));
            for (var i = 0; i < entries.Length; i++) { b.Word(at + i * 4, entries[i].Item); b.Word(at + i * 4 + 2, entries[i].Quantity); }
            boxes.Add((box, entries.Length, at));
        }
        var supplyTable = b.Allocate((boxes.Count + 1) * 8);
        for (var i = 0; i < boxes.Count; i++)
        {
            b.Byte(supplyTable + i * 8, boxes[i].Id); b.Byte(supplyTable + i * 8 + 1, (byte)boxes[i].Count);
            b.Dword(supplyTable + i * 8 + 4, (uint)boxes[i].Address);
        }
        b.Byte(supplyTable + boxes.Count * 8, 255); b.Dword(8, (uint)supplyTable);
        for (var table = 0; table < 3; table++)
        {
            var entries = Rewards.Where(r => r.Table == table).ToArray();
            var at = b.Allocate(entries.Length * 6 + 2);
            for (var i = 0; i < entries.Length; i++)
            { b.Word(at + i * 6, entries[i].Chance); b.Word(at + i * 6 + 2, entries[i].Item); b.Word(at + i * 6 + 4, entries[i].Quantity); }
            b.Word(at + entries.Length * 6, ushort.MaxValue);
            var top = b.Allocate(16); b.Dword(top, 0x8000); b.Dword(top + 4, (uint)at); b.Dword(top + 8, ushort.MaxValue);
            b.Dword(0x1C + table * 4, (uint)top);
        }
        var interned = new Dictionary<string, int>(StringComparer.Ordinal);
        var languages = new List<int>();
        foreach (var language in Languages)
        {
            var strings = new List<int>();
            foreach (var text in language.Values())
            {
                if (!interned.TryGetValue(text, out var at))
                {
                    var bytes = new UnicodeEncoding(false, false, true).GetBytes(text);
                    at = b.Allocate(bytes.Length + 2); b.Bytes(at, bytes); interned.Add(text, at);
                }
                strings.Add(at);
            }
            languages.Add(b.Pointers(strings, false));
        }
        b.Dword(header + 0x1C, (uint)b.Pointers(languages));
        return new QuestDocument(b.ToArray());
    }

    private static void WriteMonsterEnd(QuestBuffer b, int at) { b.Dword(at, uint.MaxValue); b.Byte(at + 8, 255); }
    private static void WriteMonster(QuestBuffer b, int at, QuestMonsterDraft m)
    {
        b.Dword(at, m.Id); b.Dword(at + 4, m.Quantity); b.Byte(at + 8, 255); b.Byte(at + 9, m.Area); b.Byte(at + 15, m.Infection);
        b.Float(at + 16, m.X); b.Float(at + 20, m.Y); b.Float(at + 24, m.Z); b.Dword(at + 32, m.Rotation);
    }

    private sealed class QuestBuffer
    {
        private readonly byte[] _bytes = new byte[QuestExtdataSet.QuestSize];
        private int _length;
        public int Allocate(int size)
        {
            var at = (_length + 15) & ~15;
            if (size < 0 || size > _bytes.Length - at) throw new InvalidDataException("Quest exceeds the 8192-byte MIB limit. Reduce text or table entries.");
            _length = at + size;
            return at;
        }
        public void Byte(int at, byte value) => _bytes[at] = value;
        public void Word(int at, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(_bytes.AsSpan(at), value);
        public void Dword(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(_bytes.AsSpan(at), value);
        public void Float(int at, float value) => BinaryPrimitives.WriteSingleLittleEndian(_bytes.AsSpan(at), value);
        public void Bytes(int at, ReadOnlySpan<byte> value) => value.CopyTo(_bytes.AsSpan(at));
        public int Pointers(IReadOnlyList<int> addresses, bool terminated = true)
        {
            var at = Allocate((addresses.Count + (terminated ? 1 : 0)) * 4);
            for (var i = 0; i < addresses.Count; i++) Dword(at + i * 4, (uint)addresses[i]);
            return at;
        }
        public byte[] ToArray() => _bytes.AsSpan(0, _length).ToArray();
    }
}
