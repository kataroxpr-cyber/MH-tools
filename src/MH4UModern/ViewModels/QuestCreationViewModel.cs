using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows;
using ClassicMhTools.Core.CustomQuests;
using MH4UModern.Services;
using mh4edit;

namespace MH4UModern.ViewModels;

public sealed record QuestChoice(uint Id, string Name, ImageSource? Image = null, bool IsItem = false)
{
    public ImageSource? Icon => IsItem ? ItemIconService.Resolve((ushort)Id, Name) : Image;
    public bool IsOccupied => true;
    public string Detail => $"{Name} (ID {Id})";
}

public static class QuestCreationChoices
{
    private static readonly uint[][] MonsterFamilies = [[1,3,5], [2,4,6], [7,8], [9,10], [11,12,80,121], [13], [14], [15], [16],
        [17,18], [19,45,112], [20,90], [21,91], [22,92], [23,93,120], [24,83,110,111], [25,94],
        [26,95], [27,96], [28,29,97], [30], [31,82], [32], [33], [34,35], [36,37], [38,39],
        [40,41,115], [42,43,113], [44,98], [46], [47], [48,49,114], [77,78,79,117], [88,122],
        [89], [99,100,119], [101,102], [103], [105], [107,108], [116]];
    public static IReadOnlyList<QuestChoice> Monsters { get; } = QuestPresentationService.Catalog.MonsterNames
        .Where(p => QuestPresentationService.Catalog.LargeMonsterIds.Contains(p.Key))
        .Select(p => new QuestChoice(p.Key, p.Value, QuestPresentationService.Monster(p.Key).Image)).OrderBy(p => MonsterOrder(p.Id)).ToArray();
    public static IReadOnlyList<QuestChoice> SmallMonsters { get; } = QuestPresentationService.Catalog.MonsterNames
        .Where(p => QuestCreationDraft.IsSmallMonster(p.Key)).Select(p => new QuestChoice(p.Key, p.Value, QuestPresentationService.Monster(p.Key).Image)).OrderBy(p => p.Name).ToArray();
    public static IReadOnlyList<QuestChoice> AllMonsters { get; } = Monsters.Concat(SmallMonsters).ToArray();
    public static IReadOnlyList<QuestChoice> Previews { get; } = QuestPresentationService.Catalog.PreviewNames.OrderBy(p => p.Key)
        .Select(p => new QuestChoice(p.Key, p.Value, QuestPresentationService.Preview(p.Key).Image)).ToArray();
    public static IReadOnlyList<QuestChoice> MonsterKinds { get; } = [new(0, "Large"), new(1, "Unstable / intruder"), new(2, "Small")];
    public static IReadOnlyList<QuestChoice> AreasFor(byte map)
    {
        if (QuestPresentationService.Catalog.MapZones.TryGetValue(map, out var zones))
            return zones.Where(z => z.Id > 0).OrderBy(z => z.Id).Select(z => new QuestChoice(z.Id, $"Area {z.Id}")).ToArray();
        // These omitted coordinate tables have numbered zones visible in the supplied map bitmaps.
        return (map == 6 ? new[] { 1, 2 } : map is 7 or 10 or 14 or 21 ? new[] { 1 } : Enumerable.Range(1, 20))
            .Select(i => new QuestChoice((uint)i, $"Area {i}")).ToArray();
    }
    private static int MonsterOrder(uint id)
    {
        for (var group = 0; group < MonsterFamilies.Length; group++)
        {
            var variant = Array.IndexOf(MonsterFamilies[group], id);
            if (variant >= 0) return group * 10 + variant;
        }
        return 1000 + (int)id;
    }
    public static IReadOnlyList<QuestChoice> Items { get; } = MonHunItem.Names.Select((name, id) => new QuestChoice((uint)id, name, IsItem: true))
        .Where(p => p.Id > 0 && !string.IsNullOrWhiteSpace(p.Name)).OrderBy(p => p.Name).ToArray();
    public static IReadOnlyList<QuestChoice> Maps { get; } = QuestPresentationService.Catalog.MapNames.Where(p => p.Key != 0 && p.Key != 13)
        .Select(p => new QuestChoice(p.Key, p.Value, QuestPresentationService.MapIcon(p.Key))).OrderBy(p => p.Name).ToArray();
    public static IReadOnlyList<QuestChoice> Types { get; } = [new(3, "Hunt"), new(1, "Slay"), new(2, "Capture"), new(4, "Gather"),
        new(8, "Hunt all"), new(9, "Slay all"), new(10, "Capture all"), new(12, "Gather all")];
    public static IReadOnlyList<QuestChoice> Goals { get; } = [new(0, "None"), new(1, "Hunt"), new(257, "Slay"), new(129, "Capture"), new(2, "Deliver")];
    public static IReadOnlyList<QuestChoice> RewardTables { get; } = [new(0, "A"), new(1, "B"), new(2, "C")];
    public static IReadOnlyList<QuestChoice> SupplyBoxes { get; } = Enumerable.Range(0, 4).Select(i => new QuestChoice((uint)i, $"Box {i + 1}")).ToArray();
    public static IReadOnlyList<QuestChoice> Waves { get; } = Enumerable.Range(1, 5).Select(i => new QuestChoice((uint)i, $"Wave {i}")).ToArray();
    public static IReadOnlyList<QuestChoice> Areas { get; } = Enumerable.Range(1, 20).Select(i => new QuestChoice((uint)i, $"Area {i}")).ToArray();
    public static IReadOnlyList<QuestChoice> Options(string name) => QuestPresentationService.Catalog.Options[name]
        .OrderBy(p => p.Key).Select(p => new QuestChoice(p.Key, p.Value)).ToArray();
}

