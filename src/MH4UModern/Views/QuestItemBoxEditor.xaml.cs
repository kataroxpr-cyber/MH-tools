using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MH4UModern.ViewModels;

namespace MH4UModern.Views;

public sealed record QuestItemSlot(int Slot, CreationItem? Entry, bool IsReward)
{
    public bool IsOccupied => Entry != null;
    public ImageSource? IconImage => Entry?.IconImage;
    public string Quantity => Entry?.Quantity.Value ?? "";
    public string Chance => IsReward && Entry != null ? $"{Entry.Chance.Value}%" : "";
    public string Tooltip => Entry == null ? $"Empty slot {Slot}" : $"{Entry.Name} x{Quantity}" + (IsReward ? $" ({Chance})" : "");
}

public partial class QuestItemBoxEditor : UserControl, INotifyPropertyChanged
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(nameof(Items), typeof(ObservableCollection<CreationItem>), typeof(QuestItemBoxEditor), new PropertyMetadata(null, ItemsChanged));
    public static readonly DependencyProperty IsRewardProperty = DependencyProperty.Register(nameof(IsReward), typeof(bool), typeof(QuestItemBoxEditor), new PropertyMetadata(false, KindChanged));
    public static readonly DependencyProperty IsLayoutLockedProperty = DependencyProperty.Register(nameof(IsLayoutLocked), typeof(bool), typeof(QuestItemBoxEditor), new PropertyMetadata(false, KindChanged));
    public ObservableCollection<CreationItem>? Items { get => (ObservableCollection<CreationItem>?)GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }
    public bool IsReward { get => (bool)GetValue(IsRewardProperty); set => SetValue(IsRewardProperty, value); }
    public bool IsLayoutLocked { get => (bool)GetValue(IsLayoutLockedProperty); set => SetValue(IsLayoutLockedProperty, value); }
    public Visibility StructuralVisibility => IsLayoutLocked ? Visibility.Collapsed : Visibility.Visible;
    public Visibility LockedVisibility => IsLayoutLocked ? Visibility.Visible : Visibility.Collapsed;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<QuestChoiceRequest>? PickerRequested;
    private readonly HashSet<CreationItem> _tracked = [];
    private bool _refreshing;
    private int _page;
    private uint Group => (GroupPicker.SelectedItem as QuestChoice)?.Id ?? 0;
    private List<CreationItem> GroupItems => Items?.Where(i => IsReward ? i.Table?.Id == Group : uint.TryParse(i.Box.Value, out var box) && box == Group).ToList() ?? [];
    private int Pages => Math.Max(1, (GroupItems.Count + (CanAdd ? 1 : 0) + 39) / 40);
    public IReadOnlyList<QuestChoice> Groups => IsReward ? QuestCreationChoices.RewardTables : IsLayoutLocked && Items?.Count > 0
        ? Items.Select(i => (uint)i.Box.Parsed).Distinct().Order().Select(i => new QuestChoice(i, $"Box {i + 1}")).ToArray() : QuestCreationChoices.SupplyBoxes;
    public IReadOnlyList<QuestItemSlot> Slots { get; private set; } = [];
    public CreationItem? SelectedEntry { get; private set; }
    public bool HasSelection => SelectedEntry != null;
    public bool CanAdd => !IsLayoutLocked && Items != null && Items.Count < (IsReward ? 96 : 160) && (IsReward || GroupItems.Count < 40);
    public bool CanDuplicate => HasSelection && CanAdd;
    public bool HasPrevious => _page > 0;
    public bool HasNext => _page + 1 < Pages;
    public string CountText => $"{GroupItems.Count} items" + (IsLayoutLocked ? "" : $" / {(IsReward ? $"{Items?.Count ?? 0} of 96 total" : "40 slots")}") + (Pages > 1 ? $" - {PageText}" : "");
    public string PageText => $"Page {_page + 1} / {Pages}";
    public string GroupLabel => IsReward ? "Reward table" : "Supply box";
    public Visibility DetailVisibility => HasSelection ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RewardVisibility => IsReward ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SupplyVisibility => IsReward ? Visibility.Collapsed : Visibility.Visible;
    public Visibility PagingVisibility => Pages > 1 ? Visibility.Visible : Visibility.Collapsed;

    public QuestItemBoxEditor()
    {
        InitializeComponent();
        GroupPicker.SelectedIndex = 0;
        Refresh();
    }
    private static void KindChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (QuestItemBoxEditor)d;
        view.RaiseAll();
        if (view.GroupPicker == null) return;
        view.GroupPicker.SelectedIndex = 0;
        view._page = 0;
        view.Refresh();
    }
    private static void ItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (QuestItemBoxEditor)d;
        if (e.OldValue is ObservableCollection<CreationItem> old) old.CollectionChanged -= view.CollectionChanged;
        foreach (var item in view._tracked.ToArray()) view.Track(item, false);
        if (e.NewValue is ObservableCollection<CreationItem> items) items.CollectionChanged += view.CollectionChanged;
        view.SelectedEntry = null; view._page = 0;
        if (view.GroupPicker != null) view.GroupPicker.SelectedIndex = 0;
        view.SyncTracking(); view.Refresh();
    }
    private void Track(CreationItem item, bool add)
    {
        if (add) { _tracked.Add(item); item.PropertyChanged += ItemChanged; item.Box.PropertyChanged += ItemChanged; item.Quantity.PropertyChanged += ItemChanged; item.Chance.PropertyChanged += ItemChanged; }
        else { _tracked.Remove(item); item.PropertyChanged -= ItemChanged; item.Box.PropertyChanged -= ItemChanged; item.Quantity.PropertyChanged -= ItemChanged; item.Chance.PropertyChanged -= ItemChanged; }
    }
    private void SyncTracking()
    {
        foreach (var old in _tracked.Where(i => Items?.Contains(i) != true).ToArray()) Track(old, false);
        if (Items != null) foreach (var item in Items.Where(i => !_tracked.Contains(i))) Track(item, true);
    }
    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) { SyncTracking(); Refresh(); }
    private void ItemChanged(object? sender, PropertyChangedEventArgs e) => Refresh();
    private void RaiseAll()
    {
        foreach (var name in new[] { nameof(Groups), nameof(Slots), nameof(SelectedEntry), nameof(HasSelection), nameof(CanAdd), nameof(CanDuplicate), nameof(HasPrevious), nameof(HasNext), nameof(CountText), nameof(PageText), nameof(GroupLabel), nameof(DetailVisibility), nameof(RewardVisibility), nameof(SupplyVisibility), nameof(PagingVisibility), nameof(StructuralVisibility), nameof(LockedVisibility) }) PropertyChanged?.Invoke(this, new(name));
    }
    private void Refresh()
    {
        if (_refreshing || SlotRows == null) return;
        _refreshing = true;
        try
        {
            _page = Math.Clamp(_page, 0, Pages - 1);
            var entries = GroupItems;
            if (SelectedEntry != null && !entries.Contains(SelectedEntry)) SelectedEntry = null;
            Slots = Enumerable.Range(0, 40).Select(i => new QuestItemSlot(_page * 40 + i + 1, entries.ElementAtOrDefault(_page * 40 + i), IsReward)).ToArray();
            RaiseAll();
            SlotRows.SelectedItem = Slots.FirstOrDefault(s => s.Entry != null && s.Entry == SelectedEntry);
        }
        finally { _refreshing = false; }
    }
    private void SelectEntry(CreationItem item)
    {
        var index = GroupItems.IndexOf(item);
        if (index < 0) return;
        SelectedEntry = item; _page = index / 40; Refresh();
    }
    private void Group_Changed(object sender, SelectionChangedEventArgs e) { SelectedEntry = null; _page = 0; Refresh(); }
    private void Slot_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing) return;
        SelectedEntry = (SlotRows.SelectedItem as QuestItemSlot)?.Entry;
        RaiseAll();
    }
    private void Slot_Click(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(SlotRows, e.OriginalSource as DependencyObject) is ListBoxItem { DataContext: QuestItemSlot { Entry: null } }) RequestAdd();
    }
    private void Slot_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && SlotRows.SelectedItem is QuestItemSlot slot) { if (slot.Entry == null) RequestAdd(); else RequestChange(); e.Handled = true; }
        else if (e.Key == Key.Delete && HasSelection) { RemoveSelected(); e.Handled = true; }
    }
    private void RequestAdd()
    {
        if (!CanAdd || Items == null) return;
        var source = Items; var group = Group;
        PickerRequested?.Invoke(this, new("Choose item", QuestCreationChoices.Items, null, choice =>
        {
            if (Items != source || Group != group || !CanAdd) return;
            var item = new CreationItem((ushort)choice.Id, 1, box: IsReward ? (byte)0 : (byte)group, table: IsReward ? (byte)group : (byte)0);
            source.Add(item); SelectEntry(item);
        }));
    }
    private void RequestChange()
    {
        var item = SelectedEntry;
        if (item == null) return;
        PickerRequested?.Invoke(this, new("Choose item", QuestCreationChoices.Items, item.Item, choice => { if (Items?.Contains(item) == true) item.Item = choice; }));
    }
    private void RemoveSelected() { if (!IsLayoutLocked && SelectedEntry is { } item) Items?.Remove(item); }
    private void DuplicateSelected()
    {
        if (!CanDuplicate || SelectedEntry is not { } selected || Items == null) return;
        var copy = new CreationItem(selected.ItemId, 1, box: (byte)Group, table: IsReward ? (byte)Group : (byte)0);
        copy.Quantity.Value = selected.Quantity.Value; copy.Chance.Value = selected.Chance.Value;
        Items.Add(copy); SelectEntry(copy);
    }
    private void Add_Click(object sender, RoutedEventArgs e) => RequestAdd();
    private void ChangeItem_Click(object sender, RoutedEventArgs e) => RequestChange();
    private void Remove_Click(object sender, RoutedEventArgs e) => RemoveSelected();
    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (IsLayoutLocked) return;
        if (Items?.Count > 0 && MessageBox.Show(Window.GetWindow(this), IsReward ? "Clear all reward tables?" : "Clear all supply boxes?", "Clear quest items", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes) Items.Clear();
    }
    private void Duplicate_Click(object sender, RoutedEventArgs e) => DuplicateSelected();
    private void Previous_Click(object sender, RoutedEventArgs e) { if (HasPrevious) { _page--; SelectedEntry = null; Refresh(); } }
    private void Next_Click(object sender, RoutedEventArgs e) { if (HasNext) { _page++; SelectedEntry = null; Refresh(); } }
    internal void VerifySmokeRender()
    {
        UpdateLayout();
        if (SlotRows.Items.Count != 40 || SlotRows.ActualWidth < 420 || SlotRows.ActualHeight < 220) throw new InvalidOperationException("Quest item box grid did not render.");
        foreach (var slot in Slots.Where(s => s.IsOccupied)) if (slot.IconImage == null) throw new InvalidOperationException("Quest item icon is missing.");
        if (SlotRows.ItemContainerGenerator.ContainerFromIndex(0) is not ListBoxItem cell || cell.ActualWidth < 48 || cell.ActualHeight < 40) throw new InvalidOperationException("Quest item cells are too small.");
        if (HasSelection && EntryDetails.TranslatePoint(new Point(0, EntryDetails.ActualHeight), this).Y > ActualHeight + 1) throw new InvalidOperationException($"Selected quest item controls are outside the viewport ({EntryDetails.TranslatePoint(new Point(0, EntryDetails.ActualHeight), this).Y:F1} / {ActualHeight:F1}).");
    }
    internal void AddSmoke() => RequestAdd();
    internal void SelectSmoke(int index) { GroupPicker.SelectedIndex = 0; if (GroupItems.Count > index) SelectEntry(GroupItems[index]); }
    internal void DuplicateSmoke() => DuplicateSelected();
    internal void RemoveSmoke() => RemoveSelected();
    internal void GroupSmoke(int group) => GroupPicker.SelectedIndex = group;
    internal void NextSmoke() => Next_Click(this, new());
}
