using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using ClassicMhTools.Core.CustomQuests;
using MH4UModern.Services;

namespace MH4UModern.ViewModels;

// Shared presentation, but every edit still targets the original verified field offset.
public sealed partial class CustomQuestEditorViewModel
{
    private readonly Dictionary<CreationMonster, Dictionary<string, QuestNumberEditor>> _monsterFields = [];
    private readonly Dictionary<CreationMonster, List<Action>> _monsterLinks = [];
    public IReadOnlyList<CreationNumber> General { get; private set; } = [];
    public IReadOnlyList<CreationNumber> MainFields => General.Skip(2).Take(14).ToArray();
    public IReadOnlyList<CreationNumber> FenceFields => General.Skip(16).ToArray();
    public Visibility FenceVisibility => Map?.Id == 11 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HelperVisibility => Visibility.Collapsed;
    public bool CanChooseStoryHelpers => false;
    public bool ExperimentalStoryHelpers { get => false; set { } }
    public IReadOnlyList<CreationNumber> Artwork { get; private set; } = [];
    public IReadOnlyList<CreationNumber> IntruderFields { get; private set; } = [];
    public IReadOnlyList<CreationNumber> SmallConditions { get; private set; } = [];
    public IReadOnlyList<QuestWaveCondition> WaveConditions => Enumerable.Range(2, 2).Where(w => Monsters.Any(m => m.Kind?.Id == 2 && m.Wave.Value == w.ToString()))
        .Select(w => new QuestWaveCondition(w, SmallConditions.Skip((w - 2) * 3).Take(3).ToArray())).ToArray();
    public IReadOnlyList<CreationObjective> Objectives { get; private set; } = [];
    public ObservableCollection<CreationMonster> Monsters { get; } = [];
    public ObservableCollection<CreationItem> Supplies { get; } = [];
    public ObservableCollection<CreationItem> Rewards { get; } = [];
    public IReadOnlyList<CreationLanguage> Languages { get; private set; } = [];
    public CreationNumber Stars => General[1];
    public string QuestTitle { get => Title; set { if (TitleField != null) TitleField.Value = value; } }
    public int TitleMaxLength => TitleField?.MaxLength ?? 0;
    public bool IsLayoutLocked => true;
    public bool CanAddMonster => false;
    public string PresetStatus => "";
    private QuestNumberEditor N(string name) => Numbers.Single(n => n.Field.Name == name);
    private static QuestChoice Choice(uint id, IReadOnlyList<QuestChoice> choices) => choices.FirstOrDefault(c => c.Id == id) ?? new(id, $"Unknown (ID {id})");
    private static IReadOnlyList<QuestChoice> Include(IReadOnlyList<QuestChoice> choices, QuestChoice? selected) => selected != null && !choices.Any(c => c.Id == selected.Id) ? choices.Append(selected).ToArray() : choices;
    public QuestChoice? Map
    {
        get { var id = N("Map ID").Parsed; return QuestCreationChoices.Maps.FirstOrDefault(c => c.Id == id) ?? new(id, QuestPresentationService.MapName((byte)id), QuestPresentationService.MapIcon((byte)id)); }
        set { if (value != null) N("Map ID").Value = value.Id.ToString(CultureInfo.InvariantCulture); }
    }
    public QuestChoice? Type { get => Choice(N("Quest type").Parsed, QuestCreationChoices.Types); set { if (value != null) N("Quest type").Value = value.Id.ToString(CultureInfo.InvariantCulture); } }
    public IReadOnlyList<QuestChoice> MapChoices => Include(QuestCreationChoices.Maps, Map);
    public IReadOnlyList<QuestChoice> TypeChoices => Include(QuestCreationChoices.Types, Type);

    private static Action Link(CreationNumber input, QuestNumberEditor field)
    {
        input.Value = field.Input.Value;
        PropertyChangedEventHandler fromInput = (_, e) => { if (e.PropertyName == nameof(input.Value)) field.Input.Value = input.Value; };
        PropertyChangedEventHandler fromField = (_, e) => { if (e.PropertyName == nameof(input.Value)) input.Value = field.Input.Value; };
        input.PropertyChanged += fromInput; field.Input.PropertyChanged += fromField;
        return () => { input.PropertyChanged -= fromInput; field.Input.PropertyChanged -= fromField; };
    }