public sealed class CreationNumber : BindableBase, IDataErrorInfo
{
    private string _value;
    public string Name { get; }
    public double Min { get; }
    public double Max { get; private set; }
    public void SetMaximum(double max) { Max = max; Raise(nameof(Error)); Raise(nameof(Hint)); }
    public bool IsFloat { get; }
    private bool _isEnabled = true;
    public bool IsEnabled { get => _isEnabled; set => Set(ref _isEnabled, value); }
    private IReadOnlyList<QuestChoice> _choices;
    public void SetChoices(IReadOnlyList<QuestChoice> choices)
    { _choices = choices; Raise(nameof(Choices)); Raise(nameof(SelectedChoice)); }
    public IReadOnlyList<QuestChoice> Choices => uint.TryParse(Value, out var id) && HasChoices && !_choices.Any(c => c.Id == id)
        ? _choices.Append(new QuestChoice(id, $"Unknown (ID {id})")).ToArray() : _choices;
    public bool HasChoices => _choices.Count != 0;
    public bool IsToggle { get; }
    public Visibility NumberVisibility => !HasChoices ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ChoiceVisibility => HasChoices && !IsToggle ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ToggleVisibility => IsToggle ? Visibility.Visible : Visibility.Collapsed;
    public bool ToggleValue { get => Value == "1"; set => Value = value ? "1" : "0"; }
    public QuestChoice? SelectedChoice { get => Choices.FirstOrDefault(c => c.Id.ToString(CultureInfo.InvariantCulture) == Value); set { if (value != null) Value = value.Id.ToString(CultureInfo.InvariantCulture); } }
    public string Value
    {
        get => _value;
        set { if (Set(ref _value, value)) { Raise(nameof(Choices)); Raise(nameof(SelectedChoice)); Raise(nameof(ToggleValue)); Raise(nameof(Error)); Raise(nameof(Hint)); } }
    }
    public CreationNumber(string name, double value, double min = 0, double max = 255, bool isFloat = false, IReadOnlyList<QuestChoice>? choices = null, bool isToggle = false)
    { Name = name; Min = min; Max = max; IsFloat = isFloat; _choices = choices?.Where(c => c.Id >= min && c.Id <= max).ToArray() ?? []; IsToggle = isToggle; _value = value.ToString(CultureInfo.InvariantCulture); }
    public void Step(int delta) { if (Error.Length == 0) Value = Math.Clamp(Parsed + delta, Min, Max).ToString(CultureInfo.InvariantCulture); }
    public string Error => this[nameof(Value)];
    public string Hint => Error.Length > 0 ? Error : $"{Name}: {Min.ToString(CultureInfo.InvariantCulture)} to {Max.ToString(CultureInfo.InvariantCulture)}";
    public string this[string columnName] => columnName != nameof(Value) ||
        (double.TryParse(Value, IsFloat ? NumberStyles.Float : NumberStyles.None, CultureInfo.InvariantCulture, out var number) &&
        double.IsFinite(number) && number >= Min && number <= Max && (IsFloat || number == Math.Truncate(number)))
        ? "" : $"{Name}: enter {(IsFloat ? "a number" : "a whole number")} from {Min} to {Max}.";
    public double Parsed
    {
        get { if (Error.Length != 0) throw new InvalidDataException(Error); return double.Parse(Value, CultureInfo.InvariantCulture); }
    }
}

