using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MH4UModern.Models;
using MH4UModern.Services;
using MH4UModern.ViewModels;
using Microsoft.Win32;
using mh4edit;

namespace MH4UModern;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly UndoManager _undo = new();
    private readonly DirtyTracker _dirty = new();
    private readonly BackupService _backupService = new();
    private readonly RecentFilesService _recentFilesService = new(App.IsSmokeTest ? App.SmokeTestDirectory : null);
    private readonly ValidationService _validationService = new();
    private readonly PropertyInspectorService _inspector;

    private MonHunSave? _save;
    private MonHunCharacter? _character;
    private SaveSnapshot? _snapshot;
    private ICollectionView? _itemsView;
    private ICollectionView? _equipmentView;
    private byte[]? _equipmentClipboard;
    private bool _suppressEquipmentSelectors;
    private bool _suppressEquipmentValueControls;
    private MonHunEquip[] _equipmentTail = Array.Empty<MonHunEquip>();
    private bool _lightTheme;

    private const int BoxPageSize = 100;
    private const int ItemPageCount = 14;
    private const int EquipmentPageCount = 15;
    private int _itemPageIndex;
    private int _itemRightPageIndex = 1;
    private int _equipmentPageIndex;
    private int _equipmentRightPageIndex = 1;
    private bool _itemPagePickerRight;
    private bool _equipmentPagePickerRight;
    private Point _slotDragStart;
    private int _itemDragOriginIndex = -1;
    private int _equipmentDragOriginIndex = -1;
    private bool _suppressItemEditor;
    private bool _suppressOverlayOpen;
    private ItemRowViewModel? _activeItemEditorRow;
    private EquipmentRowViewModel? _activeEquipmentEditorRow;
    private QuestRowViewModel? _activeQuestEditorRow;

    private CharacterEditorViewModel? _characterVm;
    private string _currentFileName = "No save loaded";
    private string _currentFilePath = "No character save selected";
    private string _validationStatus = "Waiting for save";

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ItemRowViewModel> Items { get; } = new();
    public ObservableCollection<EquipmentRowViewModel> Equipment { get; } = new();
    public ObservableCollection<ItemRowViewModel> ItemPageSlots { get; } = new();
    public ObservableCollection<ItemRowViewModel> ItemRightPageSlots { get; } = new();
    public ObservableCollection<EquipmentRowViewModel> EquipmentPageSlots { get; } = new();
    public ObservableCollection<EquipmentRowViewModel> EquipmentRightPageSlots { get; } = new();
    public ObservableCollection<QuestRowViewModel> Quests { get; } = new();
    public ObservableCollection<PalicoRowViewModel> Palicos { get; } = new();
    public ObservableCollection<BackupRowViewModel> Backups { get; } = new();
    public ObservableCollection<RecentSaveRowViewModel> RecentFiles { get; } = new();
    public ObservableCollection<ItemRowViewModel> ItemSearchSuggestions { get; } = new();
    public ObservableCollection<EquipmentRowViewModel> EquipmentSearchSuggestions { get; } = new();
    public ObservableCollection<EditablePropertyViewModel> EquipmentFields { get; } = new();
    public ObservableCollection<EditablePropertyViewModel> QuestFields { get; } = new();
    public ObservableCollection<EditablePropertyViewModel> PalicoFields { get; } = new();

    public string[] ItemNames => MonHunItem.Names;

    public CharacterEditorViewModel? Character
    {
        get => _characterVm;
        private set
        {
            if (_characterVm is not null) _characterVm.PropertyChanged -= CharacterIdentity_Changed;
            _characterVm = value;
            if (_characterVm is not null) _characterVm.PropertyChanged += CharacterIdentity_Changed;
            OnPropertyChanged(nameof(Character));
            OnPropertyChanged(nameof(HunterIdentity));
            OnPropertyChanged(nameof(HasCharacter));
        }
    }

    public string CurrentFileName
    {
        get => _currentFileName;
        private set { _currentFileName = value; OnPropertyChanged(nameof(CurrentFileName)); }
    }

    public string CurrentFilePath
    {
        get => _currentFilePath;
        private set { _currentFilePath = value; OnPropertyChanged(nameof(CurrentFilePath)); }
    }

    public string ValidationStatus
    {
        get => _validationStatus;
        private set { _validationStatus = value; OnPropertyChanged(nameof(ValidationStatus)); }
    }

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        CustomQuestsPage.EmulatorPathProvider = () => _emulatorPath;
        CustomQuestsPage.CharacterPathProvider = () => Character is null ? null : CurrentFilePath;
        CustomQuestOverlayHost.Content = CustomQuestsPage.TakeEditorPanel();
        CustomQuestsPage.EditorOpened += (_, _) =>
        {
            ItemEditorOverlayPanel.Visibility = Visibility.Collapsed;
            EquipmentEditorOverlayPanel.Visibility = Visibility.Collapsed;
            QuestEditorOverlayPanel.Visibility = Visibility.Collapsed;
            CustomQuestOverlayHost.Visibility = Visibility.Visible;
            EditorOverlay.Visibility = Visibility.Visible;
        };
        CustomQuestsPage.EditorClosed += (_, _) =>
        {
            CustomQuestOverlayHost.Visibility = Visibility.Collapsed;
            EditorOverlay.Visibility = Visibility.Collapsed;
        };
        _inspector = new PropertyInspectorService(_undo, _dirty);

        _dirty.Changed += (_, _) => UpdateDirtyUi();
        _undo.StateChanged += (_, _) => { UpdateUndoUi(); RecalculateDirty(); };

        BulkItemAmountBox.Text = "99";

        EquipmentIdCombo.SelectedValuePath = "ID";
        RefreshRecentFiles();
        Loaded += MainWindow_Loaded;
        UpdateUndoUi();
        UpdateDirtyUi();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (App.IsSmokeTest)
        {
            _ = Dispatcher.BeginInvoke(new Action(RunStartupSmokeTest), DispatcherPriority.ApplicationIdle);
            return;
        }
        await RestoreEmulatorPathAsync();
        const string version = "0.18.0";
        if (!VersionNoticeService.ShouldShow(version)) return;
        var changes = new[]
        {
            "Custom Quests adds a Rathian helper test preset: one Rathian in Battlequarters, area 3, with low-rank supplies and rewards.",
            "General has an experimental Ace Hunters patch checkbox for native Battlequarters quests. This saves authoring intent only; there is no verified MIB helper bit.",
            "The export icon creates a quest and BPS code-patch bundle from the exact inspected USA update executable. Other executables are rejected. Exporting installs nothing.",
            "The patch targets one custom quest ID and Battlequarters while retaining the original story/location checks. In-game NPC loading, combat, completion and saving remain UNVERIFIED. Test offline with copied saves and extdata.",
            "Unchecking the setting does not remove an installed patch. Close the game, remove this code.bps and restart to disable it. Do not combine executable mods.",
            "Save/Cancel, explicit installation consent, emulator/stale-snapshot guards and five-file backups remain. Story progress, character saves, Guild Quests, Box 15 and the original save-core DLL are unchanged."
        };
        var window = new ChangelogWindow(version, changes) { Owner = this };
        window.ShowDialog();
        VersionNoticeService.MarkSeen(version);
    }

    private void RunStartupSmokeTest()
    {
        try
        {
            Directory.CreateDirectory(App.SmokeTestDirectory);
            foreach (var asset in new[] { "assets/ui/nav/overview.png", "assets/ui/nav/items.png", "assets/ui/nav/equipment.png", "assets/catalog/items.tsv", "lists/itemlist.txt" })
                if (!File.Exists(Path.Combine(AppContext.BaseDirectory, asset))) throw new FileNotFoundException("Required app asset is missing.", asset);

            foreach (var page in new[] { "Overview", "Character", "Items", "Equipment", "Quests", "CustomQuests", "Palicos", "Advanced" })
            {
                ShowPage(page);
                CaptureSmokeScreenshot(page);
            }
            RunOverviewSmokeChecks();
            RunCustomQuestSmokeChecks();
            ToggleTheme_Click(this, new RoutedEventArgs());
            ShowPage("Items");
            CaptureSmokeScreenshot("Items-Light");
            _ = new ColorPickerWindow("Smoke test", Colors.Green);
            _ = new DecorationPickerWindow(0);
            if (DecorationCatalogService.Skills.Count == 0) throw new InvalidOperationException("Decoration catalog did not load.");
            File.WriteAllText(Path.Combine(App.SmokeTestDirectory, "smoke-results.txt"),
                "PASS: all 8 pages rendered; quest browser and shared native/imported three-tab editors checked at minimum/default/wide sizes in both themes; numbered rows/icons, overlay close/reopen, dirty-close cancellation and automatic/manual paths checked; native creation/save/reopen, transactional monster settings, family order, title/text, named options, picker search/select/cancel, side-by-side item boxes/capacities/reward paging checked; duplicate-free rows, decline/disable/re-enable/delete, exact imported-copy retention, deletion backups, unsaved-draft protection, shared imported field edits/opaque bytes/numeric and text aliases/text capacities/locked structure/faint conversion checked; occupied-wave controls, conditional intruder details, independent compact General artwork, manual preset protection, map-area rejection and rendered map previews checked; Save/Cancel, installation consent, emulator guard, five-file backups, legacy-ID migration and cutscene restrictions checked with isolated synthetic extdata. Picker constructors and decoration metadata loaded. No real save/extdata files were written.");
            Application.Current.Shutdown(0);
        }
        catch (Exception ex)
        {
            App.LogException(ex, "Startup smoke test");
            Application.Current.Shutdown(1);
        }
    }

    private void CaptureSmokeScreenshot(string name)
    {
        UpdateLayout();
        var content = (FrameworkElement)Content;
        var width = (int)Math.Ceiling(content.ActualWidth);
        var height = (int)Math.Ceiling(content.ActualHeight);
        if (width < 100 || height < 100) throw new InvalidOperationException("The desktop window did not lay out.");
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        if (!pixels.Any(value => value != 0)) throw new InvalidOperationException("The desktop screenshot is blank.");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(App.SmokeTestDirectory, name + ".png"));
        encoder.Save(output);
    }

    private void CharacterColor_Click(object sender, RoutedEventArgs e)
    {
        if (Character is null || sender is not Button { Tag: string key }) return;

        try
        {
            var initial = Character.GetAppearanceColor(key);
            var picker = new ColorPickerWindow($"{key} Color", initial) { Owner = this };
            if (picker.ShowDialog() == true)
                Character.SetAppearanceColor(key, picker.SelectedColor);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Color picker", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open MH4U save",
            Filter = "MH4U user save (user*)|user*|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
            LoadSave(dialog.FileName);
    }

    private void LoadSave(string path, bool skipDirtyPrompt = false)
    {
        if (!skipDirtyPrompt && _dirty.IsDirty && !ConfirmDiscard())
            return;

        try
        {
            SetStatus("Loading save…", "InfoBrush");
            var loaded = new MonHunSave(path);
            _save = loaded;
            _character = loaded.slot;
            _equipmentTail = ExtendedEquipmentService.ReadTail(loaded);
            _snapshot = SaveSnapshot.Capture(_character, _equipmentTail);
            _undo.Clear();
            _dirty.MarkClean();

            Character = new CharacterEditorViewModel(_character, _undo, _dirty);
            RebuildCollections();

            CurrentFileName = Path.GetFileName(path);
            CurrentFilePath = path;
            _recentFilesService.Add(path);
            RefreshRecentFiles();
            RefreshBackups();
            RefreshValidation();

            SaveButton.IsEnabled = true;
            NavOverview.IsChecked = true;
            ShowPage("Overview");
            SetStatus("Save loaded. Automatic backup is enabled before saving.", "AccentBrush");
        }
        catch (Exception ex)
        {
            _save = null;
            _character = null;
            _equipmentTail = Array.Empty<MonHunEquip>();
            _snapshot = null;
            Character = null;
            CurrentFileName = "No save loaded";
            CurrentFilePath = "No character save selected";
            RebuildCollections();
            RefreshBackups();
            RefreshValidation();
            SaveButton.IsEnabled = false;
            SetStatus("Could not load the save.", "DangerBrush");
            if (App.IsSmokeTest) throw new InvalidOperationException("Could not load a save during verification.", ex);
            MessageBox.Show(this, ex.GetBaseException().Message, "Could not open save", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SynchronizeEmulatorSelection(); }
    }

    private void RebuildCollections()
    {
        Items.Clear();
        Equipment.Clear();
        ItemPageSlots.Clear();
        ItemRightPageSlots.Clear();
        EquipmentPageSlots.Clear();
        EquipmentRightPageSlots.Clear();
        Quests.Clear();
        Palicos.Clear();
        EquipmentFields.Clear();
        QuestFields.Clear();
        PalicoFields.Clear();

        if (_character is null) return;

        for (var i = 0; i < _character.ItemBox.Length; i++)
            Items.Add(new ItemRowViewModel(i, _character.ItemBox[i], _undo, _dirty));
        for (var i = 0; i < _character.EquipBox.Length; i++)
            Equipment.Add(new EquipmentRowViewModel(i, _character.EquipBox[i], _dirty));
        for (var i = 0; i < _equipmentTail.Length; i++)
            Equipment.Add(new EquipmentRowViewModel(ExtendedEquipmentService.LegacySlotCount + i, _equipmentTail[i], _dirty));
        for (var i = 0; i < _character.GuildQuests.Length; i++)
            Quests.Add(new QuestRowViewModel(i, _character.GuildQuests[i]));
        for (var i = 0; i < _character.Palicos.Length; i++)
            Palicos.Add(new PalicoRowViewModel(i, _character.Palicos[i]));

        _itemsView = CollectionViewSource.GetDefaultView(Items);
        _equipmentView = CollectionViewSource.GetDefaultView(Equipment);
        ItemSearchSuggestions.Clear();
        EquipmentSearchSuggestions.Clear();
        _itemPageIndex = 0;
        _itemRightPageIndex = 1;
        _equipmentPageIndex = 0;
        _equipmentRightPageIndex = 1;
        RefreshItemPage();
        RefreshEquipmentPage();
        OnPropertyChanged(nameof(ItemNames));
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_save is null || _character is null || _snapshot is null) return;

        try
        {
            var changes = _snapshot.Compare(_character, _equipmentTail);
            if (changes.Count == 0)
            {
                MessageBox.Show(this, "There are no changes to save.", "MH4U Modern", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var warnings = ValidationCheck.IsChecked == true
                ? _validationService.Validate(_character)
                : Array.Empty<string>();

            if (PreviewCheck.IsChecked == true)
            {
                var preview = new ChangePreviewWindow(changes, warnings) { Owner = this };
                if (preview.ShowDialog() != true) return;
            }
            else if (warnings.Count > 0)
            {
                var warningText = string.Join(Environment.NewLine, warnings.Take(8));
                if (warnings.Count > 8) warningText += $"{Environment.NewLine}…and {warnings.Count - 8} more.";
                var result = MessageBox.Show(this, warningText + "\n\nSave anyway?", "Validation warnings", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes) return;
            }

            string? backupPath = null;
            if (AutoBackupCheck.IsChecked == true)
                backupPath = _backupService.CreateBackup(_save.FileName);

            ExtendedEquipmentService.SaveWithTail(_save, _character, _equipmentTail);
            _snapshot = SaveSnapshot.Capture(_character, _equipmentTail);
            _undo.Clear();
            _dirty.MarkClean();
            RefreshBackups();
            RefreshValidation();

            SetStatus(backupPath is null ? "Save written successfully." : $"Saved. Backup: {Path.GetFileName(backupPath)}", "AccentBrush");
        }
        catch (Exception ex)
        {
            SetStatus("Save failed. The original file was not intentionally discarded.", "DangerBrush");
            MessageBox.Show(this, ex.GetBaseException().Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Undo_Click(object sender, RoutedEventArgs e) => _undo.Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => _undo.Redo();

    private void UpdateUndoUi()
    {
        if (!IsLoaded) return;
        UndoButton.IsEnabled = _undo.CanUndo;
        RedoButton.IsEnabled = _undo.CanRedo;
        UndoButton.Content = _undo.UndoLabel;
        RedoButton.Content = _undo.RedoLabel;
    }


    private void RecalculateDirty()
    {
        if (_snapshot is null || _character is null) return;
        if (_snapshot.Compare(_character, _equipmentTail).Count == 0) _dirty.MarkClean();
        else _dirty.MarkDirty();
    }

    private void UpdateDirtyUi()
    {
        if (!IsLoaded) return;
        if (_save is null)
        {
            SetStatus("Open or drop an MH4U user save to begin.", "MutedTextBrush");
            return;
        }

        if (_dirty.IsDirty)
            SetStatus("Unsaved changes", "WarningBrush");
        else
            SetStatus("No unsaved changes", "AccentBrush");
    }

    private void SetStatus(string text, string brushResource)
    {
        if (!IsLoaded) return;
        StatusText.Text = text;
        if (TryFindResource(brushResource) is Brush brush)
            StatusDot.Fill = brush;
    }

    private void RefreshValidation()
    {
        if (_character is null)
        {
            ValidationStatus = "Waiting for save";
            return;
        }
        var warnings = _validationService.Validate(_character);
        ValidationStatus = warnings.Count == 0 ? "✓ Structure looks valid" : $"⚠ {warnings.Count} warning{(warnings.Count == 1 ? string.Empty : "s")}";
    }

    // Navigation
    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton button && button.Tag is string page)
            ShowPage(page);
    }

    private void ShowPage(string page)
    {
        if (!IsLoaded) return;
        if (page != "CustomQuests" && CustomQuestsPage.IsEditorOpen && !CustomQuestsPage.TryCloseEditor())
        {
            NavCustomQuests.IsChecked = true;
            return;
        }
        OverviewPage.Visibility = page == "Overview" ? Visibility.Visible : Visibility.Collapsed;
        CharacterPage.Visibility = page == "Character" ? Visibility.Visible : Visibility.Collapsed;
        ItemsPage.Visibility = page == "Items" ? Visibility.Visible : Visibility.Collapsed;
        EquipmentPage.Visibility = page == "Equipment" ? Visibility.Visible : Visibility.Collapsed;
        QuestsPage.Visibility = page == "Quests" ? Visibility.Visible : Visibility.Collapsed;
        CustomQuestsPage.Visibility = page == "CustomQuests" ? Visibility.Visible : Visibility.Collapsed;
        CharacterCommandBar.IsEnabled = page != "CustomQuests";
        PalicosPage.Visibility = page == "Palicos" ? Visibility.Visible : Visibility.Collapsed;
        AdvancedPage.Visibility = page == "Advanced" ? Visibility.Visible : Visibility.Collapsed;
        if (page == "CustomQuests" && !App.IsSmokeTest) CustomQuestsPage.UseConfiguredEmulatorPath();
    }


    private void ToggleTheme_Click(object sender, RoutedEventArgs e)
    {
        _lightTheme = !_lightTheme;
        if (_lightTheme)
        {
            SetBrushColor("WindowBrush", "#EEF2F5");
            SetBrushColor("SidebarBrush", "#E4EAF0");
            SetBrushColor("SurfaceBrush", "#FFFFFF");
            SetBrushColor("Surface2Brush", "#F5F7F9");
            SetBrushColor("Surface3Brush", "#EAF0F4");
            SetBrushColor("BorderBrush", "#CBD4DD");
            SetBrushColor("TextBrush", "#17202A");
            SetBrushColor("MutedTextBrush", "#62707E");
            SetBrushColor("HoverBrush", "#E8F1EE");
            SetBrushColor("SelectionBrush", "#D4EEE5");
            SetBrushColor("SelectionTextBrush", "#0E2B22");
            SetBrushColor("NavHoverBrush", "#D9E3E9");
            SetBrushColor("NavSelectedBrush", "#CBE7DD");
            SetBrushColor("HeaderBrush", "#E5EBF0");
            SetBrushColor("AlternateRowBrush", "#F5F7F9");
            SetBrushColor("GridLineBrush", "#D8E0E7");
            SetBrushColor("ToolTipBrush", "#F7FFFC");
            SetBrushColor("ToolTipTextBrush", "#17202A");
            SetBrushColor("IconSurfaceBrush", "#EDF2F5");
            SetBrushColor("AccentTintBrush", "#DDF5ED");
            SetBrushColor("WarningSurfaceBrush", "#FFF7E5");
            SetBrushColor("WarningBorderBrush", "#E7C879");
            SetBrushColor("ValidSurfaceBrush", "#DDF5E8");
            SetBrushColor("SlotEmptyBrush", "#F3F6F8");
            SetBrushColor("SlotOccupiedBrush", "#E8EEF2");
        }
        else
        {
            SetBrushColor("WindowBrush", "#0D1016");
            SetBrushColor("SidebarBrush", "#111722");
            SetBrushColor("SurfaceBrush", "#171D27");
            SetBrushColor("Surface2Brush", "#1E2632");
            SetBrushColor("Surface3Brush", "#242E3B");
            SetBrushColor("BorderBrush", "#2B3543");
            SetBrushColor("TextBrush", "#F4F6F8");
            SetBrushColor("MutedTextBrush", "#9EABB9");
            SetBrushColor("HoverBrush", "#25303C");
            SetBrushColor("SelectionBrush", "#294338");
            SetBrushColor("SelectionTextBrush", "#F7FFFC");
            SetBrushColor("NavHoverBrush", "#18212D");
            SetBrushColor("NavSelectedBrush", "#20352F");
            SetBrushColor("HeaderBrush", "#202936");
            SetBrushColor("AlternateRowBrush", "#141A23");
            SetBrushColor("GridLineBrush", "#27313E");
            SetBrushColor("ToolTipBrush", "#25313E");
            SetBrushColor("ToolTipTextBrush", "#F4F6F8");
            SetBrushColor("IconSurfaceBrush", "#111720");
            SetBrushColor("AccentTintBrush", "#16342B");
            SetBrushColor("WarningSurfaceBrush", "#2D2616");
            SetBrushColor("WarningBorderBrush", "#594A22");
            SetBrushColor("ValidSurfaceBrush", "#12291F");
            SetBrushColor("SlotEmptyBrush", "#121924");
            SetBrushColor("SlotOccupiedBrush", "#1D2733");
        }
    }

    private void SetBrushColor(string key, string hex)
    {
        Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }

    private void GoBackups_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in FindVisualChildren<RadioButton>(this))
        {
            if (Equals(item.Tag, "Advanced")) { item.IsChecked = true; break; }
        }
    }

    // Visual 10x10 box paging -------------------------------------------------
    private void RefreshItemPage()
    {
        ItemPageSlots.Clear();
        foreach (var row in Items.Skip(_itemPageIndex * BoxPageSize).Take(BoxPageSize)) ItemPageSlots.Add(row);
        ItemRightPageSlots.Clear();
        foreach (var row in Items.Skip(_itemRightPageIndex * BoxPageSize).Take(BoxPageSize)) ItemRightPageSlots.Add(row);
        if (!IsLoaded) return;
        ItemPageLabel.Text = $"{_itemPageIndex + 1} / {ItemPageCount}";
        ItemRightPageLabel.Text = $"{_itemRightPageIndex + 1} / {ItemPageCount}";
        ItemLeftBoxKind.Text = GetItemPageLabel(_itemPageIndex);
        ItemRightBoxKind.Text = GetItemPageLabel(_itemRightPageIndex);
    }

    private void RefreshEquipmentPage()
    {
        EquipmentPageSlots.Clear();
        foreach (var row in Equipment.Skip(_equipmentPageIndex * BoxPageSize).Take(BoxPageSize)) EquipmentPageSlots.Add(row);
        EquipmentRightPageSlots.Clear();
        foreach (var row in Equipment.Skip(_equipmentRightPageIndex * BoxPageSize).Take(BoxPageSize)) EquipmentRightPageSlots.Add(row);
        if (!IsLoaded) return;
        EquipmentPageLabel.Text = $"{_equipmentPageIndex + 1} / {EquipmentPageCount}";
        EquipmentRightPageLabel.Text = $"{_equipmentRightPageIndex + 1} / {EquipmentPageCount}";
        EquipmentLeftBoxKind.Text = GetEquipmentPageLabel(_equipmentPageIndex);
        EquipmentRightBoxKind.Text = GetEquipmentPageLabel(_equipmentRightPageIndex);
    }

    private string GetItemPageLabel(int page)
    {
        var count = Items.Skip(page * BoxPageSize).Take(BoxPageSize).Count(x => x.IsOccupied);
        return count == 0 ? "Empty" : $"Items · {count}";
    }

    private void ItemPagePrevious_Click(object sender, RoutedEventArgs e)
    {
        _itemPageIndex = (_itemPageIndex - 1 + ItemPageCount) % ItemPageCount;
        RefreshItemPage();
        CloseEditorOverlay();
    }

    private void ItemPageNext_Click(object sender, RoutedEventArgs e)
    {
        _itemPageIndex = (_itemPageIndex + 1) % ItemPageCount;
        RefreshItemPage();
        CloseEditorOverlay();
    }

    private void ItemRightPagePrevious_Click(object sender, RoutedEventArgs e)
    {
        _itemRightPageIndex = (_itemRightPageIndex - 1 + ItemPageCount) % ItemPageCount;
        RefreshItemPage();
    }

    private void ItemRightPageNext_Click(object sender, RoutedEventArgs e)
    {
        _itemRightPageIndex = (_itemRightPageIndex + 1) % ItemPageCount;
        RefreshItemPage();
    }

    private void EquipmentPagePrevious_Click(object sender, RoutedEventArgs e)
    {
        _equipmentPageIndex = (_equipmentPageIndex - 1 + EquipmentPageCount) % EquipmentPageCount;
        RefreshEquipmentPage();
        CloseEditorOverlay();
    }

    private void EquipmentPageNext_Click(object sender, RoutedEventArgs e)
    {
        _equipmentPageIndex = (_equipmentPageIndex + 1) % EquipmentPageCount;
        RefreshEquipmentPage();
        CloseEditorOverlay();
    }

    private void EquipmentRightPagePrevious_Click(object sender, RoutedEventArgs e)
    {
        _equipmentRightPageIndex = (_equipmentRightPageIndex - 1 + EquipmentPageCount) % EquipmentPageCount;
        RefreshEquipmentPage();
    }

    private void EquipmentRightPageNext_Click(object sender, RoutedEventArgs e)
    {
        _equipmentRightPageIndex = (_equipmentRightPageIndex + 1) % EquipmentPageCount;
        RefreshEquipmentPage();
    }

    private void ItemPagePicker_Click(object sender, RoutedEventArgs e)
    {
        _itemPagePickerRight = sender is FrameworkElement { Tag: string tag } && tag == "Right";
        BuildItemPagePicker();
        ItemPagePickerPopup.PlacementTarget = sender as UIElement;
        ItemPagePickerPopup.IsOpen = true;
    }

    private void EquipmentPagePicker_Click(object sender, RoutedEventArgs e)
    {
        _equipmentPagePickerRight = sender is FrameworkElement { Tag: string tag } && tag == "Right";
        BuildEquipmentPagePicker();
        EquipmentPagePickerPopup.PlacementTarget = sender as UIElement;
        EquipmentPagePickerPopup.IsOpen = true;
    }

    private void BuildItemPagePicker()
    {
        ItemPagePickerGrid.Children.Clear();
        var current = _itemPagePickerRight ? _itemRightPageIndex : _itemPageIndex;
        for (var page = 0; page < ItemPageCount; page++)
        {
            var label = GetItemPageLabel(page);
            var button = CreatePagePickerButton(page, label, page == current);
            button.Click += ItemPagePickerButton_Click;
            ItemPagePickerGrid.Children.Add(button);
        }
    }

    private void BuildEquipmentPagePicker()
    {
        EquipmentPagePickerGrid.Children.Clear();
        var current = _equipmentPagePickerRight ? _equipmentRightPageIndex : _equipmentPageIndex;
        for (var page = 0; page < EquipmentPageCount; page++)
        {
            var label = GetEquipmentPageLabel(page);
            var button = CreatePagePickerButton(page, label, page == current);
            button.Click += EquipmentPagePickerButton_Click;
            EquipmentPagePickerGrid.Children.Add(button);
        }
    }

    private Button CreatePagePickerButton(int page, string label, bool current)
    {
        var title = new TextBlock { Text = $"Box {page + 1}", FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        var subtitle = new TextBlock
        {
            Text = label,
            FontSize = 10,
            Foreground = (Brush)FindResource(label == "Empty" ? "MutedTextBrush" : "AccentBrush"),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var stack = new StackPanel(); stack.Children.Add(title); stack.Children.Add(subtitle);
        return new Button
        {
            Tag = page,
            Content = stack,
            Margin = new Thickness(4),
            Padding = new Thickness(8, 7, 8, 7),
            Opacity = label == "Empty" ? 0.60 : 1.0,
            BorderBrush = current ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("BorderBrush")
        };
    }

    private string GetEquipmentPageLabel(int page)
    {
        var rows = Equipment.Skip(page * BoxPageSize).Take(BoxPageSize).Where(x => x.IsOccupied).ToArray();
        if (rows.Length == 0) return "Empty";
        var kinds = rows.Select(x => x.Model.Kind).Distinct().ToArray();
        if (kinds.Length != 1) return "Mixed";
        return kinds[0] switch
        {
            MonHunEquip.Kinds.Weapon => "Weapons",
            MonHunEquip.Kinds.Armor => "Armor",
            MonHunEquip.Kinds.Talisman => "Talismans",
            _ => "Mixed"
        };
    }

    private void ItemPagePickerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int page }) return;
        if (_itemPagePickerRight) _itemRightPageIndex = page; else _itemPageIndex = page;
        ItemPagePickerPopup.IsOpen = false;
        RefreshItemPage();
        CloseEditorOverlay();
    }

    private void EquipmentPagePickerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int page }) return;
        if (_equipmentPagePickerRight) _equipmentRightPageIndex = page; else _equipmentPageIndex = page;
        EquipmentPagePickerPopup.IsOpen = false;
        RefreshEquipmentPage();
        CloseEditorOverlay();
    }

    private void GoToItemSlot(ItemRowViewModel row)
    {
        _itemPageIndex = Math.Clamp(row.Index / BoxPageSize, 0, ItemPageCount - 1);
        RefreshItemPage();
        _suppressOverlayOpen = true;
        try { ItemsGrid.SelectedItems.Clear(); ItemsGrid.SelectedItem = row; }
        finally { _suppressOverlayOpen = false; }
        CloseEditorOverlay();
    }

    private void GoToEquipmentSlot(EquipmentRowViewModel row)
    {
        _equipmentPageIndex = Math.Clamp(row.Index / BoxPageSize, 0, EquipmentPageCount - 1);
        RefreshEquipmentPage();
        _suppressOverlayOpen = true;
        try { EquipmentGrid.SelectedItems.Clear(); EquipmentGrid.SelectedItem = row; }
        finally { _suppressOverlayOpen = false; }
        CloseEditorOverlay();
    }

    // Smart search -------------------------------------------------------------
    // Search suggestions are based only on equipment/items that are actually present in this save.
    private void RefreshItemSearchSuggestions()
    {
        ItemSearchSuggestions.Clear();
        var query = ItemSearchBox.Text.Trim();
        if (query.Length == 0)
        {
            ItemSearchPopup.IsOpen = false;
            return;
        }

        foreach (var row in Items.Where(r => r.IsOccupied && MatchesItemSearch(r, query))
                                 .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                                 .ThenBy(r => r.Slot)
                                 .Take(10))
            ItemSearchSuggestions.Add(row);

        ItemSearchPopup.IsOpen = ItemSearchSuggestions.Count > 0 && ItemSearchBox.IsKeyboardFocusWithin;
    }

    private void RefreshEquipmentSearchSuggestions()
    {
        EquipmentSearchSuggestions.Clear();
        var query = EquipmentSearchBox.Text.Trim();
        if (query.Length == 0)
        {
            EquipmentSearchPopup.IsOpen = false;
            return;
        }

        foreach (var row in Equipment.Where(r => r.IsOccupied && MatchesEquipmentSearch(r, query))
                                     .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                                     .ThenBy(r => r.Slot)
                                     .Take(10))
            EquipmentSearchSuggestions.Add(row);

        EquipmentSearchPopup.IsOpen = EquipmentSearchSuggestions.Count > 0 && EquipmentSearchBox.IsKeyboardFocusWithin;
    }

    private static bool MatchesItemSearch(ItemRowViewModel row, string query)
        => row.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
           || row.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
           || row.ID.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)
           || row.Slot.ToString().Contains(query, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesEquipmentSearch(EquipmentRowViewModel row, string query)
        => row.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
           || row.Type.Contains(query, StringComparison.OrdinalIgnoreCase)
           || row.Model.ID.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)
           || row.Slot.ToString().Contains(query, StringComparison.OrdinalIgnoreCase);

    private void ItemSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || ItemSearchSuggestions.Count == 0) return;
        GoToItemSlot(ItemSearchSuggestions[0]);
        ItemSearchPopup.IsOpen = false;
        e.Handled = true;
    }

    private void EquipmentSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || EquipmentSearchSuggestions.Count == 0) return;
        GoToEquipmentSlot(EquipmentSearchSuggestions[0]);
        EquipmentSearchPopup.IsOpen = false;
        e.Handled = true;
    }

    private void ItemSearchSuggestionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ItemSearchSuggestionsList.SelectedItem is not ItemRowViewModel row) return;
        GoToItemSlot(row);
        ItemSearchPopup.IsOpen = false;
        ItemSearchSuggestionsList.SelectedItem = null;
        SetStatus($"Found {row.Name} in item slot {row.Slot}.", "InfoBrush");
    }

    private void EquipmentSearchSuggestionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EquipmentSearchSuggestionsList.SelectedItem is not EquipmentRowViewModel row) return;
        GoToEquipmentSlot(row);
        EquipmentSearchPopup.IsOpen = false;
        EquipmentSearchSuggestionsList.SelectedItem = null;
        SetStatus($"Found {row.Name} in equipment slot {row.Slot}.", "InfoBrush");
    }

    // Centered item/equipment editors -----------------------------------------
    private void ItemsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressOverlayOpen) return;
        if (ItemArrangeButton.IsChecked == true) return;
        if (sender is ListBox list && list.SelectedItem is ItemRowViewModel row)
            OpenItemOverlay(row);
    }

    private void OpenItemOverlay(ItemRowViewModel row)
    {
        _activeItemEditorRow = row;
        _activeEquipmentEditorRow = null;
        _activeQuestEditorRow = null;
        RefreshItemInspector(row, 1);
        ItemEditorOverlayPanel.Visibility = Visibility.Visible;
        EquipmentEditorOverlayPanel.Visibility = Visibility.Collapsed;
        QuestEditorOverlayPanel.Visibility = Visibility.Collapsed;
        EditorOverlay.Visibility = Visibility.Visible;
    }

    private void RefreshItemInspector(ItemRowViewModel? row, int selectionCount = 0)
    {
        _suppressItemEditor = true;
        try
        {
            if (row is null)
            {
                SelectedItemTitle.Text = "Item";
                SelectedItemSubtitle.Text = "No slot selected";
                SelectedItemIdCombo.SelectedIndex = -1;
                SelectedItemCountBox.Text = string.Empty;
                SelectedItemIdCombo.IsEnabled = false;
                SelectedItemCountBox.IsEnabled = false;
                SelectedItemImage.Source = null;
                SelectedItemIconGlyph.Text = string.Empty;
                SelectedItemCategoryChip.Text = "Item";
                SelectedItemSlotChip.Text = "No slot";
                SelectedItemCatalogCard.Visibility = Visibility.Collapsed;
                SelectedItemCatalogLine.Text = string.Empty;
                SelectedItemCatalogDescription.Text = string.Empty;
                return;
            }

            SelectedItemTitle.Text = row.IsOccupied ? row.Name : "Empty slot";
            SelectedItemSubtitle.Text = $"Item ID {row.ID} · Quantity {row.Count}";
            SelectedItemCategoryChip.Text = row.Category;
            SelectedItemSlotChip.Text = $"Slot {row.Slot}";
            SelectedItemIdCombo.SelectedIndex = row.ID < ItemNames.Length ? row.ID : -1;
            SelectedItemCountBox.Text = row.Count.ToString();
            SelectedItemIdCombo.IsEnabled = true;
            SelectedItemCountBox.IsEnabled = true;
            SelectedItemImage.Source = row.IconImage;
            SelectedItemImage.Visibility = row.HasIcon ? Visibility.Visible : Visibility.Collapsed;
            SelectedItemIconGlyph.Text = row.HasIcon || !row.IsOccupied ? string.Empty : row.IconGlyph;
            SelectedItemIconGlyph.Foreground = row.IconBrush;

            if (row.Catalog is not null)
            {
                var catalogBits = new[] { row.DatabaseType, row.RarityText, row.EconomyText }
                    .Where(value => !string.IsNullOrWhiteSpace(value));
                SelectedItemCatalogLine.Text = "MH4U DB · " + string.Join(" · ", catalogBits);
                SelectedItemCatalogDescription.Text = row.Description;
                SelectedItemCatalogCard.Visibility = Visibility.Visible;
            }
            else
            {
                SelectedItemCatalogCard.Visibility = Visibility.Collapsed;
                SelectedItemCatalogLine.Text = string.Empty;
                SelectedItemCatalogDescription.Text = string.Empty;
            }
        }
        finally { _suppressItemEditor = false; }
    }

    private void SelectedItemIdCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressItemEditor || _activeItemEditorRow is not ItemRowViewModel row || SelectedItemIdCombo.SelectedIndex < 0) return;
        row.ID = (ushort)SelectedItemIdCombo.SelectedIndex;
        if (row.ID == 0) row.Count = 0;
        else if (row.Count == 0) row.Count = 1;
        RefreshItemInspector(row, 1);
        RefreshItemPage();
        RefreshValidation();
    }

    private void SelectedItemCountBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressItemEditor || _activeItemEditorRow is not ItemRowViewModel row) return;
        if (!ushort.TryParse(SelectedItemCountBox.Text.Trim(), out var amount) || amount > 99)
        {
            SelectedItemCountBox.Text = row.Count.ToString();
            return;
        }
        row.Count = amount;
        RefreshItemInspector(row, 1);
        RefreshItemPage();
    }

    private void OpenEquipmentOverlay(EquipmentRowViewModel row)
    {
        _activeEquipmentEditorRow = row;
        _activeItemEditorRow = null;
        _activeQuestEditorRow = null;
        RefreshEquipmentInspector(row);
        EquipmentEditorOverlayPanel.Visibility = Visibility.Visible;
        ItemEditorOverlayPanel.Visibility = Visibility.Collapsed;
        QuestEditorOverlayPanel.Visibility = Visibility.Collapsed;
        EditorOverlay.Visibility = Visibility.Visible;
    }

    private void CloseEditorOverlay_Click(object sender, RoutedEventArgs e) => CloseEditorOverlay();

    private void CloseEditorOverlay()
    {
        if (!IsLoaded) return;
        if (CustomQuestsPage.IsEditorOpen && !CustomQuestsPage.TryCloseEditor()) return;
        CustomQuestOverlayHost.Visibility = Visibility.Collapsed;
        EditorOverlay.Visibility = Visibility.Collapsed;
        ItemEditorOverlayPanel.Visibility = Visibility.Collapsed;
        EquipmentEditorOverlayPanel.Visibility = Visibility.Collapsed;
        QuestEditorOverlayPanel.Visibility = Visibility.Collapsed;
        _activeItemEditorRow = null;
        _activeEquipmentEditorRow = null;
        _activeQuestEditorRow = null;

        // Reset the quest selection when its modal closes so clicking the same
        // quest again immediately reopens the editor.
        if (QuestList.SelectedItem is not null)
            QuestList.SelectedItem = null;
    }

    // Drag/drop Arrange mode --------------------------------------------------
    private void ItemArrange_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        var enabled = ItemArrangeButton.IsChecked == true;
        ItemRightColumn.Width = enabled ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        ItemGapColumn.Width = enabled ? new GridLength(12) : new GridLength(0);
        ItemRightCard.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        ItemMultiSelectButton.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (enabled && _itemRightPageIndex == _itemPageIndex) _itemRightPageIndex = (_itemPageIndex + 1) % ItemPageCount;
        if (!enabled) ItemMultiSelectButton.IsChecked = false;
        SetItemSelectionModes();
        CloseEditorOverlay();
        RefreshItemPage();
        SetStatus(enabled ? "Arrange mode: drag between the two item boxes. Shift selects A→B; Multi Select enables click-to-check." : "Arrange mode closed. Single-click an item to edit it.", "InfoBrush");
    }

    private void EquipmentArrange_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        var enabled = EquipmentArrangeButton.IsChecked == true;
        EquipmentRightColumn.Width = enabled ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        EquipmentGapColumn.Width = enabled ? new GridLength(12) : new GridLength(0);
        EquipmentRightCard.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        EquipmentMultiSelectButton.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (enabled && _equipmentRightPageIndex == _equipmentPageIndex) _equipmentRightPageIndex = (_equipmentPageIndex + 1) % EquipmentPageCount;
        if (!enabled) EquipmentMultiSelectButton.IsChecked = false;
        EquipmentSellSelectedButton.Visibility = enabled && EquipmentMultiSelectButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SetEquipmentSelectionModes();
        CloseEditorOverlay();
        RefreshEquipmentPage();
        SetStatus(enabled ? "Arrange mode: drag between the two equipment boxes. Shift selects A→B; Multi Select keeps checked slots together while dragging." : "Arrange mode closed. Single-click equipment to edit it.", "InfoBrush");
    }

    private void ItemMultiSelect_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        SetItemSelectionModes();
        SetStatus(ItemMultiSelectButton.IsChecked == true
            ? "Item Multi Select: click adds a checkmark, Ctrl-click removes one, and click-hold/drag on any checked slot moves the whole selection."
            : "Shift selects an A→B range; Ctrl adds/removes individual slots.", "InfoBrush");
    }

    private void EquipmentMultiSelect_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        SetEquipmentSelectionModes();
        EquipmentSellSelectedButton.Visibility = EquipmentArrangeButton.IsChecked == true && EquipmentMultiSelectButton.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
        SetStatus(EquipmentMultiSelectButton.IsChecked == true
            ? "Equipment Multi Select: click adds a checkmark, Ctrl-click removes one, and click-hold/drag on any checked slot moves the whole selection."
            : "Shift selects an A→B range; Ctrl adds/removes individual slots.", "InfoBrush");
    }

    private void SetItemSelectionModes()
    {
        var mode = ItemArrangeButton.IsChecked == true
            ? (ItemMultiSelectButton.IsChecked == true ? SelectionMode.Multiple : SelectionMode.Extended)
            : SelectionMode.Single;
        ItemsGrid.SelectionMode = mode;
        ItemsGridRight.SelectionMode = mode;
    }

    private void SetEquipmentSelectionModes()
    {
        var mode = EquipmentArrangeButton.IsChecked == true
            ? (EquipmentMultiSelectButton.IsChecked == true ? SelectionMode.Multiple : SelectionMode.Extended)
            : SelectionMode.Single;
        EquipmentGrid.SelectionMode = mode;
        EquipmentGridRight.SelectionMode = mode;
    }

    private void ItemsGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox list) return;
        _slotDragStart = e.GetPosition(this);
        var row = GetDropTarget<ItemRowViewModel>(list, e.GetPosition(list));
        _itemDragOriginIndex = row?.Index ?? -1;

        if (ItemArrangeButton.IsChecked == true && ItemMultiSelectButton.IsChecked == true && row is not null)
        {
            // WPF Multiple mode normally toggles a selected item off on mouse-down.
            // That makes a drag that begins on a checked slot collapse to one item.
            // In arrange mode we keep checked slots stable; Ctrl-click is the explicit uncheck gesture.
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                if (list.SelectedItems.Contains(row)) list.SelectedItems.Remove(row);
                else list.SelectedItems.Add(row);
            }
            else if (!list.SelectedItems.Contains(row))
            {
                list.SelectedItems.Add(row);
            }
            e.Handled = true;
            return;
        }

        if (ItemArrangeButton.IsChecked != true && row is not null)
        {
            _suppressOverlayOpen = true;
            try { list.SelectedItem = row; } finally { _suppressOverlayOpen = false; }
            OpenItemOverlay(row);
            e.Handled = true;
        }
    }

    private void EquipmentGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox list) return;
        _slotDragStart = e.GetPosition(this);
        var row = GetDropTarget<EquipmentRowViewModel>(list, e.GetPosition(list));
        _equipmentDragOriginIndex = row?.Index ?? -1;

        if (EquipmentArrangeButton.IsChecked == true && EquipmentMultiSelectButton.IsChecked == true && row is not null)
        {
            // Preserve the checked group when a drag begins on an already-selected slot.
            // Ctrl-click is used to remove an individual checkmark.
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                if (list.SelectedItems.Contains(row)) list.SelectedItems.Remove(row);
                else list.SelectedItems.Add(row);
            }
            else if (!list.SelectedItems.Contains(row))
            {
                list.SelectedItems.Add(row);
            }
            e.Handled = true;
            return;
        }

        if (EquipmentArrangeButton.IsChecked != true && row is not null)
        {
            _suppressOverlayOpen = true;
            try { list.SelectedItem = row; } finally { _suppressOverlayOpen = false; }
            OpenEquipmentOverlay(row);
            e.Handled = true;
        }
    }

    private bool HasDraggedFarEnough(MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return false;
        var now = e.GetPosition(this);
        return Math.Abs(now.X - _slotDragStart.X) >= SystemParameters.MinimumHorizontalDragDistance
               || Math.Abs(now.Y - _slotDragStart.Y) >= SystemParameters.MinimumVerticalDragDistance;
    }

    private int[] GetSelectedItemIndices()
        => ItemsGrid.SelectedItems.Cast<ItemRowViewModel>()
            .Concat(ItemsGridRight.SelectedItems.Cast<ItemRowViewModel>())
            .Select(x => x.Index).Distinct().OrderBy(x => x).ToArray();

    private int[] GetSelectedEquipmentIndices()
        => EquipmentGrid.SelectedItems.Cast<EquipmentRowViewModel>()
            .Concat(EquipmentGridRight.SelectedItems.Cast<EquipmentRowViewModel>())
            .Select(x => x.Index).Distinct().OrderBy(x => x).ToArray();

    private void ItemsGrid_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (ItemArrangeButton.IsChecked != true || !HasDraggedFarEnough(e) || sender is not ListBox list) return;
        var clicked = GetDropTarget<ItemRowViewModel>(list, e.GetPosition(list));
        if (clicked is null) return;
        var selected = GetSelectedItemIndices();
        if (!selected.Contains(clicked.Index)) selected = new[] { clicked.Index };
        if (selected.Length == 0) return;
        var data = new DataObject(); data.SetData("MH4UModern.ItemSlots", selected);
        DragDrop.DoDragDrop(list, data, DragDropEffects.Move);
    }

    private void EquipmentGrid_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (EquipmentArrangeButton.IsChecked != true || !HasDraggedFarEnough(e) || sender is not ListBox list) return;
        var clicked = GetDropTarget<EquipmentRowViewModel>(list, e.GetPosition(list));
        if (clicked is null) return;
        var selected = GetSelectedEquipmentIndices();
        if (!selected.Contains(clicked.Index)) selected = new[] { clicked.Index };
        if (selected.Length == 0) return;
        var data = new DataObject(); data.SetData("MH4UModern.EquipmentSlots", selected);
        DragDrop.DoDragDrop(list, data, DragDropEffects.Move);
    }

    private void ItemsGrid_DragOver(object sender, DragEventArgs e)
    {
        if (sender is not ListBox list) return;
        e.Effects = ItemArrangeButton.IsChecked == true && e.Data.GetDataPresent("MH4UModern.ItemSlots") && GetDropTarget<ItemRowViewModel>(list, e.GetPosition(list)) is not null
            ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void EquipmentGrid_DragOver(object sender, DragEventArgs e)
    {
        if (sender is not ListBox list) return;
        e.Effects = EquipmentArrangeButton.IsChecked == true && e.Data.GetDataPresent("MH4UModern.EquipmentSlots") && GetDropTarget<EquipmentRowViewModel>(list, e.GetPosition(list)) is not null
            ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void ItemsGrid_Drop(object sender, DragEventArgs e)
    {
        if (sender is not ListBox list || e.Data.GetData("MH4UModern.ItemSlots") is not int[] sourceIndices) return;
        var target = GetDropTarget<ItemRowViewModel>(list, e.GetPosition(list));
        if (target is null) return;
        RearrangeItems(sourceIndices, target.Index);
        e.Handled = true;
    }

    private void EquipmentGrid_Drop(object sender, DragEventArgs e)
    {
        if (sender is not ListBox list || e.Data.GetData("MH4UModern.EquipmentSlots") is not int[] sourceIndices) return;
        var target = GetDropTarget<EquipmentRowViewModel>(list, e.GetPosition(list));
        if (target is null) return;
        RearrangeEquipment(sourceIndices, target.Index);
        e.Handled = true;
    }

    private static T? GetDropTarget<T>(ListBox list, Point point) where T : class
    {
        var hit = list.InputHitTest(point) as DependencyObject;
        while (hit is not null && hit is not ListBoxItem)
            hit = VisualTreeHelper.GetParent(hit);
        if (hit is not ListBoxItem container) return null;
        return list.ItemContainerGenerator.ItemFromContainer(container) as T;
    }

    private void RearrangeItems(int[] sourceIndices, int targetIndex)
    {
        var sources = sourceIndices.Distinct().Where(i => i >= 0 && i < Items.Count).OrderBy(i => i).ToArray();
        if (sources.Length == 0 || targetIndex < 0 || targetIndex >= Items.Count) return;
        var previous = CaptureItems();
        (ushort Id, ushort Count)[] after;
        int[] destinationIndices;

        if (sources.Length == 1)
        {
            if (sources[0] == targetIndex) return;
            after = previous.ToArray();
            (after[sources[0]], after[targetIndex]) = (after[targetIndex], after[sources[0]]);
            destinationIndices = new[] { targetIndex };
        }
        else
        {
            var origin = sources.Contains(_itemDragOriginIndex) ? _itemDragOriginIndex : sources[0];
            var requestedDelta = targetIndex - origin;
            var delta = Math.Clamp(requestedDelta, -sources.Min(), Items.Count - 1 - sources.Max());
            if (delta == 0) return;
            destinationIndices = sources.Select(i => i + delta).ToArray();
            after = TranslateSelection(previous, sources, destinationIndices);
        }

        if (previous.SequenceEqual(after)) return;
        ApplyItems(after);
        RecordBulkItemEdit(previous, after, sources.Length == 1 ? "swap item slots" : $"move {sources.Length} item slots");
        RefreshItemPage();
        SelectItemIndices(destinationIndices);
        SetStatus(sources.Length == 1 ? "Item slots swapped." : $"Moved {sources.Length} selected item slots without changing their relative spacing.", "AccentBrush");
    }

    private static T[] TranslateSelection<T>(T[] previous, int[] sources, int[] destinations)
    {
        var after = previous.ToArray();
        var sourceSet = sources.ToHashSet();
        var destinationSet = destinations.ToHashSet();
        var moving = sources.Select(index => previous[index]).ToArray();

        // Write selected values into their translated destinations first.
        for (var i = 0; i < destinations.Length; i++)
            after[destinations[i]] = moving[i];

        // Slots pushed out of the destination footprint are swapped back into the vacated footprint.
        // This keeps every unselected item/equipment entry instead of deleting or auto-sorting it.
        var vacated = sources.Where(index => !destinationSet.Contains(index)).OrderBy(index => index).ToArray();
        var displaced = destinations.Where(index => !sourceSet.Contains(index)).OrderBy(index => index).ToArray();
        var displacedValues = displaced.Select(index => previous[index]).ToArray();
        for (var i = 0; i < Math.Min(vacated.Length, displacedValues.Length); i++)
            after[vacated[i]] = displacedValues[i];

        return after;
    }

    private void SelectItemIndices(IEnumerable<int> indices)
    {
        ItemsGrid.SelectedItems.Clear();
        ItemsGridRight.SelectedItems.Clear();
        foreach (var index in indices.Distinct())
        {
            if (index >= _itemPageIndex * BoxPageSize && index < (_itemPageIndex + 1) * BoxPageSize) ItemsGrid.SelectedItems.Add(Items[index]);
            if (ItemArrangeButton.IsChecked == true && index >= _itemRightPageIndex * BoxPageSize && index < (_itemRightPageIndex + 1) * BoxPageSize) ItemsGridRight.SelectedItems.Add(Items[index]);
        }
    }

    private byte[][] CaptureEquipmentState() => Equipment.Select(r => r.Model.Serialize().ToArray()).ToArray();

    private void ApplyEquipmentState(byte[][] state)
    {
        if (_character is null) return;
        for (var i = 0; i < Math.Min(state.Length, Equipment.Count); i++)
        {
            var model = MonHunEquip.Create(state[i].ToArray());
            SetEquipmentModelAtIndex(i, model);
        }
        RefreshEquipmentPage();
        RefreshValidation();
    }

    private void RearrangeEquipment(int[] sourceIndices, int targetIndex)
    {
        var sources = sourceIndices.Distinct().Where(i => i >= 0 && i < Equipment.Count).OrderBy(i => i).ToArray();
        if (sources.Length == 0 || targetIndex < 0 || targetIndex >= Equipment.Count) return;

        var previous = CaptureEquipmentState();
        var beforeRefs = _character is null ? null : EquipmentReferenceService.Capture(_character);
        byte[][] after;
        int[] destinationIndices;
        var oldToNew = new Dictionary<int, int>();

        if (sources.Length == 1)
        {
            if (sources[0] == targetIndex) return;
            after = previous.Select(x => x.ToArray()).ToArray();
            (after[sources[0]], after[targetIndex]) = (after[targetIndex], after[sources[0]]);
            destinationIndices = new[] { targetIndex };
            oldToNew[sources[0]] = targetIndex;
            oldToNew[targetIndex] = sources[0];
        }
        else
        {
            var origin = sources.Contains(_equipmentDragOriginIndex) ? _equipmentDragOriginIndex : sources[0];
            var requestedDelta = targetIndex - origin;
            var delta = Math.Clamp(requestedDelta, -sources.Min(), Equipment.Count - 1 - sources.Max());
            if (delta == 0) return;
            destinationIndices = sources.Select(i => i + delta).ToArray();
            after = TranslateEquipmentSelection(previous, sources, destinationIndices);

            for (var i = 0; i < sources.Length; i++) oldToNew[sources[i]] = destinationIndices[i];
            var sourceSet = sources.ToHashSet();
            var destinationSet = destinationIndices.ToHashSet();
            var vacated = sources.Where(index => !destinationSet.Contains(index)).OrderBy(index => index).ToArray();
            var displaced = destinationIndices.Where(index => !sourceSet.Contains(index)).OrderBy(index => index).ToArray();
            for (var i = 0; i < Math.Min(vacated.Length, displaced.Length); i++) oldToNew[displaced[i]] = vacated[i];
        }

        if (EquipmentStatesEqual(previous, after)) return;
        var afterRefs = beforeRefs is null ? null : EquipmentReferenceService.Remap(beforeRefs, oldToNew);
        if (afterRefs is not null) ApplyEquipmentStateAndReferences(after, afterRefs);
        else ApplyEquipmentState(after);
        _dirty.MarkDirty();
        _undo.Record(
            () => { if (beforeRefs is not null) ApplyEquipmentStateAndReferences(previous, beforeRefs); else ApplyEquipmentState(previous); },
            () => { if (afterRefs is not null) ApplyEquipmentStateAndReferences(after, afterRefs); else ApplyEquipmentState(after); },
            sources.Length == 1 ? "swap equipment slots" : $"move {sources.Length} equipment slots");
        SelectEquipmentIndices(destinationIndices);
        SetStatus(sources.Length == 1 ? "Equipment slots swapped." : $"Moved {sources.Length} selected equipment slots without changing their relative spacing.", "AccentBrush");
    }

    private static byte[][] TranslateEquipmentSelection(byte[][] previous, int[] sources, int[] destinations)
    {
        var after = previous.Select(x => x.ToArray()).ToArray();
        var sourceSet = sources.ToHashSet();
        var destinationSet = destinations.ToHashSet();
        var moving = sources.Select(index => previous[index].ToArray()).ToArray();

        for (var i = 0; i < destinations.Length; i++)
            after[destinations[i]] = moving[i];

        var vacated = sources.Where(index => !destinationSet.Contains(index)).OrderBy(index => index).ToArray();
        var displaced = destinations.Where(index => !sourceSet.Contains(index)).OrderBy(index => index).ToArray();
        var displacedValues = displaced.Select(index => previous[index].ToArray()).ToArray();
        for (var i = 0; i < Math.Min(vacated.Length, displacedValues.Length); i++)
            after[vacated[i]] = displacedValues[i];

        return after;
    }

    private static bool EquipmentStatesEqual(byte[][] a, byte[][] b)
        => a.Length == b.Length && a.Zip(b, (x, y) => x.SequenceEqual(y)).All(equal => equal);

    private void SelectEquipmentIndices(IEnumerable<int> indices)
    {
        EquipmentGrid.SelectedItems.Clear();
        EquipmentGridRight.SelectedItems.Clear();
        foreach (var index in indices.Distinct())
        {
            if (index >= _equipmentPageIndex * BoxPageSize && index < (_equipmentPageIndex + 1) * BoxPageSize) EquipmentGrid.SelectedItems.Add(Equipment[index]);
            if (EquipmentArrangeButton.IsChecked == true && index >= _equipmentRightPageIndex * BoxPageSize && index < (_equipmentRightPageIndex + 1) * BoxPageSize) EquipmentGridRight.SelectedItems.Add(Equipment[index]);
        }
    }

    // Item box
    private void ItemSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshItemSearchSuggestions();
    }

    private bool FilterItem(object obj) => obj is ItemRowViewModel;

    private void SetAllItems99_Click(object sender, RoutedEventArgs e)
    {
        if (_character is null) return;
        var before = CaptureItems();
        foreach (var row in Items)
            if (row.IsOccupied) row.SetSilently(row.ID, 99);
        var after = CaptureItems();
        RecordBulkItemEdit(before, after, "set all item counts to 99");
    }

    private void SetSelectedItems99_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedItemIndices().Select(i => Items[i]).ToArray();
        if (selected.Length == 0) return;
        var before = CaptureItems();
        foreach (var row in selected)
            if (row.IsOccupied) row.SetSilently(row.ID, 99);
        var after = CaptureItems();
        RecordBulkItemEdit(before, after, "set selected item counts to 99");
    }

    private void ClearSelectedItems_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedItemIndices().Select(i => Items[i]).ToArray();
        if (selected.Length == 0) return;
        var before = CaptureItems();
        foreach (var row in selected) row.SetSilently(0, 0);
        var after = CaptureItems();
        RecordBulkItemEdit(before, after, "clear selected item slots");
    }

    private void ApplySelectedItemAmount_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedItemIndices().Select(i => Items[i]).ToArray();
        if (selected.Length == 0)
        {
            MessageBox.Show(this, "Select one or more item rows first.", "Bulk quantity", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!ushort.TryParse(BulkItemAmountBox.Text.Trim(), out var amount) || amount > 99)
        {
            MessageBox.Show(this, "Enter a quantity from 0 to 99.", "Bulk quantity", MessageBoxButton.OK, MessageBoxImage.Warning);
            BulkItemAmountBox.Focus();
            BulkItemAmountBox.SelectAll();
            return;
        }

        var before = CaptureItems();
        foreach (var row in selected)
        {
            if (row.IsOccupied) row.SetSilently(row.ID, amount);
        }
        var after = CaptureItems();
        RecordBulkItemEdit(before, after, $"set selected item counts to {amount}");
    }


    private void SortItems_Click(object sender, RoutedEventArgs e)
    {
        if (_character is null) return;
        var before = CaptureItems();
        var sorted = Items
            .Where(r => r.IsOccupied)
            .Select(r => (r.ID, r.Count, r.Name))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ID)
            .ToArray();

        for (var i = 0; i < Items.Count; i++)
        {
            if (i < sorted.Length) Items[i].SetSilently(sorted[i].ID, sorted[i].Count);
            else Items[i].SetSilently(0, 0);
        }

        var after = CaptureItems();
        RecordBulkItemEdit(before, after, "sort the item box A–Z");
        _itemsView?.Refresh();
    }

    private void RemoveDupes_Click(object sender, RoutedEventArgs e)
    {
        if (_character is null) return;
        var before = CaptureItems();
        var unique = new List<(ushort Id, ushort Count)>();
        var seen = new HashSet<ushort>();
        foreach (var row in Items)
        {
            if (!row.IsOccupied || !seen.Add(row.ID)) continue;
            unique.Add((row.ID, row.Count));
        }

        for (var i = 0; i < Items.Count; i++)
        {
            if (i < unique.Count) Items[i].SetSilently(unique[i].Id, unique[i].Count);
            else Items[i].SetSilently(0, 0);
        }
        var after = CaptureItems();
        RecordBulkItemEdit(before, after, "remove duplicate items and compact");
        _itemsView?.Refresh();
    }

    private (ushort Id, ushort Count)[] CaptureItems() => Items.Select(r => (r.ID, r.Count)).ToArray();

    private void ApplyItems((ushort Id, ushort Count)[] state)
    {
        for (var i = 0; i < Math.Min(state.Length, Items.Count); i++)
            Items[i].SetSilently(state[i].Id, state[i].Count);
        _itemsView?.Refresh();
        RefreshItemPage();
        if (_activeItemEditorRow is not null) RefreshItemInspector(_activeItemEditorRow, 1);
    }

    private void RecordBulkItemEdit((ushort Id, ushort Count)[] before, (ushort Id, ushort Count)[] after, string description)
    {
        if (before.SequenceEqual(after)) return;
        _dirty.MarkDirty();
        _undo.Record(() => ApplyItems(before), () => ApplyItems(after), description);
        RefreshItemPage();
        if (_activeItemEditorRow is not null) RefreshItemInspector(_activeItemEditorRow, 1);
    }

    private void EquipmentSortButton_Click(object sender, RoutedEventArgs e)
    {
        EquipmentSortPopup.IsOpen = !EquipmentSortPopup.IsOpen;
    }

    private void SortEquipmentMonsterSets_Click(object sender, RoutedEventArgs e)
    {
        EquipmentSortPopup.IsOpen = false;
        if (_character is null || Equipment.Count == 0) return;

        var result = MessageBox.Show(
            this,
            "Monster Sets will physically rearrange the equipment box:\n\n" +
            "• armor sets run vertically Head → Chest → Arms → Waist → Legs\n" +
            "• incomplete sets keep the missing-piece spaces empty\n" +
            "• sets are ordered by catalog rarity, then MH4U catalog order\n" +
            "• weapons begin in separate boxes after armor\n" +
            "• talismans begin in separate boxes after weapons\n\n" +
            "The currently equipped references will be remapped so your hunter keeps the same gear. You can Undo the entire sort in one click.\n\nContinue?",
            "Sort equipment · Monster Sets",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        var previous = CaptureEquipmentState();
        var beforeRefs = EquipmentReferenceService.Capture(_character);
        var plan = EquipmentSortService.BuildMonsterSets(Equipment.Select(row => row.Model).ToArray());
        if (!plan.Success)
        {
            MessageBox.Show(this, plan.Error ?? "The Monster Sets layout could not be built.", "Sort equipment", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (EquipmentStatesEqual(previous, plan.State))
        {
            SetStatus("Equipment is already in the Monster Sets layout.", "InfoBrush");
            return;
        }

        var afterRefs = EquipmentReferenceService.Remap(beforeRefs, plan.OldToNew);
        ApplyEquipmentStateAndReferences(plan.State, afterRefs);
        EquipmentGrid.SelectedItems.Clear();
        EquipmentGridRight.SelectedItems.Clear();
        _equipmentPageIndex = 0;
        if (_equipmentRightPageIndex == 0) _equipmentRightPageIndex = 1;
        RefreshEquipmentPage();
        _dirty.MarkDirty();

        _undo.Record(
            () => ApplyEquipmentStateAndReferences(previous, beforeRefs),
            () => ApplyEquipmentStateAndReferences(plan.State, afterRefs),
            "sort equipment by monster sets");

        SetStatus(
            $"Sorted {plan.ArmorSetInstances} armor-set group{(plan.ArmorSetInstances == 1 ? string.Empty : "s")} vertically. Weapons start at Box {plan.WeaponStartBox}; talismans at Box {plan.TalismanStartBox}.",
            "AccentBrush");
    }

    private void ApplyEquipmentStateAndReferences(byte[][] state, EquippedIndexState references)
    {
        if (_character is null) return;
        ApplyEquipmentState(state);
        EquipmentReferenceService.Apply(_character, references);
        EquipmentGrid.SelectedItems.Clear();
        EquipmentGridRight.SelectedItems.Clear();
        RefreshEquipmentPage();
    }

    private void SellSelectedEquipment_Click(object sender, RoutedEventArgs e)
    {
        if (_character is null) return;
        var selected = GetSelectedEquipmentIndices()
            .Where(index => index >= 0 && index < Equipment.Count && Equipment[index].IsOccupied)
            .Distinct()
            .OrderBy(index => index)
            .ToArray();

        if (selected.Length == 0)
        {
            MessageBox.Show(this, "Check at least one occupied equipment slot first.", "Sell selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var equipped = EquipmentReferenceService.Capture(_character);
        var protectedIndices = selected.Where(index => EquipmentReferenceService.IsEquipped(equipped, index)).ToArray();
        var removable = selected.Except(protectedIndices).ToArray();

        if (removable.Length == 0)
        {
            MessageBox.Show(this, "Every selected piece is currently equipped. Equipped pieces are protected from Sell selected.", "Sell selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var warning =
            $"Remove {removable.Length} selected equipment piece{(removable.Length == 1 ? string.Empty : "s")} from the save?" +
            (protectedIndices.Length > 0 ? $"\n\n{protectedIndices.Length} currently equipped piece{(protectedIndices.Length == 1 ? " is" : "s are")} protected and will be skipped." : string.Empty) +
            "\n\nImportant: the bundled MH4U equipment catalog does not contain reliable resale-zenny values, so this action does NOT change Funds. It is a safe bulk remove action and can be undone.";

        if (MessageBox.Show(this, warning, "Sell selected", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var previous = CaptureEquipmentState();
        var after = previous.Select(bytes => bytes.ToArray()).ToArray();
        foreach (var index in removable) after[index] = new byte[28];

        ApplyEquipmentState(after);
        _dirty.MarkDirty();
        _undo.Record(
            () => ApplyEquipmentState(previous),
            () => ApplyEquipmentState(after),
            $"remove {removable.Length} selected equipment pieces");

        EquipmentGrid.SelectedItems.Clear();
        EquipmentGridRight.SelectedItems.Clear();
        SetStatus($"Removed {removable.Length} selected equipment piece{(removable.Length == 1 ? string.Empty : "s")}. Funds unchanged; equipped pieces were protected.", "AccentBrush");
    }

    // Equipment
    private void EquipmentSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshEquipmentSearchSuggestions();
    }

    private bool FilterEquipment(object obj) => obj is EquipmentRowViewModel;

    private static bool IsRelic(MonHunEquip equip)
    {
        if (equip is MonHunWeapon weapon) return weapon.IsRelic;
        var name = SafeEquipmentName(equip);
        return name.Contains("(red)", StringComparison.OrdinalIgnoreCase)
               || name.Contains("(yellow)", StringComparison.OrdinalIgnoreCase)
               || name.Contains("(green)", StringComparison.OrdinalIgnoreCase)
               || name.Contains("(blue)", StringComparison.OrdinalIgnoreCase)
               || name.Contains("(purple)", StringComparison.OrdinalIgnoreCase);
    }

    private void EquipmentGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressOverlayOpen) return;
        if (EquipmentArrangeButton.IsChecked == true) return;
        if (sender is ListBox list && list.SelectedItem is EquipmentRowViewModel row)
            OpenEquipmentOverlay(row);
    }

    private void RefreshEquipmentInspector(EquipmentRowViewModel? row)
    {
        EquipmentFields.Clear();
        _suppressEquipmentSelectors = true;
        _suppressEquipmentValueControls = true;
        try
        {
            WeaponEditorPanel.Visibility = Visibility.Collapsed;
            ArmorEditorPanel.Visibility = Visibility.Collapsed;
            TalismanEditorPanel.Visibility = Visibility.Collapsed;
            EmptyEquipmentEditorPanel.Visibility = Visibility.Collapsed;
            LegacyWeaponTools.Visibility = Visibility.Collapsed;
            EquipmentTypePickerCard.Visibility = Visibility.Collapsed;

            if (row is null)
            {
                SelectedEquipmentTitle.Text = "Select an equipment slot";
                SelectedEquipmentSubtitle.Text = "No slot selected";
                SelectedEquipmentImage.Source = null;
                SelectedEquipmentTypeChip.Text = "Equipment";
                SelectedEquipmentSlotChip.Text = "No slot";
                SelectedEquipmentRarityChip.Text = "Rarity —";
                EquipmentIdCombo.ItemsSource = null;
                EquipmentIdCombo.IsEnabled = false;
                EquipmentTypePickerButton.Content = "Type: Choose…  ▾";
                RefreshEquipmentTypePicker(0);
                EmptyEquipmentEditorPanel.Visibility = Visibility.Visible;
                return;
            }

            SelectedEquipmentTitle.Text = row.IsOccupied ? row.Name : "Empty slot";
            SelectedEquipmentSubtitle.Text = row.IsOccupied
                ? $"ID {row.Model.ID} · Values update live until Save Changes"
                : "Choose a type and equipment ID to populate this slot";
            SelectedEquipmentTypeChip.Text = row.Type;
            SelectedEquipmentSlotChip.Text = $"Slot {row.Slot}";
            SelectedEquipmentRarityChip.Text = row.StoredRarity > 0
                ? $"Rarity {row.StoredRarity}"
                : row.Model is MonHunWeapon craftedWeapon && !craftedWeapon.IsRelic ? "Crafted" : "Rarity —";
            SelectedEquipmentImage.Source = row.Image;

            RefreshEquipmentTypePicker(row.Model.Type);
            EquipmentTypePickerButton.Content = $"Type: {row.Type}  ▾";
            SearchableComboBoxBehavior.Configure(
                EquipmentIdCombo,
                GetEquipmentCatalog(row.Model.Type),
                "IDString",
                "ID",
                row.Model.ID,
                row.Model.Type != 0);

            switch (row.Model)
            {
                case MonHunWeapon weapon:
                    WeaponEditorPanel.Visibility = Visibility.Visible;
                    ConfigureWeaponEditor(row, weapon);
                    break;
                case MonHunArmor armor:
                    ArmorEditorPanel.Visibility = Visibility.Visible;
                    ConfigureArmorEditor(row, armor);
                    break;
                case MonHunTalisman talisman:
                    TalismanEditorPanel.Visibility = Visibility.Visible;
                    ConfigureTalismanEditor(talisman);
                    break;
                default:
                    EmptyEquipmentEditorPanel.Visibility = Visibility.Visible;
                    break;
            }
        }
        finally
        {
            _suppressEquipmentValueControls = false;
            _suppressEquipmentSelectors = false;
        }
    }

    private void RefreshEquipmentTypePicker(int selectedType)
    {
        foreach (var button in EquipmentTypeIconGrid.Children.OfType<ToggleButton>())
        {
            var type = button.Tag switch
            {
                int number => number,
                string text when int.TryParse(text, out var number) => number,
                _ => -1
            };
            button.IsChecked = type == selectedType;
        }
    }

    private static void ConfigureValueCombo(ComboBox combo, IEnumerable? items, string selectedValuePath, object? selectedValue, bool enabled = true, bool searchable = false)
    {
        if (searchable)
        {
            SearchableComboBoxBehavior.Configure(combo, items, "Display", selectedValuePath, selectedValue, enabled);
            return;
        }

        combo.IsEditable = false;
        combo.IsTextSearchEnabled = true;
        combo.DisplayMemberPath = "Display";
        combo.SelectedValuePath = selectedValuePath;
        combo.ItemsSource = items;
        combo.IsEnabled = enabled;
        if (selectedValue is null)
            combo.SelectedIndex = -1;
        else
            combo.SelectedValue = selectedValue;
    }

    private static void ConfigureCheck(CheckBox check, bool value, bool enabled = true)
    {
        check.IsEnabled = enabled;
        check.IsChecked = value;
    }

    private void ConfigureWeaponEditor(EquipmentRowViewModel row, MonHunWeapon weapon)
    {
        ConfigureValueCombo(WeaponSlot1Combo, MonHunEquipStatic.Slot1Values, "Slot1", weapon.Slot1);
        ConfigureValueCombo(WeaponSlot2Combo, MonHunEquipStatic.Slot2Values, "Slot2", weapon.Slot2);
        ConfigureValueCombo(WeaponSlot3Combo, MonHunEquipStatic.Slot3Values, "Slot3", weapon.Slot3);
        ConfigureCheck(WeaponSlot1FixedCheck, weapon.Slot1Fixed);
        ConfigureCheck(WeaponSlot2FixedCheck, weapon.Slot2Fixed);
        ConfigureCheck(WeaponSlot3FixedCheck, weapon.Slot3Fixed);

        // MH4U only stores the full editable relic payload on the colored-name relic weapons.
        // Crafted/normal weapons stay intentionally conservative: decoration slots + honing only.
        var isRelic = weapon.IsRelic;
        var showElement = isRelic && !weapon.IsBowgun;

        NormalWeaponHoningSection.Visibility = isRelic ? Visibility.Collapsed : Visibility.Visible;
        WeaponStatsSection.Visibility = isRelic ? Visibility.Visible : Visibility.Collapsed;
        WeaponMiscSection.Visibility = isRelic ? Visibility.Visible : Visibility.Collapsed;
        WeaponStatsHeader.Text = "RELIC STATS";
        WeaponStatsGrid.Columns = weapon.IsBowgun ? 3 : 5;
        WeaponElementTypeField.Visibility = showElement ? Visibility.Visible : Visibility.Collapsed;
        WeaponElementValueField.Visibility = showElement ? Visibility.Visible : Visibility.Collapsed;
        WeaponSharpnessField.Visibility = Visibility.Visible;
        WeaponHoningField.Visibility = Visibility.Visible;
        WeaponModifierField.Visibility = Visibility.Visible;

        ConfigureValueCombo(WeaponElementTypeCombo, MonHunEquipStatic.ElementTypes, "ElementType", weapon.ElementType, showElement);
        ConfigureValueCombo(WeaponElementValueCombo, weapon.ElementValueList, "ElementValue", weapon.ElementValue, showElement && weapon.HasElement && weapon.ElementValueList.Count > 0);

        WeaponSharpnessLabel.Text = weapon.IsBowgun ? "Magazine Sizes" : weapon.IsBow ? "Coatings" : "Sharpness";
        ConfigureValueCombo(WeaponSharpnessCombo, weapon.SharpnessValueList, "Sharpness", weapon.Sharpness, isRelic);
        ConfigureValueCombo(WeaponHoningCombo, MonHunEquipStatic.HoningValues, "Honing", weapon.Honing, isRelic);
        ConfigureValueCombo(NormalWeaponHoningCombo, MonHunEquipStatic.HoningValues, "Honing", weapon.Honing, !isRelic);
        ConfigureValueCombo(WeaponModifierCombo, weapon.ModifierValueList, "Modifier", weapon.Modifier, isRelic);

        ConfigureValueCombo(WeaponUpgradeCombo, weapon.UpgradeValueList, "Upgrade", weapon.Upgrade, isRelic);
        WeaponSpecialLabel.Text = weapon.SpecialValueName;
        ConfigureValueCombo(WeaponSpecialCombo, weapon.SpecialValueList, "Special", weapon.Special, isRelic && weapon.SpecialValueList.Count > 0);
        WeaponKinsectField.Visibility = isRelic && weapon.IsGlaive ? Visibility.Visible : Visibility.Collapsed;
        ConfigureValueCombo(WeaponKinsectLevelCombo, MonHunEquipStatic.KinsectLevels, "KinsectLevel", weapon.KinsectLevel, isRelic && weapon.IsGlaive);
        ConfigureValueCombo(WeaponNumSlotsCombo, MonHunEquipStatic.NumSlotsValues, "NumSlots", weapon.NumSlots, isRelic);

        object? rarityValue = isRelic ? weapon.Rarity : null;
        ConfigureValueCombo(WeaponRarityCombo, MonHunEquipStatic.RarityValues, "Rarity", rarityValue, isRelic);
        ConfigureValueCombo(WeaponPolishReqCombo, MonHunEquipStatic.PolishReqValues, "PolishReq", weapon.PolishReq, isRelic);
        ConfigureCheck(WeaponPolishedCheck, weapon.Polished, isRelic);
        ConfigureCheck(WeaponGlowCheck, weapon.Glow, isRelic);

        var weaponCatalog = row.WeaponCatalog;
        WeaponRarityHint.Text = isRelic
            ? $"Relic rarity stored in save: {weapon.Rarity + 1}"
            : weaponCatalog is not null
                ? $"Catalog: Rarity {weaponCatalog.Rarity} · {weaponCatalog.Summary}. Only Decorations and Honing are editable; relic-only bytes stay hidden."
                : "Crafted weapon: only Decorations and Honing are exposed. Relic-only bytes are hidden to avoid invalid equipment.";

        LegacyWeaponTools.Visibility = isRelic ? Visibility.Visible : Visibility.Collapsed;
        if (isRelic) ConfigureLegacyWeaponTools(row.Model.Type);
    }

    private void ConfigureArmorEditor(EquipmentRowViewModel row, MonHunArmor armor)
    {
        ConfigureValueCombo(ArmorSlot1Combo, MonHunEquipStatic.Slot1Values, "Slot1", armor.Slot1);
        ConfigureValueCombo(ArmorSlot2Combo, MonHunEquipStatic.Slot2Values, "Slot2", armor.Slot2);
        ConfigureValueCombo(ArmorSlot3Combo, MonHunEquipStatic.Slot3Values, "Slot3", armor.Slot3);
        ConfigureCheck(ArmorSlot1FixedCheck, armor.Slot1Fixed);
        ConfigureCheck(ArmorSlot2FixedCheck, armor.Slot2Fixed);
        ConfigureCheck(ArmorSlot3FixedCheck, armor.Slot3Fixed);

        ConfigureValueCombo(ArmorUpgradeCombo, MonHunEquipStatic.UpgradeArmorValues, "Upgrade", armor.Upgrade);
        ConfigureValueCombo(ArmorResistancesCombo, MonHunEquipStatic.ResistancesValues, "Resistances", armor.Resistances);
        ConfigureValueCombo(ArmorDefenseCombo, MonHunEquipStatic.DefenseValues, "Defense", armor.Defense);
        ConfigureValueCombo(ArmorNumSlotsCombo, MonHunEquipStatic.NumSlotsValues, "NumSlots", armor.NumSlots);

        var catalogRarity = ArmorRarityService.GetDisplayRarity(row.Model);
        object? rarityValue = armor.Rarity > 0 ? armor.Rarity : catalogRarity > 0 ? catalogRarity - 1 : null;
        ConfigureValueCombo(ArmorRarityCombo, MonHunEquipStatic.RarityValues, "Rarity", rarityValue);
        ConfigureValueCombo(ArmorPolishReqCombo, MonHunEquipStatic.PolishReqValues, "PolishReq", armor.PolishReq);
        ConfigureCheck(ArmorPolishedCheck, armor.Polished);
        ConfigureCheck(ArmorGlowCheck, armor.Glow);

        var armorCatalog = row.ArmorCatalog;
        ArmorRarityHint.Text = armorCatalog is not null
            ? $"Catalog: Rarity {armorCatalog.Rarity} · {armorCatalog.Summary}"
            : armor.Rarity > 0
                ? $"Stored equipment rarity: {armor.Rarity + 1}."
                : catalogRarity > 0
                    ? $"Catalog rarity auto-detected as Rarity {catalogRarity}."
                    : "Rarity metadata could not be resolved for this armor ID.";
    }

    private void ConfigureTalismanEditor(MonHunTalisman talisman)
    {
        ConfigureValueCombo(TalismanSlot1Combo, MonHunEquipStatic.Slot1Values, "Slot1", talisman.Slot0);
        ConfigureValueCombo(TalismanSlot2Combo, MonHunEquipStatic.Slot2Values, "Slot2", talisman.Slot1);
        ConfigureValueCombo(TalismanSlot3Combo, MonHunEquipStatic.Slot3Values, "Slot3", talisman.Slot2);
        ConfigureValueCombo(TalismanNumSlotsCombo, MonHunEquipStatic.NumSlotsValues, "NumSlots", talisman.NumSlots);
        ConfigureValueCombo(TalismanSkill1Combo, MonHunEquipStatic.Skill1IdValues, "Skill1Id", talisman.Skill1ID, searchable: true);
        ConfigureValueCombo(TalismanSkill1AmountCombo, MonHunEquipStatic.Skill1AmountValues, "Skill1Amount", talisman.Skill1Amount);
        ConfigureValueCombo(TalismanSkill2Combo, MonHunEquipStatic.Skill2IdValues, "Skill2Id", talisman.Skill2ID, searchable: true);
        ConfigureValueCombo(TalismanSkill2AmountCombo, MonHunEquipStatic.Skill2AmountValues, "Skill2Amount", talisman.Skill2Amount);
    }

    private void DecorationCombo_DropDownOpened(object sender, EventArgs e)
    {
        if (_suppressEquipmentValueControls || _activeEquipmentEditorRow is not EquipmentRowViewModel row) return;
        if (sender is not ComboBox { Tag: string propertyName } combo) return;

        // Replace the enormous flat jewel list with a skill-first picker.
        combo.IsDropDownOpen = false;
        ushort currentId = 0;
        try
        {
            if (combo.SelectedValue is not null)
                currentId = Convert.ToUInt16(combo.SelectedValue, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch { currentId = 0; }

        var picker = new DecorationPickerWindow(currentId) { Owner = this };
        if (picker.ShowDialog() != true) return;
        ApplyEquipmentProperty(row, propertyName, picker.SelectedDecorationId);
    }

    private void EquipmentTypePickerButton_Click(object sender, RoutedEventArgs e)
    {
        EquipmentTypePickerCard.Visibility = EquipmentTypePickerCard.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void EquipmentTypeIcon_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressEquipmentSelectors || _character is null) return;
        if (_activeEquipmentEditorRow is not EquipmentRowViewModel row) return;
        if (sender is not ToggleButton button) return;
        if (!int.TryParse(button.Tag?.ToString(), out var newType)) return;

        if (row.Model.Type == newType)
        {
            RefreshEquipmentTypePicker(newType);
            EquipmentTypePickerCard.Visibility = Visibility.Collapsed;
            return;
        }

        var oldBytes = row.Model.Serialize().ToArray();
        row.Model.Type = newType;
        MonHunEquip newModel = row.Model.Kind != row.Model.KindLast
            ? MonHunEquip.Create(row.Model.SerializeOnlyTypeAndId())
            : row.Model;

        SetEquipmentModelAtIndex(row.Index, newModel);
        var newBytes = newModel.Serialize().ToArray();
        _dirty.MarkDirty();
        _undo.Record(
            () => ApplyEquipmentBytes(row.Index, oldBytes),
            () => ApplyEquipmentBytes(row.Index, newBytes),
            $"equipment type in slot {row.Slot}");
        RefreshEquipmentInspector(Equipment[row.Index]);
        EquipmentTypePickerCard.Visibility = Visibility.Collapsed;
        _equipmentView?.Refresh();
    }

    private void EquipmentIdCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEquipmentSelectors || _character is null) return;
        if (_activeEquipmentEditorRow is not EquipmentRowViewModel row) return;
        if (EquipmentIdCombo.SelectedItem is not MonHunEquip option) return;
        if (row.Model.ID == option.ID) return;

        var oldBytes = row.Model.Serialize().ToArray();
        row.Model.ID = option.ID;
        row.Refresh();
        var newBytes = row.Model.Serialize().ToArray();
        _dirty.MarkDirty();
        _undo.Record(
            () => ApplyEquipmentBytes(row.Index, oldBytes),
            () => ApplyEquipmentBytes(row.Index, newBytes),
            $"equipment ID in slot {row.Slot}");
        RefreshEquipmentInspector(row);
        _equipmentView?.Refresh();
    }

    private void EquipmentValueCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEquipmentValueControls) return;
        if (_activeEquipmentEditorRow is not EquipmentRowViewModel row) return;
        if (sender is not ComboBox { Tag: string propertyName } combo || combo.SelectedValue is null) return;
        ApplyEquipmentProperty(row, propertyName, combo.SelectedValue);
    }

    private void EquipmentValueCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEquipmentValueControls) return;
        if (_activeEquipmentEditorRow is not EquipmentRowViewModel row) return;
        if (sender is not CheckBox { Tag: string propertyName } check) return;
        ApplyEquipmentProperty(row, propertyName, check.IsChecked == true);
    }

    private void ApplyEquipmentProperty(EquipmentRowViewModel row, string propertyName, object rawValue)
    {
        var property = FindEquipmentProperty(row.Model, propertyName);
        if (property is null || !property.CanWrite) return;
        object? converted;
        try
        {
            var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            converted = propertyType.IsInstanceOfType(rawValue)
                ? rawValue
                : Convert.ChangeType(rawValue, propertyType, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            return;
        }

        var oldValue = property.GetValue(row.Model);
        if (Equals(oldValue, converted)) return;
        var oldBytes = row.Model.Serialize().ToArray();
        try
        {
            property.SetValue(row.Model, converted);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.GetBaseException().Message, "Could not edit equipment value", MessageBoxButton.OK, MessageBoxImage.Warning);
            RefreshEquipmentInspector(row);
            return;
        }

        row.Refresh();
        var newBytes = row.Model.Serialize().ToArray();
        if (oldBytes.SequenceEqual(newBytes))
        {
            RefreshEquipmentInspector(row);
            return;
        }

        _dirty.MarkDirty();
        _undo.Record(
            () => ApplyEquipmentBytes(row.Index, oldBytes),
            () => ApplyEquipmentBytes(row.Index, newBytes),
            $"{propertyName} in equipment slot {row.Slot}");
        RefreshEquipmentInspector(row);
        _equipmentView?.Refresh();
        RefreshValidation();
    }

    private static PropertyInfo? FindEquipmentProperty(MonHunEquip equipment, string propertyName)
    {
        var targetType = equipment.GetType();
        return targetType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.Name == propertyName)
            .OrderByDescending(p => p.DeclaringType == targetType)
            .FirstOrDefault();
    }

    private IEnumerable<MonHunEquip> GetEquipmentCatalog(int type) => type switch
    {
        1 => MonHunEquip.equipChest,
        2 => MonHunEquip.equipArms,
        3 => MonHunEquip.equipWaist,
        4 => MonHunEquip.equipLegs,
        5 => MonHunEquip.equipHead,
        6 => MonHunEquip.equipTalisman,
        7 => MonHunEquip.equipGreatsword,
        8 => MonHunEquip.equipSns,
        9 => MonHunEquip.equipHammer,
        10 => MonHunEquip.equipLance,
        11 => MonHunEquip.equipLbg,
        12 => MonHunEquip.equipHbg,
        13 => MonHunEquip.equipLongsword,
        14 => MonHunEquip.equipSwitchaxe,
        15 => MonHunEquip.equipGunlance,
        16 => MonHunEquip.equipBow,
        17 => MonHunEquip.equipDualblade,
        18 => MonHunEquip.equipHuntinghorn,
        19 => MonHunEquip.equipInsectglaive,
        20 => MonHunEquip.equipChargeblade,
        _ => MonHunEquip.equipTypes.Take(1)
    };


    private void ConfigureLegacyWeaponTools(int type)
    {
        if (type == 11)
        {
            LegacyWeaponTools.Visibility = Visibility.Visible;
            LegacyPatternButton.Content = "Magazine sizes…";
            LegacySpecialButton.Content = "Shots available…";
            LegacySpecialButton.Visibility = Visibility.Visible;
        }
        else if (type == 12)
        {
            LegacyWeaponTools.Visibility = Visibility.Visible;
            LegacyPatternButton.Content = "Magazine sizes…";
            LegacySpecialButton.Content = "Shots available…";
            LegacySpecialButton.Visibility = Visibility.Visible;
        }
        else if (type == 16)
        {
            LegacyWeaponTools.Visibility = Visibility.Visible;
            LegacyPatternButton.Content = "Bow charge / coating pattern…";
            LegacySpecialButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            LegacyWeaponTools.Visibility = Visibility.Collapsed;
        }
    }

    private void LegacyPattern_Click(object sender, RoutedEventArgs e)
    {
        if (_character is null || _activeEquipmentEditorRow is not EquipmentRowViewModel row || row.Model is not MonHunWeapon weapon) return;
        Window? dialog = row.Model.Type switch
        {
            11 => new LbgInfoCDialog(false),
            12 => new LbgInfoCDialog(true),
            16 => new BowInfoCDialog(true),
            _ => null
        };
        if (dialog is null) return;
        dialog.Owner = this;
        if (dialog.ShowDialog() != true) return;

        var selectorName = row.Model.Type == 16 ? "DataCoatingsSel" : "DataMagSel";
        if (dialog.FindName(selectorName) is not ComboBox selector || selector.SelectedItem is null) return;
        if (!TryGetLegacyValue(selector.SelectedItem, out var value)) return;

        var oldBytes = row.Model.Serialize().ToArray();
        weapon.Sharpness = value;
        row.Refresh();
        var newBytes = row.Model.Serialize().ToArray();
        _dirty.MarkDirty();
        _undo.Record(() => ApplyEquipmentBytes(row.Index, oldBytes), () => ApplyEquipmentBytes(row.Index, newBytes), $"visual weapon pattern in slot {row.Slot}");
        RefreshEquipmentInspector(row);
    }

    private void LegacySpecial_Click(object sender, RoutedEventArgs e)
    {
        if (_character is null || _activeEquipmentEditorRow is not EquipmentRowViewModel row || row.Model is not MonHunWeapon weapon) return;
        Window? dialog = row.Model.Type switch
        {
            11 => new LbgInfoFDialog(),
            12 => new HbgInfoFDialog(),
            _ => null
        };
        if (dialog is null) return;
        dialog.Owner = this;
        if (dialog.ShowDialog() != true) return;
        if (dialog.FindName("DataShotSel") is not ComboBox selector || selector.SelectedItem is null) return;
        if (!TryGetLegacyValue(selector.SelectedItem, out var value)) return;

        var oldBytes = row.Model.Serialize().ToArray();
        weapon.Special = value;
        row.Refresh();
        var newBytes = row.Model.Serialize().ToArray();
        _dirty.MarkDirty();
        _undo.Record(() => ApplyEquipmentBytes(row.Index, oldBytes), () => ApplyEquipmentBytes(row.Index, newBytes), $"visual weapon special in slot {row.Slot}");
        RefreshEquipmentInspector(row);
    }

    private static bool TryGetLegacyValue(object selectedItem, out byte value)
    {
        value = 0;
        var property = selectedItem.GetType().GetProperty("Val");
        if (property is null) return false;
        try
        {
            value = Convert.ToByte(property.GetValue(selectedItem));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void CopyEquipment_Click(object sender, RoutedEventArgs e)
    {
        if (_activeEquipmentEditorRow is not EquipmentRowViewModel row) return;
        _equipmentClipboard = row.Model.Serialize().ToArray();
        SetStatus($"Copied equipment slot {row.Slot}.", "InfoBrush");
    }

    private void PasteEquipment_Click(object sender, RoutedEventArgs e)
    {
        if (_equipmentClipboard is null || _character is null) return;
        if (_activeEquipmentEditorRow is not EquipmentRowViewModel row) return;
        var oldBytes = row.Model.Serialize().ToArray();
        var newBytes = _equipmentClipboard.ToArray();
        ApplyEquipmentBytes(row.Index, newBytes);
        _dirty.MarkDirty();
        _undo.Record(() => ApplyEquipmentBytes(row.Index, oldBytes), () => ApplyEquipmentBytes(row.Index, newBytes), $"paste equipment into slot {row.Slot}");
    }

    private void DuplicateEquipment_Click(object sender, RoutedEventArgs e)
    {
        if (_character is null || _activeEquipmentEditorRow is not EquipmentRowViewModel source || !source.IsOccupied) return;

        var target = Equipment.FirstOrDefault(row => !row.IsOccupied && row.Index != source.Index);
        if (target is null)
        {
            MessageBox.Show(this, "There are no empty equipment slots available.", "Duplicate equipment", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var oldBytes = target.Model.Serialize().ToArray();
        var newBytes = source.Model.Serialize().ToArray();
        ApplyEquipmentBytes(target.Index, newBytes);
        _dirty.MarkDirty();
        _undo.Record(
            () => ApplyEquipmentBytes(target.Index, oldBytes),
            () => ApplyEquipmentBytes(target.Index, newBytes),
            $"duplicate equipment to slot {target.Slot}");

        GoToEquipmentSlot(target);
        SetStatus($"Duplicated to first empty equipment slot: {target.Slot}.", "InfoBrush");
    }

    private void ClearEquipment_Click(object sender, RoutedEventArgs e)
    {
        if (_character is null || _activeEquipmentEditorRow is not EquipmentRowViewModel row) return;
        var oldBytes = row.Model.Serialize().ToArray();
        var newBytes = new byte[28];
        ApplyEquipmentBytes(row.Index, newBytes);
        _dirty.MarkDirty();
        _undo.Record(() => ApplyEquipmentBytes(row.Index, oldBytes), () => ApplyEquipmentBytes(row.Index, newBytes), $"clear equipment slot {row.Slot}");
    }

    private void SetEquipmentModelAtIndex(int index, MonHunEquip model)
    {
        if (_character is null || index < 0 || index >= Equipment.Count) return;
        if (index < _character.EquipBox.Length)
            _character.EquipBox[index] = model;
        else
        {
            var tailIndex = index - ExtendedEquipmentService.LegacySlotCount;
            if (tailIndex < 0 || tailIndex >= _equipmentTail.Length) return;
            _equipmentTail[tailIndex] = model;
        }
        Equipment[index].ReplaceSilently(model);
    }

    private void ApplyEquipmentBytes(int index, byte[] bytes)
    {
        if (_character is null || index < 0 || index >= Equipment.Count) return;
        var model = MonHunEquip.Create(bytes.ToArray());
        SetEquipmentModelAtIndex(index, model);
        if (_activeEquipmentEditorRow is EquipmentRowViewModel selected && selected.Index == index)
        {
            _activeEquipmentEditorRow = Equipment[index];
            RefreshEquipmentInspector(Equipment[index]);
        }
        _equipmentView?.Refresh();
    }

    // Quests and Palicos
    private void QuestList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressOverlayOpen) return;
        if (QuestList.SelectedItem is not QuestRowViewModel row) return;
        OpenQuestOverlay(row);
    }

    private void OpenQuestOverlay(QuestRowViewModel row)
    {
        _activeQuestEditorRow = row;
        _activeItemEditorRow = null;
        _activeEquipmentEditorRow = null;

        QuestFields.Clear();
        RefreshQuestOverlayHeader(row);
        foreach (var field in _inspector.ForQuest(row.Model, () =>
                 {
                     row.Refresh();
                     RefreshQuestOverlayHeader(row);
                     RefreshValidation();
                 }))
            QuestFields.Add(field);

        ItemEditorOverlayPanel.Visibility = Visibility.Collapsed;
        EquipmentEditorOverlayPanel.Visibility = Visibility.Collapsed;
        QuestEditorOverlayPanel.Visibility = Visibility.Visible;
        EditorOverlay.Visibility = Visibility.Visible;
    }

    private void RefreshQuestOverlayHeader(QuestRowViewModel row)
    {
        QuestInspectorTitle.Text = row.Summary;
        QuestSlotChip.Text = $"Quest {row.Slot}";
        QuestLevelChip.Text = row.Level;
        QuestOwnerChip.Text = $"Owner: {row.Owner}";
    }

    private void PalicoList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        PalicoFields.Clear();
        if (PalicoList.SelectedItem is not PalicoRowViewModel row)
        {
            PalicoInspectorTitle.Text = "Select a Palico";
            return;
        }

        try
        {
            PalicoInspectorTitle.Text = $"Palico {row.Slot} · {row.Name}";
            foreach (var field in _inspector.ForPalico(row.Model, () =>
                     {
                         row.Refresh();
                         PalicoInspectorTitle.Text = $"Palico {row.Slot} · {row.Name}";
                     }))
                PalicoFields.Add(field);
        }
        catch (Exception ex)
        {
            App.LogException(ex, $"Palico inspector slot {row.Slot}");
            PalicoInspectorTitle.Text = $"Palico {row.Slot} could not be inspected";
            SetStatus($"Palico {row.Slot} contains data the editor could not display safely.", "WarningBrush");
        }
    }

    // Backup / recovery
    private void RefreshBackups_Click(object sender, RoutedEventArgs e) => RefreshBackups();

    private void RefreshBackups()
    {
        Backups.Clear();
        var path = _save?.FileName;
        foreach (var file in _backupService.GetBackups(path))
            Backups.Add(new BackupRowViewModel(file));
        OnPropertyChanged(nameof(BackupSummary));
    }

    private void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (_save is null || BackupsGrid.SelectedItem is not BackupRowViewModel backup) return;
        var result = MessageBox.Show(this,
            $"Restore this backup over {Path.GetFileName(_save.FileName)}?\n\n{backup.Name}\n\nThe current file will be replaced.",
            "Restore backup", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            var path = _save.FileName;
            _backupService.CreateBackup(path);
            _backupService.RestoreBackup(backup.File.FullName, path);
            LoadSave(path, skipDirtyPrompt: true);
            SetStatus("Backup restored and reloaded.", "AccentBrush");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.GetBaseException().Message, "Restore failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenBackupFolder_Click(object sender, RoutedEventArgs e)
    {
        var dir = _backupService.GetBackupDirectory(_save?.FileName);
        if (dir is null) return;
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
    }

    private void ReloadCurrent_Click(object sender, RoutedEventArgs e)
    {
        if (_save is null) return;
        var path = _save.FileName;
        var result = MessageBox.Show(this, "Discard every unsaved edit and reload the current save from disk?", "Reset changes", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
            LoadSave(path, skipDirtyPrompt: true);
    }

    // Recent files, drag/drop, closing
    private void RefreshRecentFiles()
    {
        RecentFiles.Clear();
        foreach (var path in _recentFilesService.Load())
            RecentFiles.Add(new RecentSaveRowViewModel(path));
    }

    private void RecentFilesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RecentFilesList.SelectedItem is RecentSaveRowViewModel recent && File.Exists(recent.Path))
            LoadSave(recent.Path);
    }


    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (CustomQuestsPage.HandleTopOverlayKey(e)) { e.Handled = true; return; }
        if (e.Key == Key.Escape && EditorOverlay.Visibility == Visibility.Visible)
        {
            CloseEditorOverlay();
            e.Handled = true;
            return;
        }
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        if (CustomQuestsPage.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.S) { CustomQuestsPage.SaveQuest(); e.Handled = true; }
            else if (e.Key == Key.O) { CustomQuestsPage.OpenMibFiles(); e.Handled = true; }
            else if (e.Key == Key.F) { CustomQuestsPage.FocusSearch(); e.Handled = true; }
            return;
        }

        switch (e.Key)
        {
            case Key.O:
                Open_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.S:
                if (SaveButton.IsEnabled) Save_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.Z:
                if (_undo.CanUndo) _undo.Undo();
                e.Handled = true;
                break;
            case Key.Y:
                if (_undo.CanRedo) _undo.Redo();
                e.Handled = true;
                break;
            case Key.F:
                if (ItemsPage.Visibility == Visibility.Visible)
                    ItemSearchBox.Focus();
                else if (EquipmentPage.Visibility == Visibility.Visible)
                    EquipmentSearchBox.Focus();
                e.Handled = true;
                break;
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
        if (files.All(file => string.Equals(Path.GetExtension(file), ".mib", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetExtension(file), ".mhquest", StringComparison.OrdinalIgnoreCase)))
        {
            NavCustomQuests.IsChecked = true;
            CustomQuestsPage.ImportFiles(files);
            e.Handled = true;
            return;
        }
        var path = files[0];
        if (File.Exists(path)) LoadSave(path);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!CustomQuestsPage.ConfirmClose()) { e.Cancel = true; return; }
        if (_dirty.IsDirty && !ConfirmDiscard())
        {
            e.Cancel = true;
            return;
        }
        base.OnClosing(e);
    }

    private bool ConfirmDiscard()
        => MessageBox.Show(this, "You have unsaved changes. Discard them?", "Unsaved changes", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    private static string SafeEquipmentName(MonHunEquip equip)
    {
        try { return equip.IDString; }
        catch { return $"ID {equip.ID}"; }
    }

    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is null) yield break;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }
}