    private void InitializeLayout()
    {
        string[] generalNames = ["Quest ID", "Stars", "Time (minutes)", "Entry fee", "Main reward (zenny)", "Faint penalty (zenny)", "Subquest reward (zenny)",
            "HR points", "HR points faint penalty", "HR points subquest", "Requirement 1 ID", "Requirement 2 ID", "Gather rank", "Carve rank", "Monster AI", "Hunter spawn area",
            "Arena fence", "Fence initial state", "Fence uptime", "Fence cooldown"];
        General = generalNames.Select(name => N(name).Input).ToArray();
        Artwork = Numbers.Where(n => n.Field.Section == "Artwork").Select(n => n.Input).ToArray();
        IntruderFields = [N("Intruder timer").Input, N("Intruder chance").Input];
        SmallConditions = Enumerable.Range(2, 2).SelectMany(w => new[] { N($"Wave {w} condition").Input, N($"Wave {w} target ID").Input, N($"Wave {w} target quantity").Input }).ToArray();
        foreach (var wave in Enumerable.Range(2, 2))
        {
            var at = (wave - 2) * 3;
            SmallConditions[at].SetChoices(QuestCreationChoices.Options("small_monster_conditions"));
            void Targets()
            {
                var active = SmallConditions[at].Value != "0";
                SmallConditions[at + 1].SetChoices(!active ? [new(0, "None")] : SmallConditions[at].Value == "1" ? QuestCreationChoices.AllMonsters : QuestCreationChoices.Items);
                SmallConditions[at + 1].IsEnabled = SmallConditions[at + 2].IsEnabled = active;
            }
            Targets(); SmallConditions[at].PropertyChanged += (_, e) => { if (e.PropertyName == nameof(CreationNumber.Value)) Targets(); };
        }
        Objectives = new[] { "Main 1", "Main 2", "Subquest" }.Select((prefix, index) =>
        {
            var type = N(prefix + " type"); var target = N(prefix + " target ID"); var quantity = N(prefix + " quantity");
            var objective = new CreationObjective(new[] { "Main objective", "Second main objective", "Subquest" }[index], new() { Type = type.Parsed, Target = (ushort)target.Parsed, Quantity = (ushort)quantity.Parsed });
            Link(objective.Quantity, quantity);
            objective.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(objective.Target)) target.Value = (objective.Target?.Id ?? 0).ToString(CultureInfo.InvariantCulture);
                if (e.PropertyName != nameof(objective.Type) || objective.Type == null) return;
                type.Value = objective.Type.Id.ToString(CultureInfo.InvariantCulture);
                if (index == 1) N("Objective count").Value = objective.Type.Id == 0 ? "1" : "2";
                if (index == 2) N("Flags byte 2").Value = ((N("Flags byte 2").Parsed & ~0x40u) | (objective.Type.Id == 0 ? 0 : 0x40u)).ToString(CultureInfo.InvariantCulture);
            };
            type.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(type.Value) && type.Error.Length == 0) objective.Type = Choice(type.Parsed, objective.Types); };
            target.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(target.Value) && target.Error.Length == 0) objective.Target = target.Parsed == 0 ? null : Choice(target.Parsed, objective.Targets); };
            return objective;
        }).ToArray();

        var largeIndex = 0;
        foreach (var id in Numbers.Where(n => n.Field.Name.EndsWith(": monster ID")))
        {
            var prefix = id.Field.Name[..^12];
            var kind = id.Field.Section == "Intruders" ? (byte)1 : id.Field.Section == "Small Monsters" ? (byte)2 : (byte)0;
            var wave = Regex.Match(prefix, @"^Wave (\d+)");
            var statPrefix = kind == 2 ? "Shared" : $"Stats {(kind == 1 ? 3 : ++largeIndex)}:";
            var fields = new Dictionary<string, QuestNumberEditor> { ["Monster"] = id, ["Quantity"] = N(prefix + ": quantity"), ["Spawn area"] = N(prefix + ": spawn area"), ["Frenzy / Apex"] = N(prefix + ": frenzy / apex ID") };
            if (kind == 1) fields["Intruder chance %"] = N(prefix + ": chance");
            foreach (var pair in new[] { ("Size %", "size %"), ("Size variation", "size variation"), ("HP table index", "HP table index"), ("Attack table index", "attack table index"), ("Break resistance", "break resistance"), ("Stamina index", "stamina"), ("Status resistance index", "status resistance") })
            {
                var source = Numbers.FirstOrDefault(n => n.Field.Name == statPrefix + " " + pair.Item2);
                if (source != null) fields[pair.Item1] = source;
            }
            var row = new CreationMonster(new() { Kind = kind, Id = id.Parsed, Wave = wave.Success ? byte.Parse(wave.Groups[1].Value) : (byte)1,
                Quantity = fields["Quantity"].Parsed, Area = (byte)fields["Spawn area"].Parsed, Infection = (byte)fields["Frenzy / Apex"].Parsed }, Original.MapId, true);
            Monsters.Add(row); BindMonster(row, fields);
        }
        foreach (var id in Numbers.Where(n => n.Field.Section is "Supplies" or "Rewards" && n.Field.Name.EndsWith(": item ID")))
        {
            var prefix = id.Field.Name[..^9]; var reward = id.Field.Section == "Rewards";
            var boxMatch = Regex.Match(prefix, @"^Box (\d+)");
            var box = boxMatch.Success ? checked((byte)(int.Parse(boxMatch.Groups[1].Value) - 1)) : (byte)0;
            var item = new CreationItem((ushort)id.Parsed, (ushort)N(prefix + ": quantity").Parsed, box, reward ? (byte)(prefix[0] - 'A') : (byte)0,
                reward ? (ushort)N(prefix + ": chance").Parsed : (ushort)100, true) { LocationLabel = reward ? string.Join(" / ", prefix.Split(" / ").Take(2)) : $"Box {box + 1}" };
            Link(item.Quantity, N(prefix + ": quantity")); if (reward) Link(item.Chance, N(prefix + ": chance"));
            item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(item.Item)) id.Value = item.ItemId.ToString(CultureInfo.InvariantCulture); };
            id.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(id.Value) && id.Error.Length == 0) item.Item = Choice(id.Parsed, QuestCreationChoices.Items); };
            (reward ? Rewards : Supplies).Add(item);
        }
        string[] textNames = ["Title", "Main goal", "Failure", "Description", "Other", "Client", "Subquest goal"];
        Languages = new[] { "English", "French", "Spanish", "German", "Italian" }.Select(language => new CreationLanguage(language, textNames.Select(name =>
        {
            var text = Texts.FirstOrDefault(t => t.Language == language && t.Name == name);
            var input = new CreationText(name == "Other" ? "Main monsters" : name, text?.Value ?? "") { MaxLength = text?.MaxLength ?? 0, IsEnabled = text != null };
            if (text != null)
            {
                input.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(input.Value)) text.Value = input.Value; };
                text.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(text.Value)) input.Value = text.Value; };
            }
            return input;
        }).ToArray())).ToArray();
        foreach (var field in Numbers.Cast<INotifyPropertyChanged>().Concat(Texts)) field.PropertyChanged += (_, _) =>
        {
            foreach (var name in new[] { nameof(Title), nameof(QuestTitle), nameof(Map), nameof(MapChoices), nameof(Type), nameof(TypeChoices), nameof(FenceVisibility) }) Raise(name);
        };
        N("Map ID").PropertyChanged += (_, e) => { if (e.PropertyName == nameof(QuestNumberEditor.Value) && N("Map ID").Error.Length == 0) foreach (var monster in Monsters) monster.SetMap((byte)N("Map ID").Parsed); };
    }

    private void BindMonster(CreationMonster row, Dictionary<string, QuestNumberEditor> fields)
    {
        _monsterFields[row] = fields; var links = new List<Action>(); _monsterLinks[row] = links;
        foreach (var number in row.AllNumbers)
        {
            number.IsEnabled = fields.ContainsKey(number.Name);
            if (fields.TryGetValue(number.Name, out var field)) links.Add(Link(number, field));
        }
        PropertyChangedEventHandler species = (_, e) => { if (e.PropertyName == nameof(row.Monster) && row.Monster != null) fields["Monster"].Value = row.Monster.Id.ToString(CultureInfo.InvariantCulture); };
        row.PropertyChanged += species; links.Add(() => row.PropertyChanged -= species);
        PropertyChangedEventHandler fromField = (_, e) => { if (e.PropertyName == nameof(QuestNumberEditor.Value) && fields["Monster"].Error.Length == 0) row.Monster = Choice(fields["Monster"].Parsed, row.Species); };
        fields["Monster"].PropertyChanged += fromField; links.Add(() => fields["Monster"].PropertyChanged -= fromField);
    }
    public void ApplyMonsterEdit(CreationMonster original, CreationMonster? changed)
    {
        if (changed != null && (changed.Kind?.Id != original.Kind?.Id || changed.Wave.Value != original.Wave.Value)) throw new InvalidDataException("Imported monsters must retain their existing type and wave.");
        if (changed == null || !_monsterFields.Remove(original, out var fields)) return;
        foreach (var unlink in _monsterLinks[original]) unlink(); _monsterLinks.Remove(original);
        fields["Monster"].Value = changed.Monster!.Id.ToString(CultureInfo.InvariantCulture);
        foreach (var number in changed.AllNumbers) if (fields.TryGetValue(number.Name, out var field)) field.Input.Value = number.Value;
        if (changed.Kind?.Id == 1) { IntruderFields[0].Value = changed.IntruderDelay.Value; IntruderFields[1].Value = changed.IntruderAppearance.Value; }
        Monsters[Monsters.IndexOf(original)] = changed; BindMonster(changed, fields);
    }
    public void ApplySharedStats(CreationMonster changed) { }
    public void ApplyPresets() => throw new InvalidOperationException("Imported item tables retain their existing structure.");
    public void CopyEnglish()
    {
        var changes = Texts.Where(t => t.Language != "English").Select(t => (Target: t, Source: Texts.FirstOrDefault(s => s.Language == "English" && s.Name == t.Name))).Where(pair => pair.Source != null).ToArray();
        if (changes.Any(pair => pair.Source!.Value.Length > pair.Target.MaxLength)) throw new InvalidDataException("English text exceeds an existing translated field's space.");
        foreach (var pair in changes) pair.Target.Value = pair.Source!.Value;
    }
}