public sealed class CreationObjective : BindableBase
{
    private QuestChoice? _type;
    private QuestChoice? _target;
    public string Name { get; }
    public IReadOnlyList<QuestChoice> Types => Type != null && !QuestCreationChoices.Goals.Any(c => c.Id == Type.Id) ? QuestCreationChoices.Goals.Append(Type).ToArray() : QuestCreationChoices.Goals;
    public QuestChoice? Type
    {
        get => _type;
        set
        {
            if (!Set(ref _type, value)) return;
            Raise(nameof(Targets));
            Raise(nameof(Types));
            Target = Targets.FirstOrDefault();
            Quantity.Value = value?.Id == 0 ? "0" : "1";
        }
    }
    public QuestChoice? Target { get => _target; set => Set(ref _target, value); }
    public IReadOnlyList<QuestChoice> Targets => Type?.Id switch { 0 => [], 2 => QuestCreationChoices.Items, _ => QuestCreationChoices.AllMonsters };
    public CreationNumber Quantity { get; }
    public CreationObjective(string name, QuestObjectiveDraft draft)
    {
        Name = name;
        _type = QuestCreationChoices.Goals.FirstOrDefault(c => c.Id == draft.Type) ?? new(draft.Type, $"Unknown (ID {draft.Type})");
        _target = Targets.FirstOrDefault(c => c.Id == draft.Target) ?? (draft.Target == 0 ? null : new(draft.Target, $"Unknown (ID {draft.Target})"));
        Quantity = new("Objective quantity", draft.Quantity, 0, ushort.MaxValue);
    }
    public QuestObjectiveDraft Build() => Type == null ? throw new InvalidDataException("Select an objective type.") :
        Type.Id == 0 ? new() : new() { Type = Type.Id, Target = checked((ushort)(Target?.Id ?? throw new InvalidDataException("Select an objective target."))), Quantity = (ushort)Quantity.Parsed };
}

public sealed class CreationMonster : BindableBase
{
    private readonly QuestMonsterDraft _initial;
    public bool IsLayoutLocked { get; }
    public bool IsOccupied => true;
    private QuestChoice? _monster;
    private QuestChoice? _kind;
    public byte MapId { get; private set; }
    public QuestChoice? Kind
    {
        get => _kind;
        set
        {
            if (!Set(ref _kind, value)) return;
            if (!Species.Any(m => m.Id == Monster?.Id)) Monster = Species.FirstOrDefault();
            Wave.SetChoices(QuestCreationChoices.Waves.Take(value?.Id == 2 ? 3 : 5).ToArray());
            if (value?.Id == 2 && Wave.Parsed > 3) Wave.Value = "1";
            Quantity.Value = "1";
            Quantity.SetMaximum(value?.Id == 2 ? 99 : value?.Id == 1 ? 1 : 5);
            if (value?.Id == 1 && IntruderAppearance.Value == "0") IntruderAppearance.Value = "100";
            Raise(nameof(Species)); Raise(nameof(Numbers)); Raise(nameof(KindLabel)); Raise(nameof(WaveLabel)); Raise(nameof(FieldHeight));
        }
    }
    public string KindLabel => Kind?.Name ?? "Large";
    public string WaveLabel => Kind?.Id == 1 ? "Unstable / intruder" : $"Wave {Wave.Value}";
    public double FieldHeight => Math.Ceiling(Numbers.Count() / 3d) * 66;
    public IReadOnlyList<QuestChoice> Species => Kind?.Id == 2 ? QuestCreationChoices.SmallMonsters : QuestCreationChoices.Monsters;
    public CreationNumber Chance { get; }
    public CreationNumber IntruderDelay { get; } = new("Intruder delay (min)", 2);
    public CreationNumber IntruderAppearance { get; } = new("Intruder appearance %", 0, 0, 100);
    public QuestChoice? Monster { get => _monster; set => Set(ref _monster, value); }
    public CreationNumber Wave { get; }
    public CreationNumber Area { get; }
    public CreationNumber Quantity { get; }
    public IReadOnlyList<CreationNumber> Details { get; }
    public CreationMonster(QuestMonsterDraft m, byte map = 11, bool layoutLocked = false)
    {
        _initial = m; IsLayoutLocked = layoutLocked;
        MapId = map; _kind = QuestCreationChoices.MonsterKinds.Single(c => c.Id == m.Kind);
        _monster = Species.FirstOrDefault(c => c.Id == m.Id) ?? (layoutLocked ? QuestCreationChoices.AllMonsters.FirstOrDefault(c => c.Id == m.Id) : null) ?? new(m.Id, $"Unknown (ID {m.Id})");
        Chance = new("Intruder chance %", m.Chance, layoutLocked ? 0 : 1, layoutLocked ? ushort.MaxValue : 100);
        Wave = new("Wave", m.Wave, 1, 5, choices: QuestCreationChoices.Waves.Take(m.Kind == 2 ? 3 : 5).ToArray()) { IsEnabled = !layoutLocked };
        Wave.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Wave.Value)) Raise(nameof(WaveLabel)); };
        Area = new("Spawn area", m.Area, layoutLocked ? 0 : 1, layoutLocked ? 255 : 20, choices: QuestCreationChoices.AreasFor(map)); Quantity = new("Quantity", m.Quantity, layoutLocked ? 0 : 1, layoutLocked ? uint.MaxValue : m.Kind == 2 ? 99 : m.Kind == 1 ? 1 : 5);
        if (layoutLocked) IntruderAppearance.SetMaximum(255);
        Details = [new("X", m.X, -100000, 100000, true), new("Y", m.Y, -100000, 100000, true), new("Z", m.Z, -100000, 100000, true),
            new("Y rotation", m.Rotation, 0, uint.MaxValue), new("Size %", m.Size, layoutLocked ? 0 : 1, ushort.MaxValue), new("Size variation", m.Variation),
            new("HP table index", m.Hp), new("Attack table index", m.Attack), new("Break resistance", m.BreakResistance),
            new("Stamina index", m.Stamina), new("Status resistance index", m.StatusResistance), new("Frenzy / Apex", m.Infection, choices: QuestCreationChoices.Options("frenzy"))];
    }
    public IEnumerable<CreationNumber> Numbers => (Kind?.Id == 1 ? new[] { Chance, Area, Quantity, IntruderDelay, IntruderAppearance } : new[] { Wave, Area, Quantity })
        .Concat(Kind?.Id == 2 ? Details.Where((_, i) => i != 5) : Details).Where(n => n.IsEnabled);
    public IEnumerable<CreationNumber> AllNumbers => new[] { Wave, Area, Quantity, Chance }.Concat(Details);
    public CreationMonster Copy()
    {
        var copy = new CreationMonster(_initial, MapId, IsLayoutLocked);
        copy.Kind = Kind; copy.Monster = Monster;
        foreach (var pair in AllNumbers.Concat([IntruderDelay, IntruderAppearance]).Zip(copy.AllNumbers.Concat([copy.IntruderDelay, copy.IntruderAppearance])))
        { pair.Second.Value = pair.First.Value; pair.Second.IsEnabled = pair.First.IsEnabled; }
        return copy;
    }
    public void SetMap(byte map) { MapId = map; Area.SetChoices(QuestCreationChoices.AreasFor(map)); }
    public QuestMonsterDraft Build()
    {
        var id = Monster?.Id ?? throw new InvalidDataException("Select a large monster.");
        if (Kind?.Id == 1) { _ = IntruderDelay.Parsed; _ = IntruderAppearance.Parsed; }
        if (!IsLayoutLocked && !Species.Any(c => c.Id == id)) throw new InvalidDataException("Choose a species for this monster type.");
        if (!IsLayoutLocked && !QuestCreationChoices.AreasFor(MapId).Any(c => c.Id == Area.Parsed)) throw new InvalidDataException("Choose a spawn area available on this map.");
        double D(int i) => Details[i].IsEnabled ? Details[i].Parsed : 0;
        return new() { Id = id, Kind = (byte)(Kind?.Id ?? 0), Chance = (ushort)Chance.Parsed, Picture = (ushort)QuestPresentationService.Catalog.MonsterIcons.GetValueOrDefault(id, 0),
            Wave = (byte)Wave.Parsed, Area = (byte)Area.Parsed, Quantity = (uint)Quantity.Parsed,
            X = (float)D(0), Y = (float)D(1), Z = (float)D(2), Rotation = (uint)D(3),
            Size = (ushort)D(4), Variation = (byte)D(5), Hp = (byte)D(6), Attack = (byte)D(7),
            BreakResistance = (byte)D(8), Stamina = (byte)D(9), StatusResistance = (byte)D(10), Infection = (byte)D(11) };
    }
}

