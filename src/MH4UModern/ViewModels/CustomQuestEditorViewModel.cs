using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using ClassicMhTools.Core.CustomQuests;
using MH4UModern.Services;
using System.Windows.Media;
using System.Windows;

namespace MH4UModern.ViewModels;

public sealed class QuestNumberEditor : BindableBase, IDataErrorInfo
{
    public QuestNumberField Field { get; }
    private string _value;
    private bool _encodedInput;
    private string _encodingError = "";
    private string _initialInput = "";
    public bool IsDirty => Value != Field.Value.ToString(CultureInfo.InvariantCulture) || _encodedInput && Input.Value != _initialInput;
    public string Name => _encodedInput ? "Faints (0 = no reduction)" : Field.Name;
    public CreationNumber Input { get; private set; }
    public bool IsVisual => Field.Section == "Artwork" || Name == "Map ID" || Name.EndsWith(": monster ID") || Name.EndsWith(": item ID") || Name.EndsWith(" target ID");
    public Visibility VisualVisibility => IsVisual ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NumberInputVisibility => IsVisual ? Visibility.Collapsed : Visibility.Visible;
    public string Value { get => _value; set { if (Set(ref _value, value) && !_encodedInput) Input.Value = value; } }
    public QuestNumberEditor(QuestNumberField field, byte map)
    {
        Field = field; _value = field.Value.ToString(CultureInfo.InvariantCulture);
        IReadOnlyList<QuestChoice>? choices = field.Name switch
        {
            "Map ID" => QuestCreationChoices.Maps, "Stars" => QuestCreationChoices.Options("ranks"),
            "Requirement 1 ID" or "Requirement 2 ID" => QuestCreationChoices.Options("requirements"),
            "Quest type" => QuestCreationChoices.Types,
            "Gather rank" => QuestCreationChoices.Options("gather_ranks"), "Carve rank" => QuestCreationChoices.Options("carve_ranks"),
            "Monster AI" => QuestCreationChoices.Options("monster_ai"), "Hunter spawn area" => QuestCreationChoices.Options("spawn_areas"),
            _ when field.Section == "Artwork" => QuestCreationChoices.Previews,
            _ when field.Name.EndsWith(": monster ID") => QuestCreationChoices.AllMonsters,
            _ when field.Name.EndsWith(": item ID") => QuestCreationChoices.Items,
            _ when field.Name.EndsWith(": spawn area") => QuestCreationChoices.AreasFor(map),
            _ when field.Name.EndsWith(" type") && field.Section == "Objectives" => QuestCreationChoices.Goals,
            _ => null
        };
        Input = new(Name, field.Value, max: field.Width switch { 1 => byte.MaxValue, 2 => ushort.MaxValue, _ => uint.MaxValue }, choices: choices);
        Input.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Input.Value)) Value = Input.Value; };
    }
    public void UseFaintCount(QuestNumberEditor reward)
    {
        _encodedInput = true;
        var count = Field.Value == 0 ? 0u : (uint)(((ulong)reward.Parsed + Field.Value - 1) / Field.Value);
        Input = new(Name, count, 0, Math.Max(99, count)); _initialInput = Input.Value;
        void Calculate()
        {
            _encodingError = "";
            if (Input.Error.Length != 0 || reward.Error.Length != 0) { Raise(nameof(Value)); return; }
            try { Value = (Input.Parsed == 0 ? 0 : QuestCreationDraft.PenaltyFor(reward.Parsed, (uint)Input.Parsed)).ToString(CultureInfo.InvariantCulture); }
            catch (InvalidDataException ex) { _encodingError = ex.Message; }
            Raise(nameof(Value));
        }
        Input.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Input.Value)) Calculate(); };
        reward.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(reward.Value)) Calculate(); };
    }
    public uint Parsed => uint.Parse(Value, NumberStyles.None, CultureInfo.InvariantCulture);
    public string Error => _encodedInput && Input.Error.Length != 0 ? Input.Error : _encodingError.Length != 0 ? _encodingError : this[nameof(Value)];
    public string this[string columnName] => columnName == nameof(Value) &&
        (!uint.TryParse(Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ||
         value > (Field.Width switch { 1 => byte.MaxValue, 2 => ushort.MaxValue, _ => uint.MaxValue }))
        ? $"Enter a whole number from 0 to {(Field.Width switch { 1 => byte.MaxValue, 2 => ushort.MaxValue, _ => uint.MaxValue })}." : "";
}

public sealed class QuestTextEditor : BindableBase
{
    public QuestTextField Field { get; }
    private string _value;
    public string Name => Field.Name;
    public string Language => Field.Language;
    public int MaxLength => Field.Capacity / 2;
    public string SpaceHint => $"Existing text space: {MaxLength} UTF-16 characters";
    public string Value { get => _value; set => Set(ref _value, value); }
    public QuestTextEditor(QuestTextField field) { Field = field; _value = field.Value; }
}

public sealed record QuestEditorSection(string Name, IReadOnlyList<QuestNumberEditor> Fields);