public sealed class CreationItem : BindableBase
{
    private QuestChoice? _item;
    private QuestChoice? _table;
    public QuestChoice? Item { get => _item; set { if (Set(ref _item, value)) { Raise(nameof(Name)); Raise(nameof(IconImage)); } } }
    public QuestChoice? Table { get => _table; set => Set(ref _table, value); }
    public CreationNumber Box { get; }
    public CreationNumber Quantity { get; }
    public CreationNumber Chance { get; }
    public string Name => Item?.Name ?? "Item";
    public ImageSource? IconImage => Item?.Icon;
    public string LocationLabel { get; set; } = "";
    public CreationItem(ushort item, ushort quantity, byte box = 0, byte table = 0, ushort chance = 100, bool layoutLocked = false)
    {
        _item = QuestCreationChoices.Items.FirstOrDefault(c => c.Id == item) ?? new QuestChoice(item, $"Item {item}", IsItem: true);
        _table = QuestCreationChoices.RewardTables.FirstOrDefault(c => c.Id == table);
        Box = new("Supply box", box, 0, layoutLocked ? 99 : 3, choices: QuestCreationChoices.SupplyBoxes); Quantity = new("Item quantity", quantity, layoutLocked ? 0 : 1, ushort.MaxValue); Chance = new("Chance %", chance, layoutLocked ? 0 : 1, layoutLocked ? ushort.MaxValue : 100);
    }
    public ushort ItemId => (ushort)(Item?.Id ?? throw new InvalidDataException("Select an item."));
}

public sealed class CreationText : BindableBase
{
    private string _value;
    public string Name { get; }
    public int MaxLength { get; set; } = 4096;
    public bool IsEnabled { get; set; } = true;
    public string Value { get => _value; set => Set(ref _value, value); }
    public CreationText(string name, string value) { Name = name; _value = value; }
}

public sealed record CreationLanguage(string Name, IReadOnlyList<CreationText> Fields)
{
    public IEnumerable<CreationText> EditableFields => Fields.Skip(1).Where(f => f.IsEnabled);
}

public sealed class QuestCreationViewModel : BindableBase, IQuestEditorLayout
{
    private bool _dirty;
    private readonly bool _newDraft;
    private bool _experimentalStoryHelpers;
    private QuestChoice? _map;
    private QuestChoice? _type;
    public QuestCreationDraft Original { get; }
    public QuestChoice? Map { get => _map; set { if (Set(ref _map, value)) { Raise(nameof(FenceVisibility)); Raise(nameof(CanChooseStoryHelpers)); foreach (var m in Monsters) m.SetMap((byte)(value?.Id ?? 11)); } } }
    public Visibility HelperVisibility => Visibility.Visible;
    public bool CanChooseStoryHelpers => Map?.Id == 19 || ExperimentalStoryHelpers;
    public bool ExperimentalStoryHelpers { get => _experimentalStoryHelpers; set { if (Set(ref _experimentalStoryHelpers, value)) { Raise(nameof(CanChooseStoryHelpers)); Changed(); } } }
    public QuestChoice? Type { get => _type; set => Set(ref _type, value); }
    public IReadOnlyList<CreationNumber> General { get; }
    public IReadOnlyList<QuestChoice> MapChoices => QuestCreationChoices.Maps;
    public IReadOnlyList<QuestChoice> TypeChoices => QuestCreationChoices.Types;
    public int TitleMaxLength => 256;
    public bool IsLayoutLocked => false;
    public IReadOnlyList<CreationNumber> MainFields => General.Skip(2).Take(14).ToArray();
    public IReadOnlyList<CreationNumber> FenceFields => General.Skip(16).ToArray();
    public Visibility FenceVisibility => Map != null && QuestCreationPolicy.HasFence((byte)Map.Id) ? Visibility.Visible : Visibility.Collapsed;
    public string QuestTitle { get => Title; set { foreach (var language in Languages) language.Fields[0].Value = value; } }
    public CreationNumber Stars => General[1];
    public IReadOnlyList<CreationObjective> Objectives { get; }
    public ObservableCollection<CreationMonster> Monsters { get; }
    public bool CanAddMonster => Monsters.Count < 100;
    public IReadOnlyList<CreationNumber> Artwork { get; }
    public IReadOnlyList<CreationNumber> IntruderFields { get; }
    public IReadOnlyList<CreationNumber> SmallConditions { get; }
    public IReadOnlyList<QuestWaveCondition> WaveConditions => Enumerable.Range(2, 2).Where(w => Monsters.Any(m => m.Kind?.Id == 2 && m.Wave.Value == w.ToString()))
        .Select(w => new QuestWaveCondition(w, SmallConditions.Skip((w - 2) * 3).Take(3).ToArray())).ToArray();
    private bool _autoSupplies;
    private bool _autoRewards;
    private bool _applyingPreset;
    private bool _automaticArtwork;
    private bool _updatingArtwork;
    public string PresetStatus { get; private set; } = "";
    internal string PresetSmokeState => $"supplies={_autoSupplies}, rewards={_autoRewards}, stars={General[1].Value}; supply IDs: {string.Join(',', Supplies.Select(s => s.ItemId))}; reward IDs: {string.Join(',', Rewards.Select(r => r.ItemId))}";
    public ObservableCollection<CreationItem> Supplies { get; }
    public ObservableCollection<CreationItem> Rewards { get; }
    public IReadOnlyList<CreationLanguage> Languages { get; }
    public string Title => Languages[0].Fields[0].Value;
    public string Identity => $"{General[0].Value} / Custom Event / {General[1].Value} stars";
    public bool IsDirty => _newDraft || _dirty;
    public string State => IsDirty ? "Unsaved quest draft" : "Unchanged";