public sealed partial class CustomQuestEditorViewModel : BindableBase, IQuestEditorLayout
{
    public QuestDocument Original { get; }
    private bool _newDraft;
    public ObservableCollection<QuestNumberEditor> Numbers { get; }
    public ObservableCollection<QuestTextEditor> Texts { get; }
    public IReadOnlyList<QuestEditorSection> Sections { get; }
    public string Title => TitleField?.Value ?? Original.Title;
    public QuestTextEditor? TitleField => Texts.FirstOrDefault(t => t.Language == "English" && t.Name == "Title");
    public string Identity => $"{Original.Id} / {Original.Category} / {Original.Rank} stars";
    public bool IsDirty => _newDraft || Numbers.Any(n => n.IsDirty) || Texts.Any(t => t.Value != t.Field.Value);
    public string State => IsDirty ? "Unsaved quest edits" : "Unchanged";

    public CustomQuestEditorViewModel(QuestDocument document, bool newDraft = false)
    {
        Original = document;
        _newDraft = newDraft;
        Numbers = new(document.NumberFields.Select(f => new QuestNumberEditor(f, document.MapId)));
        Numbers.Single(n => n.Field.Name == "Faint penalty (zenny)").UseFaintCount(Numbers.Single(n => n.Field.Name == "Main reward (zenny)"));
        Texts = new(document.TextFields.Select(f => new QuestTextEditor(f)));
        Sections = Numbers.GroupBy(n => n.Field.Section).Select(g => new QuestEditorSection(g.Key, g.ToArray())).ToArray();
        foreach (var field in Numbers.Cast<INotifyPropertyChanged>().Concat(Texts)) field.PropertyChanged += (_, _) => { Raise(nameof(IsDirty)); Raise(nameof(State)); };
        foreach (var number in Numbers) number.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(number.Value) || number.Error.Length != 0) return;
            foreach (var alias in Numbers.Where(n => n != number && n.Field.Offset == number.Field.Offset && n.Field.Width == number.Field.Width)) alias.Value = number.Value;
        };
        // Languages can share string pointers. Editing one linked value must update all aliases.
        foreach (var text in Texts) text.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(text.Value)) return;
            foreach (var alias in Texts.Where(t => t != text && t.Field.Offset == text.Field.Offset)) alias.Value = text.Value;
        };
        var map = Numbers.Single(n => n.Name == "Map ID");
        map.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(map.Value) && map.Error.Length == 0)
                foreach (var spawn in Numbers.Where(n => n.Name.EndsWith(": spawn area"))) spawn.Input.SetChoices(QuestCreationChoices.AreasFor((byte)map.Parsed));
        };
        foreach (var type in Numbers.Where(n => n.Field.Section == "Objectives" && n.Name.EndsWith(" type")))
        {
            var target = Numbers.Single(n => n.Field.Offset == type.Field.Offset + 4);
            void SetTargets() => target.Input.SetChoices(type.Value == "2" ? QuestCreationChoices.Items : QuestCreationChoices.AllMonsters);
            SetTargets(); type.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(type.Value)) SetTargets(); };
        }
        InitializeLayout();
    }

    public QuestDocument Build()
    {
        var invalid = Numbers.FirstOrDefault(n => n.Error.Length != 0);
        if (invalid != null) throw new InvalidDataException($"{invalid.Name}: {invalid.Error}");
        var numbers = Numbers.GroupBy(n => n.Field.Offset).ToDictionary(g => g.Key, g => g.First().Parsed);
        var texts = new Dictionary<int, string>();
        foreach (var field in Texts)
        {
            if (texts.TryGetValue(field.Field.Offset, out var value) && value != field.Value)
                throw new InvalidDataException("Shared language text must have the same value in every linked field.");
            texts[field.Field.Offset] = field.Value;
        }
        return Original.Patch(numbers, texts);
    }
}

public sealed record CustomQuestLibraryRow(QuestDocument Document, string Source, string Location, ushort? InstalledId = null, QuestCreationDraft? Creation = null)
{
    public bool IsEnabled { get; set; }
    public bool CanToggle { get; set; }
    public bool HasLocalCopy { get; set; }
    public bool HasPendingChanges { get; set; }
    public int RowNumber { get; set; }
    public ushort Id => Document.Id;
    public string Title => Document.Title;
    public string Category => Document.Category;
    public bool IsOfficial { get; } = QuestPresentationService.Catalog.IsOfficial(Document);
    public string Origin => IsOfficial ? "Official" : "Custom";
    public string Detail => $"{Id} | {Category} | {Document.Rank} stars | {Origin} | {(IsEnabled ? "Enabled" : Source)}" + (HasPendingChanges ? " | Local changes" : HasLocalCopy && IsEnabled ? " | Saved locally" : "");
    public IReadOnlyList<QuestMonsterIcon> Artwork { get; } = Document.PreviewArt.Where(id => id != 98).Select(QuestPresentationService.Preview).ToArray();
    public IReadOnlyList<QuestMonsterIcon> Monsters { get; } = Document.MonsterIds.Select(QuestPresentationService.Monster).ToArray();
    public string MonsterSummary => Monsters.Count == 0 ? "No large monsters" : string.Join(", ", Monsters.Select(m => m.Name));
    public string MapName => QuestPresentationService.MapName(Document.MapId);
    public ImageSource? MapIcon => QuestPresentationService.MapIcon(Document.MapId);
    public string Reward => Document.RewardZenny.ToString("N0", CultureInfo.CurrentCulture) + " z";
}