    public QuestCreationViewModel(QuestCreationDraft original, bool newDraft = true)
    {
        Original = original.Clone(); _newDraft = newDraft;
        var d = Original;
        _experimentalStoryHelpers = d.ExperimentalStoryHelpers;
        _map = QuestCreationChoices.Maps.FirstOrDefault(c => c.Id == d.Map);
        _type = QuestCreationChoices.Types.FirstOrDefault(c => c.Id == d.Type);
        General = [new("Quest ID", d.Id, 60000, 60999), new("Stars", d.Stars, 1, 14, choices: QuestCreationChoices.Options("ranks")), new("Time (minutes)", d.Time, 1, 255),
            new("Entry fee", d.Fee, 0, uint.MaxValue), new("Main reward zenny", d.Reward, 0, uint.MaxValue), new("Faints", d.Faints ?? (d.Penalty == 0 ? 3 : (uint)(((ulong)d.Reward + d.Penalty - 1) / d.Penalty)), 1, 99),
            new("Subquest reward zenny", d.SubReward, 0, uint.MaxValue), new("HR points", d.HrPoints, 0, uint.MaxValue),
            new("HR faint penalty", d.HrPenalty, 0, uint.MaxValue), new("HR subquest points", d.HrSub, 0, uint.MaxValue),
            new("Requirement 1", d.Requirement1, choices: QuestCreationChoices.Options("requirements")), new("Requirement 2", d.Requirement2, choices: QuestCreationChoices.Options("requirements")), new("Gather rank", d.GatherRank, choices: QuestCreationChoices.Options("gather_ranks")),
            new("Carve rank", d.CarveRank, choices: QuestCreationChoices.Options("carve_ranks")), new("Monster AI", d.MonsterAi, choices: QuestCreationChoices.Options("monster_ai")), new("Hunter spawn", d.HunterSpawn, choices: QuestCreationChoices.Options("spawn_areas")),
            new("Arena fence", d.Fence, 0, 1, choices: QuestCreationChoices.Options("arena_fence"), isToggle: true), new("Fence initial state", d.FenceState, 0, 1, choices: QuestCreationChoices.Options("arena_fence"), isToggle: true), new("Fence uptime", d.FenceUptime), new("Fence cooldown", d.FenceCooldown)];
        Objectives = d.Objectives.Select((o, i) => new CreationObjective(new[] { "Main objective", "Second main objective", "Subquest" }[i], o)).ToArray();
        Monsters = new(d.Monsters.Select(m => new CreationMonster(m, d.Map)));
        Artwork = Enumerable.Range(0, 5).Select(i => new CreationNumber($"Artwork {i + 1}", d.PreviewArt?[i] ?? (i < d.Monsters.Count ? d.Monsters[i].Picture : 98), 0, 123, choices: QuestCreationChoices.Previews)).ToArray();
        IntruderFields = [new("Intruder delay (min)", d.IntruderTimer), new("Intruder appearance %", d.IntruderChance, 0, 100)];
        SmallConditions = d.SmallConditions.SelectMany((c, i) => new[] { new CreationNumber($"Wave {i + 2} condition", c.Type, choices: QuestCreationChoices.Options("small_monster_conditions")),
            new CreationNumber($"Wave {i + 2} target", c.Target, 0, ushort.MaxValue, choices: c.Type == 0 ? [new(0, "None")] : c.Type == 1 ? QuestCreationChoices.AllMonsters : QuestCreationChoices.Items), new CreationNumber($"Wave {i + 2} quantity", c.Quantity) }).ToArray();
        for (var i = 0; i < 2; i++)
        {
            var index = i * 3;
            SmallConditions[index + 1].IsEnabled = SmallConditions[index + 2].IsEnabled = SmallConditions[index].Value != "0";
            SmallConditions[index].PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(CreationNumber.Value)) return;
                var active = SmallConditions[index].Value != "0";
                SmallConditions[index + 1].SetChoices(!active ? [new(0, "None")] : SmallConditions[index].Value == "1" ? QuestCreationChoices.AllMonsters : QuestCreationChoices.Items);
                SmallConditions[index + 1].IsEnabled = SmallConditions[index + 2].IsEnabled = active;
                if (!active) { SmallConditions[index + 1].Value = "0"; SmallConditions[index + 2].Value = "0"; }
            };
        }
        Supplies = new(d.Supplies.Select(s => new CreationItem(s.Item, s.Quantity, s.Box)));
        Rewards = new(d.Rewards.Select(r => new CreationItem(r.Item, r.Quantity, table: r.Table, chance: r.Chance)));
        string[] names = ["Title", "Main goal", "Failure", "Description", "Main monsters", "Client", "Subquest goal"];
        Languages = d.Languages.Select((l, i) => new CreationLanguage(new[] { "English", "French", "Spanish", "German", "Italian" }[i],
            l.Values().Select((v, n) => new CreationText(names[n], v)).ToArray())).ToArray();
        foreach (var n in General) Track(n);
        foreach (var n in Artwork.Concat(IntruderFields).Concat(SmallConditions)) Track(n);
        _automaticArtwork = d.PreviewArt == null;
        foreach (var n in Artwork) n.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(CreationNumber.Value) && !_updatingArtwork) _automaticArtwork = false; };
        foreach (var o in Objectives) { Track(o); Track(o.Quantity); }
        foreach (var t in Languages.SelectMany(l => l.Fields)) Track(t);
        TrackCollection(Monsters, m =>
        {
            Track(m); foreach (var n in m.AllNumbers) Track(n);
            m.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(CreationMonster.Monster) or nameof(CreationMonster.Kind)) { RefreshArtwork(); if (_autoRewards) RefreshPresets(); } };
        });
        TrackCollection(Supplies, TrackItem); TrackCollection(Rewards, TrackItem);
        PropertyChanged += (_, e) => { if (e.PropertyName is nameof(Map) or nameof(Type)) Changed(); };
        _autoSupplies = _autoRewards = newDraft;
        Supplies.CollectionChanged += (_, _) => { if (!_applyingPreset) _autoSupplies = false; };
        Rewards.CollectionChanged += (_, _) => { if (!_applyingPreset) _autoRewards = false; };
        General[1].PropertyChanged += (_, e) => { if (e.PropertyName == nameof(CreationNumber.Value) && (_autoSupplies || _autoRewards)) RefreshPresets(); };
        Monsters.CollectionChanged += (_, _) => { RefreshArtwork(); if (_autoRewards) RefreshPresets(); };
        if (newDraft) RefreshPresets();
    }
    private void TrackItem(CreationItem item)
    {
        Track(item); Track(item.Box); Track(item.Quantity); Track(item.Chance);
        foreach (var field in new INotifyPropertyChanged[] { item, item.Box, item.Quantity, item.Chance }) field.PropertyChanged += (_, _) =>
        { if (!_applyingPreset) { if (Supplies.Contains(item)) _autoSupplies = false; if (Rewards.Contains(item)) _autoRewards = false; } };
    }
    private void Track(INotifyPropertyChanged obj) => obj.PropertyChanged += (_, _) => Changed();
    private void TrackCollection<T>(ObservableCollection<T> collection, Action<T> track)
    {
        foreach (var item in collection) track(item);
        collection.CollectionChanged += (_, e) => { if (e.NewItems != null) foreach (T item in e.NewItems) track(item); Changed(); };
    }
    private void Changed() { _dirty = true; Raise(nameof(IsDirty)); Raise(nameof(State)); Raise(nameof(Title)); Raise(nameof(QuestTitle)); Raise(nameof(Identity)); Raise(nameof(CanAddMonster)); Raise(nameof(WaveConditions)); }
    public void CopyEnglish()
    { foreach (var language in Languages.Skip(1)) for (var i = 0; i < 7; i++) language.Fields[i].Value = Languages[0].Fields[i].Value; }

    public QuestCreationDraft BuildDraft()
    {
        var d = Original.Clone();
        uint N(int i) => (uint)General[i].Parsed;
        d.Id = (ushort)N(0); d.Stars = (ushort)N(1); d.Time = N(2); d.Fee = N(3); d.Reward = N(4);
        // Preserve legacy penalties until the faint count or reward is explicitly edited.
        if (d.Faints != null || General[5].Value != InitialFaints(Original) || d.Reward != Original.Reward) { d.Faints = N(5); d.Penalty = QuestCreationDraft.PenaltyFor(d.Reward, N(5)); }
        d.SubReward = N(6); d.HrPoints = N(7); d.HrPenalty = N(8); d.HrSub = N(9); d.Requirement1 = (byte)N(10); d.Requirement2 = (byte)N(11);
        d.GatherRank = (byte)N(12); d.CarveRank = (byte)N(13); d.MonsterAi = (byte)N(14); d.HunterSpawn = (byte)N(15);
        d.Fence = (byte)N(16); d.FenceState = (byte)N(17); d.FenceUptime = (byte)N(18); d.FenceCooldown = (byte)N(19);
        d.Map = (byte)(Map?.Id ?? throw new InvalidDataException("Select a map."));
        d.ExperimentalStoryHelpers = ExperimentalStoryHelpers;
        d.Type = (byte)(Type?.Id ?? throw new InvalidDataException("Select a quest type."));
        d.Objectives = Objectives.Select(o => o.Build()).ToArray();
        d.Monsters = Monsters.Select(m => m.Build()).ToList();
        d.PreviewArt = _automaticArtwork ? null : Artwork.Select(n => (ushort)n.Parsed).ToArray();
        d.SmallStats = d.Monsters.FirstOrDefault(m => m.Kind == 2) ?? d.SmallStats;
        d.IntruderStats = d.Monsters.FirstOrDefault(m => m.Kind == 1) ?? d.IntruderStats;
        d.IntruderTimer = (byte)IntruderFields[0].Parsed; d.IntruderChance = (byte)IntruderFields[1].Parsed;
        d.SmallConditions = Enumerable.Range(0, 2).Select(i => new QuestSmallConditionDraft { Type = (byte)SmallConditions[i * 3].Parsed, Target = (ushort)SmallConditions[i * 3 + 1].Parsed, Quantity = (byte)SmallConditions[i * 3 + 2].Parsed }).ToArray();
        d.Supplies = Supplies.Select(s => new QuestSupplyDraft { Box = (byte)s.Box.Parsed, Item = s.ItemId, Quantity = (ushort)s.Quantity.Parsed }).ToList();
        d.Rewards = Rewards.Select(r => new QuestRewardDraft { Table = (byte)(r.Table?.Id ?? throw new InvalidDataException("Select a reward table.")),
            Item = r.ItemId, Quantity = (ushort)r.Quantity.Parsed, Chance = (ushort)r.Chance.Parsed }).ToList();
        d.Languages = Languages.Select(l => new QuestLanguageDraft { Title = l.Fields[0].Value, MainGoal = l.Fields[1].Value, Failure = l.Fields[2].Value,
            Description = l.Fields[3].Value, Other = string.IsNullOrWhiteSpace(l.Fields[4].Value) ? string.Join(", ", Monsters.Select(m => m.Monster?.Name).Distinct()) : l.Fields[4].Value, Client = l.Fields[5].Value, SubquestGoal = l.Fields[6].Value }).ToArray();
        d.Validate();
        return d;
    }
    public QuestDocument Build() => BuildDraft().Build();
    private void RefreshArtwork()
    {
        if (!_automaticArtwork) return;
        _updatingArtwork = true;
        try
        {
            var monsters = Monsters.Where(m => m.Kind?.Id == 0).OrderBy(m => m.Wave.Value).Take(5).ToArray();
            for (var i = 0; i < 5; i++) Artwork[i].Value = (i < monsters.Length ? QuestPresentationService.Catalog.MonsterIcons.GetValueOrDefault(monsters[i].Monster?.Id ?? 0, 0) : 98).ToString();
        }
        finally { _updatingArtwork = false; }
    }
    public void ApplySharedStats(CreationMonster changed)
    {
        if (changed.Kind?.Id == 0) return;
        foreach (var other in Monsters.Where(m => m != changed && m.Kind?.Id == changed.Kind?.Id))
            for (var i = 4; i <= 10; i++) other.Details[i].Value = changed.Details[i].Value;
    }
    public void ApplyMonsterEdit(CreationMonster original, CreationMonster? changed)
    {
        var index = Monsters.IndexOf(original);
        if (index < 0) return;
        if (changed == null) Monsters.RemoveAt(index);
        else
        {
            Monsters[index] = changed; ApplySharedStats(changed);
            if (changed.Kind?.Id == 1) { IntruderFields[0].Value = changed.IntruderDelay.Value; IntruderFields[1].Value = changed.IntruderAppearance.Value; }
        }
    }
    private static string InitialFaints(QuestCreationDraft d) => (d.Faints ?? (d.Penalty == 0 ? 3 : (uint)(((ulong)d.Reward + d.Penalty - 1) / d.Penalty))).ToString(CultureInfo.InvariantCulture);

    public void ApplyPresets() { _autoSupplies = _autoRewards = true; RefreshPresets(); }
    private void RefreshPresets()
    {
        if (General[1].Error.Length != 0) return;
        var rank = General[1].Parsed >= 8 ? (byte)3 : General[1].Parsed >= 4 ? (byte)2 : (byte)1;
        var catalog = QuestPresentationService.Catalog;
        _applyingPreset = true;
        try
        {
            if (_autoSupplies)
            {
                Supplies.Clear();
                foreach (var s in catalog.ItemPresets.FirstOrDefault(p => p.Rank == rank && p.Monster == 0)?.Supplies ?? []) Supplies.Add(new(s.Item, s.Quantity));
                General[12].Value = rank.ToString(); General[13].Value = rank.ToString();
            }
            if (_autoRewards)
            {
                var monsters = Monsters.Where(m => m.Kind?.Id != 1).Select(m => m.Monster?.Id ?? 0).Distinct().ToArray();
                var missing = new List<string>();
                foreach (var monster in monsters)
                    if (!catalog.ItemPresets.Any(p => p.Monster == monster && p.Rank == rank)) missing.Add(QuestPresentationService.Monster(monster).Name);
                IReadOnlyList<QuestRewardDraft> preset;
                try { preset = QuestPresetPolicy.Rewards(catalog, monsters, rank); }
                catch (InvalidDataException ex)
                {
                    _autoRewards = false;
                    PresetStatus = ex.Message + " Existing rewards were kept.";
                    Raise(nameof(PresetStatus));
                    return;
                }
                Rewards.Clear();
                foreach (var r in preset) Rewards.Add(new(r.Item, r.Quantity, table: r.Table, chance: r.Chance));
                PresetStatus = missing.Count == 0 ? "" : "No verified reward preset: " + string.Join(", ", missing);
                Raise(nameof(PresetStatus));
            }
        }
        finally { _applyingPreset = false; }
    }
}
