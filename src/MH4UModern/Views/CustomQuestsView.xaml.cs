using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClassicMhTools.Core.CustomQuests;
using MH4UModern.ViewModels;
using Microsoft.Win32;

namespace MH4UModern.Views;

public partial class CustomQuestsView : UserControl, INotifyPropertyChanged
{
    private QuestExtdataSet? _extdata;
    private CustomQuestEditorViewModel? _editor;
    private QuestCreationViewModel? _creator;
    private CustomQuestLibraryRow? _selected;
    private string? _sourcePath;
    private readonly List<CustomQuestLibraryRow> _all = new();
    private bool _suppressSelection;
    private bool _initialized;
    private bool _manualExtdata;
    private string? _autoContext;
    private Func<bool>? _smokeDiscardConfirmation;
    private Func<QuestPatch, string, bool>? _smokeInstallConfirmation;
    private Func<bool>? _smokeEmulatorRunning;
    private Func<string, bool>? _smokeManagementConfirmation;
    private string _status = "No extdata folder connected";
    private Window Owner => Window.GetWindow(this);
    private string LibraryFolder => Path.Combine(App.IsSmokeTest ? App.SmokeTestDirectory :
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MH4UModern"), "Custom Quest Library");
    public Func<string?>? EmulatorPathProvider { get; set; }
    public Func<string?>? CharacterPathProvider { get; set; }
    public event EventHandler? EditorOpened;
    public event EventHandler? EditorClosed;
    public bool IsEditorOpen { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<CustomQuestLibraryRow> Rows { get; } = new();
    public CustomQuestEditorViewModel? Editor => _editor;
    public QuestCreationViewModel? Creator => _creator;
    public IQuestEditorLayout? EditorLayout => (IQuestEditorLayout?)Creator ?? Editor;
    public bool HasEditor => Editor != null || Creator != null;
    public string EditorTitle => Creator?.Title ?? Editor?.Title ?? "";
    public Visibility CreatorVisibility => Creator != null ? Visibility.Visible : Visibility.Collapsed;
    private bool IsDirty => Creator?.IsDirty == true || Editor?.IsDirty == true || MonsterEditor.IsOpen;
    public bool HasExtdata => _extdata != null;
    public string ExtdataPath => _extdata?.Folder ?? "No extdata folder connected";
    public string Status => _status;
    public string CapacitySummary => _extdata == null ? $"{_all.Count} library / imported quests" :
        string.Join("   |   ", new[] { ("Event", 120), ("Challenge", 40), ("Episodic", 40) }
            .Select(c => $"{c.Item1}: {_extdata.Quests.Count(q => q.Category == c.Item1)} / {c.Item2}"));

    public CustomQuestsView()
    {
        InitializeComponent();
        DataContext = this;
    }

    public FrameworkElement TakeEditorPanel()
    {
        LayoutRoot.Children.Remove(EditorPanel);
        EditorPanel.DataContext = this;
        EditorPanel.Visibility = Visibility.Visible;
        return EditorPanel;
    }

    private void CreatorPicker_Requested(object? sender, QuestChoiceRequest e) => ChoicePicker.Open(e);
    private void CreatorMonster_Requested(object? sender, QuestMonsterRequest e) => MonsterEditor.Open(e);
    public bool HandleTopOverlayKey(KeyEventArgs e) => ChoicePicker.HandleKey(e) || MonsterEditor.HandleKey(e);

    private void CloseEditor_Click(object sender, RoutedEventArgs e) => TryCloseEditor();
    public bool TryCloseEditor()
    {
        if (!ConfirmClose()) return false;
        Select(null);
        if (!App.IsSmokeTest) UseConfiguredEmulatorPath();
        QuestRows.Focus();
        return true;
    }

    private void RaiseState()
    {
        foreach (var name in new[] { nameof(Editor), nameof(Creator), nameof(EditorLayout), nameof(EditorTitle), nameof(CreatorVisibility), nameof(HasEditor), nameof(HasExtdata), nameof(ExtdataPath), nameof(CapacitySummary), nameof(Status) })
            PropertyChanged?.Invoke(this, new(name));
    }

    private void View_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialized || App.IsSmokeTest) return;
        _initialized = true;
        var failed = 0;
        foreach (var path in Directory.Exists(LibraryFolder) ? Directory.EnumerateFiles(LibraryFolder).Where(p =>
            Path.GetExtension(p).Equals(".mib", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(p).Equals(".mhquest", StringComparison.OrdinalIgnoreCase)) : [])
        {
            try { _all.Add(ReadRow(path, "Library")); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { failed++; }
        }
        FilterRows();
        var libraryCount = _all.Count(q => q.Source == "Library");
        if (!HasExtdata) SetStatus(failed == 0 ? $"{libraryCount} library quests" : $"{libraryCount} library quests; {failed} unreadable files");
        else if (failed != 0) SetStatus(Status + $"; {failed} unreadable library files");
        UseConfiguredEmulatorPath();
    }

    private void SetStatus(string text) { _status = text; RaiseState(); }
    private void Run(Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            SetStatus(ex.GetBaseException().Message);
            if (App.IsSmokeTest) throw;
            MessageBox.Show(Owner, ex.GetBaseException().Message, "Custom Quests", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static QuestDocument ReadMib(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > 0x10004) throw new InvalidDataException("Quest file is too large.");
        return new(QuestCrypto.DecodeMib(File.ReadAllBytes(path)));
    }

    private static CustomQuestLibraryRow ReadRow(string path, string source)
    {
        if (!Path.GetExtension(path).Equals(".mhquest", StringComparison.OrdinalIgnoreCase)) return new(ReadMib(path), source, path);
        if (new FileInfo(path).Length > QuestCreationDraft.MaxSourceSize) throw new InvalidDataException("Quest authoring file is too large.");
        var draft = QuestCreationDraft.FromJson(File.ReadAllText(path));
        return new(draft.Build(), source, path, Creation: draft);
    }

    public bool ConfirmClose() => !IsDirty ||
        (App.IsSmokeTest && _smokeDiscardConfirmation != null ? _smokeDiscardConfirmation() :
        MessageBox.Show(Owner, "Discard the current unsaved custom quest edits?", "Custom Quests",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes);

    private void OpenExtdata_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        if (!ConfirmClose()) return;
        var dialog = new OpenFolderDialog { Title = "Select the folder containing quest1 through quest5", Multiselect = false };
        if (dialog.ShowDialog(Owner) == true) Connect(dialog.FolderName);
    });

    private void Connect(string folder, bool manual = true)
    {
        var loaded = QuestExtdataSet.Load(folder);
        _extdata = loaded;
        _manualExtdata = manual;
        _all.RemoveAll(q => q.Source == "Installed");
        _all.AddRange(loaded.Quests.Select(q => new CustomQuestLibraryRow(q.Document, "Installed", q.Location, q.Id)));
        Select(null);
        FilterRows();
        SetStatus($"{loaded.Quests.Count} installed quests; checksums valid");
    }

    private void Azahar_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        if (!ConfirmClose()) return;
        Select(null);
        UseConfiguredEmulatorPath(force: true);
    });

    public void UseConfiguredEmulatorPath(bool force = false)
    {
        if (!force && (_manualExtdata || IsEditorOpen || IsDirty)) return;
        var storage = EmulatorPathProvider?.Invoke();
        var character = CharacterPathProvider?.Invoke();
        var context = storage + "\0" + character;
        if (!force && context == _autoContext) return;
        _autoContext = context;
        _manualExtdata = false;
        try
        {
            if (string.IsNullOrWhiteSpace(storage)) throw new InvalidOperationException("Set an emulator path in Overview first.");
            var folders = QuestExtdataLocator.FindLocations(storage, character);
            if (folders.Count == 1) Connect(folders[0], manual: false);
            else if (folders.Count == 0) throw new DirectoryNotFoundException("No MH4U US/EU quest extdata found under the active emulator path.");
            else throw new InvalidOperationException("Multiple MH4U extdata folders found. Select a character in Overview, or open the intended extdata folder manually.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            _extdata = null;
            _all.RemoveAll(q => q.Source == "Installed");
            Select(null);
            FilterRows();
            SetStatus(ex.GetBaseException().Message);
            if (force) throw;
        }
    }

    private void Reload_Click(object sender, RoutedEventArgs e) => Run(() => { if (_extdata != null && ConfirmClose()) Connect(_extdata.Folder, _manualExtdata); });
    private void Import_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        if (!ConfirmClose()) return;
        var dialog = new OpenFileDialog { Filter = "MH4U quest / authoring source|*.mib;*.mhquest|All files|*.*", Multiselect = true };
        if (dialog.ShowDialog(Owner) != true) return;
        ImportValidatedFiles(dialog.FileNames);
    });

    public void OpenMibFiles() => Import_Click(this, new RoutedEventArgs());
    public void FocusSearch() { if (!IsEditorOpen || TryCloseEditor()) SearchBox.Focus(); }
    public void ImportFiles(string[] paths) => Run(() => { if (ConfirmClose()) ImportValidatedFiles(paths); });
    private void ImportValidatedFiles(string[] paths)
    {
        var imported = paths.Select(path => ReadRow(path, "Imported")).ToArray();
        _all.AddRange(imported);
        FilterRows();
        Select(imported.FirstOrDefault());
        SetStatus($"Imported {imported.Length} quest files");
    }

    private void Filter_Changed(object sender, TextChangedEventArgs e) => FilterRows();
    private void Category_Changed(object sender, SelectionChangedEventArgs e) => FilterRows();
    private void FilterRows()
    {
        if (QuestRows == null || SearchBox == null || CategoryPicker == null || OriginPicker == null) return;
        var search = SearchBox.Text.Trim();
        var category = (CategoryPicker.SelectedItem as ComboBoxItem)?.Content as string;
        var origin = (OriginPicker.SelectedItem as ComboBoxItem)?.Content as string;
        _suppressSelection = true;
        try
        {
            Rows.Clear();
            var quests = _all.GroupBy(q => q.Id).Select(g => g.OrderBy(q => q.Source == "Library" ? 0 : q.Source == "Installed" ? 1 : 2).First()).ToArray();
            foreach (var row in quests)
            {
                row.IsEnabled = _extdata?.Quests.Any(q => q.Id == row.Id) == true;
                row.CanToggle = HasExtdata;
                row.HasLocalCopy = _all.Any(q => q.Id == row.Id && q.Source == "Library");
                var installed = _extdata?.Quests.FirstOrDefault(q => q.Id == row.Id)?.Document;
                row.HasPendingChanges = installed != null && (installed.Length < row.Document.Length || !installed.ToArray().AsSpan(0, row.Document.Length).SequenceEqual(row.Document.ToArray()));
            }
            foreach (var row in quests.Where(q => (q.Title.Contains(search, StringComparison.OrdinalIgnoreCase) || q.Id.ToString().Contains(search) || q.MonsterSummary.Contains(search, StringComparison.OrdinalIgnoreCase)) &&
                (category is null or "All categories" || (category == "Library / imported" ? q.Source != "Installed" : q.Category == category)) &&
                (origin is null or "All origins" || (origin == "Official quests" ? q.IsOfficial : !q.IsOfficial)))
                .OrderBy(q => q.Id).ThenBy(q => q.Source)) { row.RowNumber = Rows.Count + 1; Rows.Add(row); }
            if (_selected != null && Rows.Contains(_selected)) QuestRows.SelectedItem = _selected;
        }
        finally { _suppressSelection = false; }
        RaiseState();
    }

    private void QuestRows_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection) return;
        if (!ConfirmClose()) { _suppressSelection = true; QuestRows.SelectedItem = _selected; _suppressSelection = false; return; }
        Select(QuestRows.SelectedItem as CustomQuestLibraryRow);
    }

    private void Select(CustomQuestLibraryRow? row, bool draft = false)
    {
        ChoicePicker.Close();
        MonsterEditor.Close();
        _selected = row;
        _sourcePath = row?.Source == "Library" ? row.Location : null;
        var creation = row?.Creation;
        if (creation == null && row?.InstalledId != null)
        {
            var candidate = _all.FirstOrDefault(q => q.Source == "Library" && q.Id == row.Id && q.Creation != null);
            if (candidate != null && row.Document.Length >= candidate.Document.Length &&
                row.Document.ToArray().AsSpan(0, candidate.Document.Length).SequenceEqual(candidate.Document.ToArray()))
            { creation = candidate.Creation; _sourcePath = candidate.Location; }
        }
        _creator = creation == null ? null : new(creation, draft);
        _editor = row == null || _creator != null ? null : new(row.Document, draft);
        if (_creator != null) _creator.PropertyChanged += (_, _) => RaiseState();
        if (_editor != null) _editor.PropertyChanged += (_, _) => RaiseState();
        _suppressSelection = true;
        QuestRows.SelectedItem = row;
        if (row != null && Rows.Contains(row)) QuestRows.ScrollIntoView(row);
        _suppressSelection = false;
        RaiseState();
        var open = row != null;
        if (open != IsEditorOpen)
        {
            IsEditorOpen = open;
            if (open) EditorOpened?.Invoke(this, EventArgs.Empty);
            else EditorClosed?.Invoke(this, EventArgs.Empty);
        }
        if (open) CreatorPanel.Focus();
    }

    private ushort NextEventId() => QuestCreationPolicy.AssignEventId(MH4UModern.Services.QuestPresentationService.Catalog,
        _all.Where(q => q.Source != "Installed").Select(q => q.Id), _all.Where(q => q.Source == "Installed").Select(q => q.Id));

    private void NewQuest_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        if (!ConfirmClose()) return;
        var id = NextEventId();
        if (id == 0) throw new InvalidOperationException("No unused Event quest IDs remain.");
        var draft = new QuestCreationDraft { Id = id, Faints = 3 };
        Select(new(draft.Build(), "Draft", "Unsaved native quest", Creation: draft), true);
        SetStatus("New Event quest draft; no game files have been changed");
    });

    private QuestDocument BuildEditor()
    {
        // Finish active cell bindings before the atomic model validation/patch.
        System.Windows.Input.Keyboard.ClearFocus();
        return Creator?.Build() ?? Editor?.Build() ?? throw new InvalidOperationException("Select a quest first.");
    }

    private bool SaveLibraryCore()
    {
        var creation = Creator?.BuildDraft();
        if (creation != null && !MH4UModern.Services.QuestPresentationService.Catalog.UnrestrictedEventIds.Contains(creation.Id)) creation.Id = NextEventId();
        var document = creation?.Build() ?? BuildEditor();
        var path = Path.Combine(LibraryFolder, $"m{document.Id}" + (creation == null ? ".mib" : ".mhquest"));
        var other = Path.ChangeExtension(path, creation == null ? ".mhquest" : ".mib");
        var ownSource = string.Equals(_sourcePath, path, StringComparison.OrdinalIgnoreCase);
        var savedSource = _all.FirstOrDefault(q => q.Source == "Library" && string.Equals(q.Location, path, StringComparison.OrdinalIgnoreCase));
        if (ownSource && !IsDirty && File.Exists(path) && savedSource != null && savedSource.Document.ToArray().AsSpan().SequenceEqual(document.ToArray()))
        { Select(savedSource); SetStatus("Quest saved locally: " + path); return true; }
        if (!ownSource && (File.Exists(path) || File.Exists(other)) && MessageBox.Show(Owner, $"Replace library quest {document.Id}?", "Quest library", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return false;
        Directory.CreateDirectory(LibraryFolder);
        foreach (var existing in new[] { path, other }.Where(File.Exists)) File.Copy(existing, existing + "." + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ".bak", false);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (creation == null) File.WriteAllBytes(temp, document.ToArray()); else File.WriteAllText(temp, creation.ToJson());
            File.Move(temp, path, true);
            if (File.Exists(other)) File.Delete(other);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        _all.RemoveAll(q => q.Source == "Library" && q.Id == document.Id);
        var row = new CustomQuestLibraryRow(document, "Library", path, Creation: creation);
        _all.Add(row);
        FilterRows();
        Select(row);
        SetStatus("Quest saved locally: " + path);
        return true;
    }

    private void SaveQuest_Click(object sender, RoutedEventArgs e) => SaveQuest();
    public void SaveQuest() => Run(() =>
    {
        if (!SaveLibraryCore()) return;
        if (_extdata != null)
        {
            var creation = Creator?.BuildDraft();
            if (creation != null)
            {
                try { QuestCreationPolicy.ValidateForInstallation(creation, MH4UModern.Services.QuestPresentationService.Catalog); }
                catch (InvalidDataException ex) { throw new InvalidDataException("Your quest is saved locally, but cannot be installed: " + ex.Message); }
            }
            var document = BuildEditor();
            var patch = _extdata.PlanInstall(document, _extdata.Quests.Any(q => q.Id == document.Id));
            if (ConfirmInstallation(patch))
            {
                var backup = _extdata.Commit(patch);
                Connect(_extdata.Folder, _manualExtdata);
                SetStatus("Quest saved and installed. Backup: " + backup);
            }
        }
        else SetStatus("Quest saved locally; no game folder connected.");
        Select(null);
    });

    private bool ConfirmInstallation(QuestPatch patch)
    {
        var existing = _extdata!.Quests.FirstOrDefault(q => q.FileNumber == patch.FileNumber && q.Slot == patch.Slot);
        var replacement = existing != null ? $"\nThis replaces the installed quest '{existing.Document.Title}' (ID {existing.Id})." : "";
        var question = $"Quest saved. Install into the game now?{replacement}\n\nAll five quest files will be backed up. Only this slot will change.\nClose Azahar/Citra before installing.\n\n{_extdata.Folder}";
        if (Creator?.ExperimentalStoryHelpers == true)
            question += "\n\nEXPERIMENTAL: This quest alone does not enable Ace Hunters. It requires its separately exported USA code patch. In-game behavior is unverified; use an offline test profile with copied saves. This will not install or remove the patch.";
        var confirmed = App.IsSmokeTest && _smokeInstallConfirmation != null ? _smokeInstallConfirmation(patch, question) :
            MessageBox.Show(Owner, question, "Install Quest", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
        if (!confirmed) return false;
        GuardEmulator();
        return true;
    }

    private void GuardEmulator()
    {
        var running = App.IsSmokeTest && _smokeEmulatorRunning != null ? _smokeEmulatorRunning() :
            Process.GetProcessesByName("azahar").Length != 0 || Process.GetProcessesByName("azahar-qt").Length != 0 ||
            Process.GetProcessesByName("citra-qt").Length != 0 || Process.GetProcessesByName("citra").Length != 0;
        if (running)
            throw new InvalidOperationException("Your quest is saved locally. Close Azahar/Citra before changing installed quests, then try again.");
    }

    private bool ConfirmManagement(string question) => App.IsSmokeTest && _smokeManagementConfirmation != null ? _smokeManagementConfirmation(question) :
        MessageBox.Show(Owner, question, "Custom Quests", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

    private void EnableQuest_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.DataContext is CustomQuestLibraryRow row) Run(() => ToggleQuest(row));
        FilterRows();
    }

    private void ToggleQuest(CustomQuestLibraryRow row)
    {
        if (_extdata == null || !ConfirmClose()) return;
        var installed = _extdata.Quests.FirstOrDefault(q => q.Id == row.Id);
        if (installed == null)
        {
            Select(row);
            if (!SaveLibraryCore()) return;
            if (Creator != null) QuestCreationPolicy.ValidateForInstallation(Creator.BuildDraft(), MH4UModern.Services.QuestPresentationService.Catalog);
            var patch = _extdata.PlanInstall(BuildEditor());
            if (!ConfirmInstallation(patch)) { Select(null); return; }
            _extdata.Commit(patch);
        }
        else
        {
            if (!ConfirmManagement($"Disable '{installed.Title}' (ID {row.Id}) in the game?\n\nA local copy will be kept, and all five game quest files will be backed up. Close Azahar/Citra first.")) return;
            GuardEmulator();
            if (!_all.Any(q => q.Source == "Library" && q.Id == row.Id))
            {
                Directory.CreateDirectory(LibraryFolder);
                var path = Path.Combine(LibraryFolder, $"m{row.Id}.mib");
                if (File.Exists(path)) throw new IOException("A local copy already exists on disk. Reload the app before disabling this quest.");
                using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write)) file.Write(installed.Document.ToArray());
                _all.Add(ReadRow(path, "Library"));
            }
            _extdata.Commit(_extdata.PlanRemove(row.Id));
        }
        Connect(_extdata.Folder, _manualExtdata);
        SetStatus(installed == null ? "Quest enabled in the game." : "Quest disabled; local copy retained.");
    }

    private void DeleteQuest_Click(object sender, RoutedEventArgs e) => Run(DeleteQuest);
    private void DeleteQuest()
    {
        if (_selected == null) return;
        if (_selected.Source == "Draft") { if (ConfirmClose()) { Select(null); SetStatus("Unsaved quest canceled."); } return; }
        var id = _selected.Id;
        var installed = _extdata?.Quests.Any(q => q.Id == id) == true;
        if (!ConfirmManagement($"Delete '{_selected.Title}' (ID {id})?\n\n" +
            (installed ? "The game copy will be removed with a five-file backup. Close Azahar/Citra first.\n" : "") +
            "Managed library files will be moved into a deletion backup. Original imported files are not deleted.")) return;
        var local = _all.Where(q => q.Id == id && q.Source == "Library").Select(q => Path.GetFullPath(q.Location)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var root = Path.GetFullPath(LibraryFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (local.Any(p => !p.StartsWith(root, StringComparison.OrdinalIgnoreCase))) throw new IOException("Cannot delete a file outside the managed quest library.");
        foreach (var path in local)
        {
            var loaded = _all.Single(q => q.Source == "Library" && string.Equals(q.Location, path, StringComparison.OrdinalIgnoreCase));
            if (!File.Exists(path) || !ReadRow(path, "Library").Document.ToArray().AsSpan().SequenceEqual(loaded.Document.ToArray())) throw new IOException("The library changed on disk. Reload the app before deleting.");
        }
        if (installed) { GuardEmulator(); _extdata!.Commit(_extdata.PlanRemove(id)); }
        if (local.Length != 0)
        {
            var backup = Path.Combine(LibraryFolder, "Deleted Quest Backups", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(backup);
            foreach (var path in local) File.Move(path, Path.Combine(backup, Path.GetFileName(path)));
        }
        _all.RemoveAll(q => q.Id == id);
        if (_extdata != null) Connect(_extdata.Folder, _manualExtdata); else { Select(null); FilterRows(); }
        SetStatus("Quest deleted; backups retained.");
    }

    internal void LoadSmokeQuest(QuestDocument document)
    {
        var row = new CustomQuestLibraryRow(document, "Synthetic", "Smoke fixture");
        _all.Add(row);
        FilterRows();
        Select(row);
        if (Editor == null || Editor.Sections.Count < 4) throw new InvalidOperationException("Custom quest editor did not populate.");
        CreatorPanel.ShowSmokeTab(0);
        var field = Editor.Numbers.Single(f => f.Name == "Quest ID");
        field.Value = "60014";
        if (!Editor.IsDirty || Editor.Build().Id != 60014) throw new InvalidOperationException("Custom quest field edit failed.");
        field.Value = "99999";
        try { _ = Editor.Build(); throw new InvalidOperationException("Invalid quest edit was accepted."); }
        catch (InvalidDataException) { }
        field.Value = "60013";
        var rank = Editor.Numbers.Single(f => f.Name == "Stars");
        rank.Value = "11";
        try
        {
            _smokeDiscardConfirmation = () => false;
            if (TryCloseEditor() || !IsEditorOpen || rank.Value != "11") throw new InvalidOperationException("Canceled quest close lost unsaved edits.");
            _smokeDiscardConfirmation = () => true;
            if (!TryCloseEditor() || IsEditorOpen || QuestRows.SelectedItem != null) throw new InvalidOperationException("Confirmed quest close did not clear selection.");
            QuestRows.SelectedItem = row;
            if (Editor?.Numbers.Single(f => f.Name == "Stars").Value != "10") throw new InvalidOperationException("Discarded quest edits persisted on reopen.");
        }
        finally { _smokeDiscardConfirmation = null; }
    }

    internal void VerifyEditorRender()
    {
        UpdateLayout();
        if (!IsEditorOpen || EditorPanel.ActualWidth < 700 || EditorPanel.ActualHeight < 300)
            throw new InvalidOperationException("Custom quest overlay did not open at a usable size.");
        if (CreatorPanel.DataContext != Editor || Editor?.IsLayoutLocked != true) throw new InvalidOperationException("Imported quest is not using the shared protected layout.");
        CreatorPanel.ShowSmokeTab(0);
    }

    internal void LoadPrivateSmokeFixtures(string fixtureRoot)
    {
        _all.Clear();
        Connect(Path.Combine(fixtureRoot, "personal", "user"));
        if (Rows.Count(q => q.Source == "Installed") != 128) throw new InvalidOperationException("Private quest list did not populate.");
        OriginPicker.SelectedIndex = 1;
        if (Rows.Count(q => q.Source == "Installed") != 93 || Rows.Any(q => !q.IsOfficial)) throw new InvalidOperationException("Official quest filter failed.");
        OriginPicker.SelectedIndex = 2;
        if (Rows.Count(q => q.Source == "Installed") != 35 || Rows.Any(q => q.IsOfficial)) throw new InvalidOperationException("Custom quest filter failed.");
        CategoryPicker.SelectedIndex = 1;
        if (Rows.Any(q => q.Category != "Event")) throw new InvalidOperationException("Combined quest filters failed.");
        CategoryPicker.SelectedIndex = 0;
        OriginPicker.SelectedIndex = 0;
        var row = Rows.First(q => q.Id == 60145);
        Select(row);
        if (Editor == null || Editor.Numbers.Count(n => n.Field.Section == "Rewards") < 300)
            throw new InvalidOperationException("Extended custom reward table did not populate.");
        foreach (var installed in _extdata!.Quests)
        {
            var model = new CustomQuestEditorViewModel(installed.Document);
            if (model.IsDirty || !model.Build().ToArray().AsSpan().SequenceEqual(installed.Document.ToArray())) throw new InvalidOperationException($"Shared layout changed imported quest {installed.Id} on open.");
        }
        for (var tab = 0; tab < 3; tab++) CreatorPanel.ShowSmokeTab(tab);
        CreatorPanel.ShowSmokeTab(0);
    }

    internal void VerifyBrowserRender()
    {
        UpdateLayout();
        if (IsEditorOpen || BrowserPanel.ActualWidth < ActualWidth - 2 || QuestRows.ActualWidth < ActualWidth - 2)
            throw new InvalidOperationException("Quest browser is not full width with the overlay closed.");
        var row = Rows.FirstOrDefault(q => q.Monsters.Count != 0) ?? throw new InvalidOperationException("No quest monster icons to verify.");
        QuestRows.ScrollIntoView(row);
        UpdateLayout();
        var item = QuestRows.ItemContainerGenerator.ContainerFromItem(row) as ListBoxItem;
        if (item == null || row.Monsters.Any(m => m.Image == null)) throw new InvalidOperationException("Quest row did not render monster icons.");
        if (row.MapIcon == null && row.Document.MapId is not (0 or 13)) throw new InvalidOperationException("Known quest map image is missing.");
        if (item.ActualWidth < QuestRows.ActualWidth - 35) throw new InvalidOperationException("Quest row did not stretch.");
        if (!Rows.Select(q => q.RowNumber).SequenceEqual(Enumerable.Range(1, Rows.Count))) throw new InvalidOperationException("Filtered row numbers are not consecutive.");
    }

    internal void OpenSmokeRow() => QuestRows.SelectedItem = Rows.First();
    internal void CloseSmokeEditor() { if (!TryCloseEditor()) throw new InvalidOperationException("Quest overlay failed to close."); }
    internal void DiscardSmokeEditor()
    {
        if (!App.IsSmokeTest) throw new InvalidOperationException("Smoke-only operation.");
        _smokeDiscardConfirmation = () => true;
        try { CloseSmokeEditor(); } finally { _smokeDiscardConfirmation = null; }
    }

    internal void StartCreatorSmoke()
    {
        NewQuest_Click(this, new RoutedEventArgs());
        var creator = Creator ?? throw new InvalidOperationException("New Quest did not open the creator.");
        if (!IsEditorOpen || !creator.IsDirty || Editor != null || CreatorPanel.Visibility != Visibility.Visible)
            throw new InvalidOperationException("Native quest creator state is incorrect.");
        creator.Languages[0].Fields[0].Value = "A Hunter's New Beginning";
        creator.Languages[0].Fields[1].Value = "Hunt a Rathian and a Rathalos";
        creator.Languages[0].Fields[3].Value = new string('a', 300);
        creator.CopyEnglish();
        creator.Monsters.Add(new CreationMonster(new QuestMonsterDraft { Id = 2, Wave = 2, Picture = 4, X = 10, Z = -30 }));
        creator.Objectives[1].Type = QuestCreationChoices.Goals.First(c => c.Id == 1);
        creator.Objectives[1].Target = QuestCreationChoices.Monsters.First(c => c.Id == 2);
        creator.Rewards.Clear();
        creator.Rewards.Add(new CreationItem(8, 3));
        creator.Rewards.Add(new CreationItem(785, 2, table: 1));
        var built = BuildEditor();
        if (built.MonsterIds.Count != 2 || built.Title != creator.Title || built.TextFields.Count != 35 ||
            !built.NumberFields.Any(f => f.Section == "Rewards") || !built.NumberFields.Any(f => f.Section == "Supplies"))
            throw new InvalidOperationException("Native quest generation did not retain creation fields.");
        creator.General[0].Value = "99999";
        try { _ = BuildEditor(); throw new InvalidOperationException("Invalid creation ID accepted."); } catch (InvalidDataException) { }
        creator.General[0].Value = built.Id.ToString();
        _smokeDiscardConfirmation = () => false;
        if (TryCloseEditor() || !ReferenceEquals(Creator, creator)) throw new InvalidOperationException("Canceled creation close lost the draft.");
        _smokeDiscardConfirmation = null;
    }

    internal void VerifyCreatorTab(int index) => CreatorPanel.ShowSmokeTab(index);
    internal void ShowRequirementsSmoke() => CreatorPanel.ShowRequirementsSmoke();

    internal void OpenCreatorPickerSmoke(bool map)
    {
        VerifyCreatorTab(map ? 0 : 1);
        if (map) CreatorPanel.OpenMapSmoke(); else { CreatorPanel.OpenMonsterSmoke(); MonsterEditor.VerifySmokeRender(); MonsterEditor.ChooseSmoke(); }
        ChoicePicker.VerifySmokeRender();
    }

    internal void VerifyCreatorPickerSmoke(bool map)
    {
        var creator = Creator!;
        var before = map ? creator.Map : creator.Monsters[0].Monster;
        ChoicePicker.SearchSmoke("zz-no-such-option");
        if (ChoicePicker.SmokeMatchCount != 0) throw new InvalidOperationException("Empty picker search is incorrect.");
        ChoicePicker.SearchSmoke(map ? "Frozen" : "RATHALOS");
        ChoicePicker.VerifySmokeRender();
        var chosen = (map ? QuestCreationChoices.Maps : QuestCreationChoices.Monsters).First(c => c.Name == (map ? "Frozen Seaway" : "Rathalos"));
        ChoicePicker.ChooseSmoke(chosen.Id);
        if (!map)
        {
            if (creator.Monsters[0].Monster != before || MonsterEditor.SmokeDraft.Monster?.Id != chosen.Id) throw new InvalidOperationException("Monster picker leaked unapplied settings into the quest.");
            MonsterEditor.DoneSmoke();
        }
        if (ChoicePicker.IsOpen || (map ? creator.Map : creator.Monsters[0].Monster)?.Id != chosen.Id)
            throw new InvalidOperationException("Picker did not apply the selected ID.");
        if (map) CreatorPanel.OpenMapSmoke(); else { CreatorPanel.OpenMonsterSmoke(); MonsterEditor.ChooseSmoke(); }
        var escape = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(ChoicePicker), 0, Key.Escape);
        if (!HandleTopOverlayKey(escape) || ChoicePicker.IsOpen || !IsEditorOpen || (map ? creator.Map : creator.Monsters[0].Monster)?.Id != chosen.Id)
            throw new InvalidOperationException("Escape did not cancel only the picker.");
        if (!map && (!MonsterEditor.IsOpen || !HandleTopOverlayKey(escape) || MonsterEditor.IsOpen || !IsEditorOpen)) throw new InvalidOperationException("Escape did not close the nested monster settings.");
        if (map) creator.Map = before; else creator.Monsters[0].Monster = before;
    }

    internal void OpenCreatorItemPickerSmoke(bool reward)
    {
        VerifyCreatorTab(2);
        (reward ? CreatorPanel.RewardSmoke : CreatorPanel.SupplySmoke).AddSmoke();
        ChoicePicker.VerifySmokeRender();
    }

    internal void VerifyCreatorItemPickerSmoke(bool reward)
    {
        var creator = Creator!;
        var view = reward ? CreatorPanel.RewardSmoke : CreatorPanel.SupplySmoke;
        var items = reward ? creator.Rewards : creator.Supplies;
        var count = items.Count;
        ChoicePicker.Close();
        if (items.Count != count) throw new InvalidOperationException("Canceled empty slot added an item.");
        view.AddSmoke(); ChoicePicker.SearchSmoke("Potion"); ChoicePicker.VerifySmokeRender(); ChoicePicker.ChooseSmoke(8);
        if (items.Count != count + 1 || view.SelectedEntry?.ItemId != 8) throw new InvalidOperationException("Item picker did not fill the selected box.");
        var added = view.SelectedEntry!;
        added.Quantity.Value = "7"; added.Chance.Value = "35";
        view.DuplicateSmoke();
        if (items.Count != count + 2 || view.SelectedEntry?.Quantity.Value != "7") throw new InvalidOperationException("Box duplication did not retain quantity.");
        view.RemoveSmoke();
        if (reward) added.Table = QuestCreationChoices.RewardTables[1]; else added.Box.SelectedChoice = QuestCreationChoices.SupplyBoxes[1];
        if (view.Slots.Any(s => s.Entry == added)) throw new InvalidOperationException("Moved item remained in the original box.");
        view.GroupSmoke(1);
        if (!view.Slots.Any(s => s.Entry == added)) throw new InvalidOperationException("Moved item is missing from its destination box.");
        var built = creator.BuildDraft();
        if (reward ? !built.Rewards.Any(r => r.Item == 8 && r.Quantity == 7 && r.Chance == 35 && r.Table == 1) : !built.Supplies.Any(s => s.Item == 8 && s.Quantity == 7 && s.Box == 1))
            throw new InvalidOperationException("Icon box edits did not reach the serialized quest.");
        items.Remove(added); view.GroupSmoke(0);
        var extras = Enumerable.Range(0, (reward ? 96 : 40) - count).Select(_ => new CreationItem(8, 1)).ToArray();
        if (reward)
        {
            foreach (var item in extras) items.Add(item);
            if (view.CanAdd) throw new InvalidOperationException("Reward box accepted more than 96 entries.");
            view.NextSmoke(); view.VerifySmokeRender();
            if (!view.Slots[0].IsOccupied) throw new InvalidOperationException("Reward paging lost entries.");
        }
        else
        {
            foreach (var item in extras.Take(40 - count)) items.Add(item);
            if (view.CanAdd) throw new InvalidOperationException("Supply box accepted more than 40 entries.");
        }
        foreach (var item in extras) items.Remove(item);
        view.GroupSmoke(1); view.GroupSmoke(0);
        if (items.Count != count) throw new InvalidOperationException("Box smoke did not restore the original draft.");
    }

    internal void VerifyCreatorNamedOptionsSmoke()
    {
        var creator = Creator!;
        if (creator.General[10].Choices.Count != 37 || creator.General[11].Choices.Count != 37 || creator.General[12].Choices.Count != 4 || creator.Monsters[0].Details[11].Choices.Count != 6)
            throw new InvalidOperationException("Recovered creator choices are incomplete.");
        creator.General[10].SelectedChoice = creator.General[10].Choices.Single(c => c.Id == 32);
        creator.General[11].SelectedChoice = creator.General[11].Choices.Single(c => c.Id == 36);
        if (creator.BuildDraft().Requirement1 != 32 || creator.BuildDraft().Requirement2 != 36) throw new InvalidOperationException("Named requirements saved indices instead of IDs.");
        creator.General[11].Value = "250";
        if (creator.General[11].SelectedChoice is not { Id: 250 } unknown || !unknown.Name.StartsWith("Unknown") || creator.BuildDraft().Requirement2 != 250)
            throw new InvalidOperationException("Unknown requirement was silently replaced.");
        creator.General[11].Value = "0";
        creator.General[1].Step(1000);
        if (creator.General[1].Value != "14") throw new InvalidOperationException("Number step did not clamp at its bound.");
        creator.General[1].Value = "1";
        var family = QuestCreationChoices.Monsters.Select(c => c.Id).ToList();
        if (family[family.IndexOf(28) + 1] != 29 || family[family.IndexOf(29) + 1] != 97 || family[family.IndexOf(44) + 1] != 98 || family.Distinct().Count() != family.Count)
            throw new InvalidOperationException("Monster family order is incomplete or duplicated.");
        if (!MH4UModern.Services.QuestPresentationService.Catalog.UnrestrictedEventIds.Contains(creator.BuildDraft().Id)) throw new InvalidOperationException("Creator assigned an unsupported quest ID.");
        var originalMap = creator.Map;
        creator.Map = QuestCreationChoices.Maps.Single(c => c.Id == 16);
        if (creator.FenceVisibility != Visibility.Collapsed) throw new InvalidOperationException("Fence controls remained visible on Sanctuary.");
        creator.Map = QuestCreationChoices.Maps.Single(c => c.Id == 11);
        if (creator.FenceVisibility != Visibility.Visible) throw new InvalidOperationException("Arena fence controls are hidden.");
        creator.Map = originalMap;
    }

    internal void OpenMonsterSettingsSmoke()
    {
        VerifyCreatorTab(1); CreatorPanel.OpenMonsterSmoke(); MonsterEditor.VerifySmokeRender();
    }
    internal void VerifyMonsterSettingsSmoke()
    {
        var old = Creator!.Monsters[0]; var hp = old.Details[6].Value;
        MonsterEditor.SmokeDraft.Details[6].Value = "999";
        MonsterEditor.DoneSmoke();
        if (!MonsterEditor.IsOpen || old.Details[6].Value != hp) throw new InvalidOperationException("Invalid monster settings were committed.");
        MonsterEditor.Close();
        if (Creator.Monsters[0] != old || old.Details[6].Value != hp) throw new InvalidOperationException("Cancel changed the monster.");
        CreatorPanel.OpenMonsterSmoke(); MonsterEditor.SmokeDraft.Details[6].Value = "31"; MonsterEditor.DoneSmoke();
        if (MonsterEditor.IsOpen || Creator.Monsters[0].Details[6].Value != "31") throw new InvalidOperationException("Done did not commit monster settings.");
        Creator.Monsters[0] = old;
        var count = Creator.Monsters.Count;
        CreatorPanel.AddMonsterSmoke(); MonsterEditor.Close();
        if (Creator.Monsters.Count != count) throw new InvalidOperationException("Cancel added a monster.");
        CreatorPanel.AddMonsterSmoke(); MonsterEditor.DoneSmoke();
        if (Creator.Monsters.Count != count + 1) throw new InvalidOperationException("Done did not add a monster.");
        var added = Creator.Monsters[^1];
        MonsterEditor.Open(new(added, false, changed => { if (changed == null) Creator.Monsters.Remove(added); })); MonsterEditor.RemoveSmoke();
        if (Creator.Monsters.Count != count) throw new InvalidOperationException("Remove did not remove the selected monster.");
    }

    internal void FinishCreatorSmoke()
    {
        var built = BuildEditor();
        if (!SaveLibraryCore()) throw new InvalidOperationException("Smoke source save was canceled.");
        if (Creator?.IsDirty != false || _selected?.Source != "Library" || !File.Exists(_selected.Location))
            throw new InvalidOperationException("Native quest source did not save to the isolated library.");
        var loaded = ReadRow(_selected.Location, "Library");
        if (loaded.Creation == null || !loaded.Document.ToArray().AsSpan().SequenceEqual(built.ToArray()))
            throw new InvalidOperationException("Saved native quest source did not reproduce its MIB.");
        Select(loaded);
        if (Creator?.Monsters.Count != 2 || Creator.Rewards.Count != 2) throw new InvalidOperationException("Native quest source did not reopen for structural editing.");
        Creator.Rewards.RemoveAt(0);
        if (!Creator.IsDirty || BuildEditor().NumberFields.Count(f => f.Section == "Rewards") != 3)
            throw new InvalidOperationException("Reopened native quest source did not support removing a reward.");
        _smokeDiscardConfirmation = () => true;
        CloseSmokeEditor();
        _smokeDiscardConfirmation = null;
    }

    internal void VerifyExpansionSmoke()
    {
        if (!App.IsSmokeTest) throw new InvalidOperationException("Smoke-only operation.");
        var draft = new QuestCreationDraft { Faints = 3 };
        var capacityModel = new QuestCreationViewModel(draft);
        capacityModel.General[1].Value = "8";
        foreach (var choice in QuestCreationChoices.Monsters.Where(c => c.Id != 1)) capacityModel.Monsters.Add(new(new() { Id = choice.Id }, draft.Map));
        if (capacityModel.Rewards.Count is 0 or > 96 || !capacityModel.PresetStatus.Contains("96-entry limit"))
            throw new InvalidOperationException("An oversized automatic preset did not preserve the previous reward box.");
        Select(new(draft.Build(), "Draft", "Expansion smoke", Creation: draft), true);
        var model = Creator!;
        if (model.Supplies.Count < 24 || model.Rewards.Count == 0 || !model.Rewards.Any(r => r.ItemId == 487)) throw new InvalidOperationException("New quest did not receive rank/monster presets.");
        model.General[1].Value = "8";
        if (!model.Supplies.Any(s => s.ItemId == 770) || !model.Rewards.Any(r => r.ItemId == 1264)) throw new InvalidOperationException("Automatic G-rank presets are missing: " + model.PresetSmokeState);
        model.Supplies[0].Quantity.Value = "7"; model.Rewards.Clear(); model.General[1].Value = "1";
        model.Monsters[0].Monster = QuestCreationChoices.Monsters.Single(c => c.Id == 2);
        model.Objectives[0].Target = model.Monsters[0].Monster;
        if (model.Supplies[0].Quantity.Value != "7" || model.Rewards.Count != 0) throw new InvalidOperationException("Automatic presets overwrote manual item edits or a cleared box.");
        model.ApplyPresets();
        if (model.Rewards.Count == 0 || model.Supplies[0].Quantity.Value == "7") throw new InvalidOperationException("Explicit presets did not restore item boxes.");
        if (QuestCreationChoices.AreasFor(11).Count != 1 || QuestCreationChoices.AreasFor(17).Count != 11 || QuestCreationChoices.AreasFor(6).Count != 2) throw new InvalidOperationException("Map-aware spawn lists are incorrect.");
        model.Map = QuestCreationChoices.Maps.Single(c => c.Id == 17); model.Monsters[0].Area.Value = "11";
        _ = model.Build(); model.Map = QuestCreationChoices.Maps.Single(c => c.Id == 11);
        try { _ = model.Build(); throw new InvalidOperationException("Unavailable spawn area was accepted after changing maps."); } catch (InvalidDataException) { }
        model.Monsters[0].Area.Value = "1";
        CreatorPanel.AddMonsterSmoke(); MonsterEditor.SmokeDraft.Kind = QuestCreationChoices.MonsterKinds[2];
        if (MonsterEditor.SmokeDraft.Species.Any(s => QuestCreationDraft.IsLargeMonster(s.Id))) throw new InvalidOperationException("Small monster picker includes unsafe large species.");
        MonsterEditor.SmokeDraft.Monster = QuestCreationChoices.SmallMonsters.Single(c => c.Id == 50); MonsterEditor.SmokeDraft.Quantity.Value = "5"; MonsterEditor.SmokeDraft.Wave.Value = "2"; MonsterEditor.DoneSmoke();
        CreatorPanel.AddMonsterSmoke(); MonsterEditor.SmokeDraft.Kind = QuestCreationChoices.MonsterKinds[1];
        MonsterEditor.SmokeDraft.Monster = QuestCreationChoices.Monsters.Single(c => c.Id == 7); MonsterEditor.SmokeDraft.Chance.Value = "70";
        MonsterEditor.SmokeDraft.IntruderDelay.Value = "9"; MonsterEditor.SmokeDraft.IntruderAppearance.Value = "75";
        if (model.IntruderFields[0].Value != "2" || model.IntruderFields[1].Value != "0") throw new InvalidOperationException("Unconfirmed intruder settings changed the parent.");
        MonsterEditor.DoneSmoke();
        if (model.Monsters.Count != 3 || model.Monsters[1].Kind?.Id != 2 || model.Monsters[2].Kind?.Id != 1 || model.IntruderFields[0].Value != "9" || model.IntruderFields[1].Value != "75") throw new InvalidOperationException("Monster type overlay did not apply native small/intruder settings.");
        CreatorPanel.OpenMonsterSmoke(2); MonsterEditor.SmokeDraft.IntruderAppearance.Value = "40"; MonsterEditor.Close();
        if (model.IntruderFields[1].Value != "75") throw new InvalidOperationException("Canceled intruder settings were saved.");
        CreatorPanel.OpenMonsterSmoke(2); MonsterEditor.SmokeDraft.IntruderAppearance.Value = "101"; MonsterEditor.DoneSmoke();
        if (!MonsterEditor.IsOpen || model.IntruderFields[1].Value != "75") throw new InvalidOperationException("Invalid intruder settings left the details panel.");
        MonsterEditor.Close();
        if (model.WaveConditions.Count != 1 || model.WaveConditions[0].Wave != 2) throw new InvalidOperationException("Unused wave settings are visible.");
        var small = model.Monsters[1]; var smallCopy = small.Copy(); smallCopy.Wave.Value = "1"; model.ApplyMonsterEdit(small, smallCopy);
        if (model.WaveConditions.Count != 0) throw new InvalidOperationException("Empty wave settings remained visible.");
        small = model.Monsters[1]; smallCopy = small.Copy(); smallCopy.Wave.Value = "2"; model.ApplyMonsterEdit(small, smallCopy);
        model.SmallConditions[0].Value = "1"; model.SmallConditions[1].Value = "2"; model.SmallConditions[2].Value = "1";
        CreatorPanel.VerifyContextualSmoke();
        model.General[4].Value = "3001"; model.General[5].Value = "4";
        var built = model.Build();
        if (!built.MonsterIds.Contains(50u) || !built.MonsterIds.Contains(7u) || built.NumberFields.Single(f => f.Name == "Faint penalty (zenny)").Value != 751) throw new InvalidOperationException("Expanded fields were not serialized.");
    }
    internal void OpenArtworkPickerSmoke() { VerifyCreatorTab(0); CreatorPanel.OpenArtworkSmoke(0); ChoicePicker.VerifySmokeRender(); }
    internal void FinishArtworkPickerSmoke()
    {
        ChoicePicker.SearchSmoke("Danger"); ChoicePicker.ChooseSmoke(84);
        if (Creator!.Build().PreviewArt[0] != 84 || Creator.Monsters[0].Monster?.Id != 2) throw new InvalidOperationException("Artwork selection changed the spawn list.");
    }
    internal void VerifyMapPreviewSmoke(string name) => CreatorPanel.VerifyMapPreviewSmoke(Path.Combine(App.SmokeTestDirectory, name + ".png"));
    internal void OpenIntruderDetailsSmoke()
    {
        if (!App.IsSmokeTest || EditorLayout == null) throw new InvalidOperationException("Smoke-only operation.");
        var index = EditorLayout.Monsters.ToList().FindIndex(m => m.Kind?.Id == 1);
        CreatorPanel.OpenMonsterSmoke(index); MonsterEditor.VerifySmokeRender();
        if (!MonsterEditor.SmokeDraft.Numbers.Contains(MonsterEditor.SmokeDraft.IntruderDelay) || !MonsterEditor.SmokeDraft.Numbers.Contains(MonsterEditor.SmokeDraft.IntruderAppearance))
            throw new InvalidOperationException("Intruder-specific controls are missing from details.");
    }
    internal void CloseIntruderDetailsSmoke() => MonsterEditor.Close();
    internal void VerifyImportedAliasingSmoke()
    {
        var document = new QuestCreationDraft().Build(); var vm = new CustomQuestEditorViewModel(document);
        vm.TitleField!.Value = "New title";
        if (vm.Texts.Where(t => t.Name == "Title").Any(t => t.Value != "New title") || vm.Build().Title != "New title") throw new InvalidOperationException("Imported linked language editing failed.");
        var before = document.ToArray(); var after = vm.Build().ToArray();
        var range = vm.TitleField.Field;
        for (var i = 0; i < before.Length; i++) if ((i < range.Offset || i >= range.Offset + range.Capacity + 2) && before[i] != after[i]) throw new InvalidOperationException("Imported text edit changed opaque quest data.");
        var penalty = vm.Numbers.Single(n => n.Field.Name == "Faint penalty (zenny)");
        var reward = vm.Numbers.Single(n => n.Field.Name == "Main reward (zenny)");
        penalty.Input.Value = "5"; reward.Input.Value = "3001";
        if (vm.Build().NumberFields.Single(n => n.Name == "Faint penalty (zenny)").Value != 601) throw new InvalidOperationException("Imported faint count did not encode the exact threshold.");
        penalty.Input.Value = "0";
        if (vm.Build().NumberFields.Single(n => n.Name == "Faint penalty (zenny)").Value != 0) throw new InvalidOperationException("Imported no-reduction setting was lost.");
        reward.Input.Value = "5"; penalty.Input.Value = "4";
        try { _ = vm.Build(); throw new InvalidOperationException("Unrepresentable imported faint count was accepted."); } catch (InvalidDataException) { }
    }

    internal void VerifyUnifiedImportedSmoke()
    {
        if (!App.IsSmokeTest) throw new InvalidOperationException("Smoke-only operation.");
        var draft = new QuestCreationDraft { PreviewArt = [84, 88, 0, 88, 84], IntruderChance = 70 };
        foreach (var language in draft.Languages) { language.Title = "Raw layout test"; language.Description = "Preserve scene data"; }
        draft.Monsters.Add(new() { Kind = 1, Id = 7, Chance = 65 });
        draft.Monsters.Add(new() { Kind = 2, Id = 50, Quantity = 5, Wave = 2 });
        draft.SmallConditions[0] = new() { Type = 1, Target = 1, Quantity = 1 };
        draft.Rewards.Add(new() { Item = 487, Quantity = 2 });
        var bytes = draft.Build().ToArray();
        var h = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes);
        bytes[h + 3] = 0xAD;
        var firstMonster = draft.Build().NumberFields.First(n => n.Section == "Large Monsters" && n.Name.EndsWith(": monster ID"));
        bytes[firstMonster.Offset + 11] = 0x44;
        var document = new QuestDocument(bytes);
        Select(new(document, "Synthetic", "Shared imported layout smoke"));
        var model = Editor!;
        if (Creator != null || model.IsDirty || !model.Build().ToArray().AsSpan().SequenceEqual(bytes)) throw new InvalidOperationException("Shared imported layout changed bytes on open.");
        CreatorPanel.VerifyContextualSmoke();
        model.QuestTitle = "Raw edited test"; model.Stars.Value = "5";
        model.Artwork[0].SelectedChoice = QuestCreationChoices.Previews.Single(p => p.Id == 98);
        model.Objectives[0].Type = QuestCreationChoices.Goals.Single(p => p.Id == 129);
        model.Objectives[0].Target = QuestCreationChoices.Monsters.Single(p => p.Id == 2);
        model.Map = QuestCreationChoices.Maps.Single(p => p.Id == 15);
        CreatorPanel.OpenMonsterSmoke();
        MonsterEditor.SmokeDraft.Monster = QuestCreationChoices.Monsters.Single(p => p.Id == 2);
        MonsterEditor.SmokeDraft.Details[6].Value = "31";
        if (model.Monsters[0].Monster?.Id != 1) throw new InvalidOperationException("Unconfirmed imported monster edits changed the source.");
        MonsterEditor.DoneSmoke();
        if (model.Monsters[0].Monster?.Id != 2 || model.Numbers.Single(n => n.Field.Name == "Stats 1: HP table index").Parsed != 31) throw new InvalidOperationException("Imported monster changes did not patch the mapped fields.");
        var beforeCancel = model.Build().ToArray();
        CreatorPanel.OpenMonsterSmoke(); MonsterEditor.SmokeDraft.Details[6].Value = "32"; MonsterEditor.Close();
        if (!model.Build().ToArray().AsSpan().SequenceEqual(beforeCancel)) throw new InvalidOperationException("Canceled imported monster edit was applied.");
        var intruder = model.Monsters.ToList().FindIndex(m => m.Kind?.Id == 1);
        CreatorPanel.OpenMonsterSmoke(intruder);
        if (!MonsterEditor.SmokeDraft.Numbers.Any(n => n.Name == "Intruder delay (min)")) throw new InvalidOperationException("Intruder settings are missing from monster details.");
        MonsterEditor.SmokeDraft.IntruderDelay.Value = "9"; MonsterEditor.SmokeDraft.IntruderAppearance.Value = "80"; MonsterEditor.SmokeDraft.Chance.Value = "55";
        MonsterEditor.DoneSmoke();
        if (model.IntruderFields[0].Value != "9" || model.IntruderFields[1].Value != "80") throw new InvalidOperationException("Imported intruder settings were not applied.");
        CreatorPanel.OpenMonsterSmoke(intruder); MonsterEditor.SmokeDraft.IntruderDelay.Value = "256"; MonsterEditor.DoneSmoke();
        if (!MonsterEditor.IsOpen || model.IntruderFields[0].Value != "9") throw new InvalidOperationException("Invalid imported intruder settings left the details panel.");
        MonsterEditor.Close();
        CreatorPanel.ShowSmokeTab(2);
        var supplyCount = model.Supplies.Count;
        CreatorPanel.SupplySmoke.SelectSmoke(0); CreatorPanel.SupplySmoke.AddSmoke(); CreatorPanel.SupplySmoke.DuplicateSmoke(); CreatorPanel.SupplySmoke.RemoveSmoke();
        if (ChoicePicker.IsOpen || model.Supplies.Count != supplyCount) throw new InvalidOperationException("Protected imported item structure was modified.");
        model.Supplies[0].Quantity.Value = "3"; model.Supplies[0].Item = QuestCreationChoices.Items.Single(i => i.Id == 785);
        model.Rewards[0].Quantity.Value = "4"; model.Rewards[0].Chance.Value = "75";
        var changed = model.Build();
        if (changed.Title != "Raw edited test" || changed.Rank != 5 || changed.MapId != 15 || changed.PreviewArt[0] != 98 || changed.NumberFields.Single(n => n.Name == "Main 1 type").Value != 129 || changed.NumberFields.Single(n => n.Name == "Main 1 target ID").Value != 2 || changed.NumberFields.Single(n => n.Name == "Intruder 1: chance").Value != 55 || changed.NumberFields.Single(n => n.Name == "Box 1, item 1: quantity").Value != 3 || changed.NumberFields.Single(n => n.Name == "A / 1 / 1: quantity").Value != 4)
            throw new InvalidOperationException("Shared imported controls did not retain their edits.");
        var allowed = new HashSet<int>();
        foreach (var field in model.Numbers.Where(n => n.Parsed != n.Field.Value)) foreach (var at in Enumerable.Range(field.Field.Offset, field.Field.Width)) allowed.Add(at);
        foreach (var text in model.Texts.Where(t => t.Value != t.Field.Value)) foreach (var at in Enumerable.Range(text.Field.Offset, text.Field.Capacity + 2)) allowed.Add(at);
        var after = changed.ToArray();
        for (var i = 0; i < bytes.Length; i++) if (!allowed.Contains(i) && bytes[i] != after[i]) throw new InvalidOperationException("Shared imported edit changed opaque data.");
        if (after[h + 3] != 0xAD || after[firstMonster.Offset + 11] != 0x44) throw new InvalidOperationException("Opaque quest flags were overwritten.");
        var small = model.Monsters.Single(m => m.Kind?.Id == 2); var moved = small.Copy(); moved.Wave.Value = "3";
        try { model.ApplyMonsterEdit(small, moved); throw new InvalidOperationException("Imported wave structure could be changed."); } catch (InvalidDataException) { }
        var description = model.Languages[0].Fields[3]; var originalDescription = description.Value;
        description.Value = new string('a', description.MaxLength + 1);
        try { _ = model.Build(); throw new InvalidOperationException("Imported text capacity was ignored."); } catch (InvalidDataException) { }
        description.Value = originalDescription;

        var aliases = draft.Build().ToArray();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(aliases.AsSpan(0x20), System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(aliases.AsSpan(0x1C)));
        var aliasModel = new CustomQuestEditorViewModel(new(aliases));
        aliasModel.Rewards.Single(i => i.Table?.Id == 1).Quantity.Value = "7";
        if (aliasModel.Rewards.Single(i => i.Table?.Id == 0).Quantity.Value != "7" || aliasModel.Build().NumberFields.Any(n => n.Section == "Rewards" && n.Name.EndsWith(": quantity") && n.Value != 7)) throw new InvalidOperationException("Linked imported reward entries lost an edit.");
        File.WriteAllText(Path.Combine(App.SmokeTestDirectory, "shared-editor-verification.txt"), "PASS: shared controls patch title/stars/map/art/objectives/monster stats/intruder settings/items; canceled details preserve bytes; opaque flags survive; imported structure/text limits hold; numeric pointer aliases remain synchronized.");
        CreatorPanel.ShowSmokeTab(0);
    }

    internal void VerifySaveInstallFlowSmoke()
    {
        if (!App.IsSmokeTest) throw new InvalidOperationException("Smoke-only operation.");
        var folder = Path.Combine(App.SmokeTestDirectory, "QuestSaveFlow");
        Directory.CreateDirectory(folder);
        for (var i = 1; i <= 5; i++) File.WriteAllBytes(Path.Combine(folder, $"quest{i}"),
            QuestCrypto.EncodeContainer(new(new byte[QuestCrypto.PayloadSize], 0x12340010, new byte[256])));
        byte[][] Snapshot() => Enumerable.Range(1, 5).Select(i => File.ReadAllBytes(Path.Combine(folder, $"quest{i}"))).ToArray();
        void VerifyUnchanged(byte[][] original)
        {
            for (var i = 0; i < 5; i++) if (!original[i].AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(folder, $"quest{i + 1}"))))
                throw new InvalidOperationException("Canceled/blocked installation changed extdata.");
        }
        var prompts = new List<string>();
        try
        {
            Connect(folder);
            var original = Snapshot();
            var libraryCount = Directory.EnumerateFiles(LibraryFolder).Count();
            NewQuest_Click(this, new());
            Creator!.QuestTitle = "Canceled new quest";
            _smokeDiscardConfirmation = () => true;
            CloseSmokeEditor();
            if (Directory.EnumerateFiles(LibraryFolder).Count() != libraryCount) throw new InvalidOperationException("Cancel saved an unsaved quest.");
            VerifyUnchanged(original);

            _smokeInstallConfirmation = (_, question) =>
            {
                if (_selected?.Source != "Library" || !File.Exists(_selected.Location) || Creator?.IsDirty != false)
                    throw new InvalidOperationException("Installation was offered before the local save finished.");
                prompts.Add(question); return false;
            };
            _smokeEmulatorRunning = () => false;
            NewQuest_Click(this, new());
            Creator!.QuestTitle = "Save flow quest";
            var id = Creator.BuildDraft().Id;
            if (Creator.Languages.Any(l => l.Fields[0].Value != Creator.QuestTitle) || Creator.BuildDraft().Languages.Any(l => l.Other != "Rathian"))
                throw new InvalidOperationException("Header title or derived Main Monsters text was not retained.");
            SaveQuest();
            var saved = _all.Single(q => q.Source == "Library" && q.Id == id);
            if (IsEditorOpen || prompts.Count != 1 || !File.Exists(saved.Location)) throw new InvalidOperationException("Save/decline did not finish locally.");
            VerifyUnchanged(original);

            Select(saved);
            _smokeInstallConfirmation = (_, question) => { prompts.Add(question); return true; };
            _smokeEmulatorRunning = () => true;
            try { SaveQuest(); throw new InvalidOperationException("Running emulator allowed installation."); }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("Your quest is saved locally.")) { }
            if (!File.Exists(saved.Location) || Creator?.IsDirty != false) throw new InvalidOperationException("Blocked install lost the saved source.");
            VerifyUnchanged(original);
            _smokeEmulatorRunning = () => false;
            SaveQuest();
            var installed = _extdata!.Quests.Single();
            var savedBytes = saved.Document.ToArray();
            var installedBytes = installed.Document.ToArray();
            if (IsEditorOpen || installed.Id != id || !savedBytes.AsSpan().SequenceEqual(installedBytes.AsSpan(0, savedBytes.Length)) || installedBytes.AsSpan(savedBytes.Length).ContainsAnyExcept((byte)0))
                throw new InvalidOperationException("Save/accept did not install the saved quest.");
            var backup = Directory.GetDirectories(Path.Combine(folder, "Classic MH Quest Backups")).Single();
            for (var i = 0; i < 5; i++) if (!original[i].AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(backup, $"quest{i + 1}"))))
                throw new InvalidOperationException("Install flow backup differs from the original snapshot.");

            Select(_all.Single(q => q.Source == "Installed" && q.Id == id));
            if (Creator == null || _sourcePath != saved.Location) throw new InvalidOperationException("Installed quest did not recover its matching authoring source.");
            Creator!.QuestTitle = "Revised save flow quest";
            _smokeInstallConfirmation = (_, question) =>
            {
                if (!question.Contains($"'Save flow quest' (ID {id})")) throw new InvalidOperationException("Install prompt did not identify the replacement.");
                prompts.Add(question); return true;
            };
            SaveQuest();
            if (_extdata!.Quests.Single().Title != "Revised save flow quest" || Directory.GetDirectories(Path.Combine(folder, "Classic MH Quest Backups")).Length != 2)
                throw new InvalidOperationException("Confirmed replacement did not update exactly one quest and back it up.");

            if (Rows.Count(q => q.Id == id) != 1 || !Rows.Single(q => q.Id == id).IsEnabled || !Rows.Single(q => q.Id == id).HasLocalCopy)
                throw new InvalidOperationException("Saved and installed quest produced duplicate browser rows.");
            var beforeDisable = Snapshot();
            _smokeManagementConfirmation = _ => false;
            ToggleQuest(Rows.Single(q => q.Id == id)); VerifyUnchanged(beforeDisable);
            _smokeManagementConfirmation = _ => true;
            _smokeEmulatorRunning = () => true;
            try { ToggleQuest(Rows.Single(q => q.Id == id)); throw new InvalidOperationException("Running emulator allowed disable."); }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("Your quest is saved locally.")) { }
            VerifyUnchanged(beforeDisable); _smokeEmulatorRunning = () => false;
            ToggleQuest(Rows.Single(q => q.Id == id));
            if (_extdata!.Quests.Any(q => q.Id == id) || Rows.Single(q => q.Id == id).IsEnabled || !File.Exists(saved.Location)) throw new InvalidOperationException("Disable did not retain the sole local row.");
            _smokeInstallConfirmation = (_, _) => true;
            ToggleQuest(Rows.Single(q => q.Id == id));
            if (!_extdata!.Quests.Any(q => q.Id == id) || Rows.Count(q => q.Id == id) != 1) throw new InvalidOperationException("Re-enable did not restore one quest.");
            Select(Rows.Single(q => q.Id == id));
            _smokeManagementConfirmation = _ => false; var beforeDelete = Snapshot(); DeleteQuest(); VerifyUnchanged(beforeDelete);
            if (!File.Exists(saved.Location)) throw new InvalidOperationException("Declined delete removed a source.");
            _smokeManagementConfirmation = _ => true; DeleteQuest();
            if (_extdata!.Quests.Any(q => q.Id == id) || Rows.Any(q => q.Id == id) || File.Exists(saved.Location) || !Directory.EnumerateFiles(Path.Combine(LibraryFolder, "Deleted Quest Backups"), Path.GetFileName(saved.Location), SearchOption.AllDirectories).Any())
                throw new InvalidOperationException("Delete did not remove both copies with a local backup.");

            var rawDraft = new QuestCreationDraft { Id = NextEventId() };
            _extdata.Commit(_extdata.PlanInstall(rawDraft.Build())); Connect(folder);
            var rawInstalled = _extdata!.Quests.Single(q => q.Id == rawDraft.Id).Document.ToArray();
            ToggleQuest(Rows.Single(q => q.Id == rawDraft.Id));
            var captured = _all.Single(q => q.Source == "Library" && q.Id == rawDraft.Id);
            if (captured.Creation != null || !rawInstalled.AsSpan().SequenceEqual(File.ReadAllBytes(captured.Location)) || _extdata!.Quests.Any(q => q.Id == rawDraft.Id))
                throw new InvalidOperationException("Disabling an imported quest did not preserve its exact local MIB.");
            ToggleQuest(Rows.Single(q => q.Id == rawDraft.Id));
            var beforeDraftDelete = Snapshot();
            Select(new(rawDraft.Build(), "Draft", "Unsaved occupied-ID smoke", Creation: rawDraft), true);
            DeleteQuest(); VerifyUnchanged(beforeDraftDelete);
            if (!File.Exists(captured.Location) || Rows.Count(q => q.Id == rawDraft.Id) != 1) throw new InvalidOperationException("Deleting an unsaved draft removed a saved quest with the same ID.");
            Select(Rows.Single(q => q.Id == rawDraft.Id)); DeleteQuest();
            if (_extdata!.Quests.Any(q => q.Id == rawDraft.Id) || Rows.Any(q => q.Id == rawDraft.Id) || File.Exists(captured.Location))
                throw new InvalidOperationException("Captured imported quest could not be deleted.");

            var legacy = new QuestCreationDraft { Id = 60000 };
            var legacyPath = Path.Combine(LibraryFolder, "m60000.mhquest");
            File.WriteAllText(legacyPath, legacy.ToJson());
            var legacyBytes = File.ReadAllBytes(legacyPath);
            var legacyRow = ReadRow(legacyPath, "Library"); _all.Add(legacyRow); Select(legacyRow);
            _smokeInstallConfirmation = (_, question) => { prompts.Add(question); return false; };
            var beforeMigration = Snapshot();
            SaveQuest();
            if (!legacyBytes.AsSpan().SequenceEqual(File.ReadAllBytes(legacyPath)) || !_all.Any(q => q.Source == "Library" && q.Creation != null && q.Id != 60000 && q.Title == legacy.Languages[0].Title))
                throw new InvalidOperationException("Unsupported legacy ID was not migrated safely.");
            VerifyUnchanged(beforeMigration);
            var older = new QuestCreationDraft { Id = NextEventId() };
            var olderPath = Path.Combine(LibraryFolder, $"m{older.Id}.mhquest");
            File.WriteAllText(olderPath, older.ToJson());
            var olderRow = ReadRow(olderPath, "Library"); _all.Add(olderRow); Select(olderRow);
            SaveQuest();
            if (ReadRow(olderPath, "Library").Creation!.Languages.Any(l => l.Other != "Rathian"))
                throw new InvalidOperationException("Unchanged older source did not persist derived Main Monsters text.");
            VerifyUnchanged(beforeMigration);
            NewQuest_Click(this, new());
            Creator!.Monsters[0].Monster = QuestCreationChoices.Monsters.Single(c => c.Id == 116);
            Creator.Objectives[0].Target = Creator.Monsters[0].Monster;
            _smokeInstallConfirmation = (_, _) => throw new InvalidOperationException("Invalid cutscene map reached install confirmation.");
            try { SaveQuest(); throw new InvalidOperationException("Invalid cutscene map was installed."); }
            catch (InvalidDataException ex) when (ex.Message.StartsWith("Your quest is saved locally, but cannot be installed:")) { }
            if (Creator?.IsDirty != false || _selected?.Source != "Library" || !File.Exists(_selected.Location))
                throw new InvalidOperationException("Cutscene-map rejection lost the saved source.");
            VerifyUnchanged(beforeMigration); Select(null);
            File.WriteAllText(Path.Combine(App.SmokeTestDirectory, "save-install-prompts.txt"), string.Join("\n\n---\n\n", prompts));
        }
        finally
        {
            Select(null); _smokeDiscardConfirmation = null; _smokeInstallConfirmation = null; _smokeEmulatorRunning = null; _smokeManagementConfirmation = null;
            _extdata = null; _manualExtdata = false; _autoContext = null;
            _all.RemoveAll(q => q.Source == "Installed"); FilterRows();
        }
    }

    internal void VerifyAutomaticConnection(QuestDocument document)
    {
        var provider = EmulatorPathProvider;
        var characterProvider = CharacterPathProvider;
        var storage = Path.Combine(App.SmokeTestDirectory, "QuestAutoPath", "sdmc");
        var profile = Path.Combine(storage, "Nintendo 3DS", new string('0', 32), new string('1', 32));
        var folder = Path.Combine(profile, "extdata", "00000000", "00001263", "user");
        Directory.CreateDirectory(folder);
        for (var i = 1; i <= 5; i++) File.WriteAllBytes(Path.Combine(folder, $"quest{i}"),
            QuestCrypto.EncodeContainer(new(new byte[QuestCrypto.PayloadSize], 0x12340010, new byte[256])));
        var set = QuestExtdataSet.Load(folder);
        set.Commit(set.PlanInstall(document));
        var originalHashes = Enumerable.Range(1, 5).Select(i => File.ReadAllBytes(Path.Combine(folder, $"quest{i}"))).ToArray();
        try
        {
            _manualExtdata = false;
            _autoContext = null;
            EmulatorPathProvider = () => storage;
            CharacterPathProvider = () => null;
            UseConfiguredEmulatorPath();
            if (_extdata?.Folder != folder || Rows.Count(q => q.Source == "Installed") != 1) throw new InvalidOperationException("Configured Azahar extdata did not connect automatically.");
            OpenSmokeRow();
            EmulatorPathProvider = () => Path.Combine(storage, "missing");
            UseConfiguredEmulatorPath();
            if (_extdata?.Folder != folder || !IsEditorOpen) throw new InvalidOperationException("Automatic path change interrupted an open editor.");
            CloseSmokeEditor();
            UseConfiguredEmulatorPath();
            if (HasExtdata) throw new InvalidOperationException("Old automatic extdata remained active after a path change.");
            Connect(folder);
            UseConfiguredEmulatorPath();
            if (_extdata?.Folder != folder) throw new InvalidOperationException("Manual quest folder was replaced automatically.");
            _manualExtdata = false;
            _autoContext = null;
            EmulatorPathProvider = () => storage;
            CharacterPathProvider = () => Path.Combine(profile, "title", "00040000", "00126300", "data", "00000001", "user1");
            UseConfiguredEmulatorPath();
            if (_extdata?.Folder != folder) throw new InvalidOperationException("Active character profile did not resolve.");
            for (var i = 1; i <= 5; i++) if (!originalHashes[i - 1].AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(folder, $"quest{i}"))))
                throw new InvalidOperationException("Automatic quest discovery wrote extdata.");
        }
        finally
        {
            EmulatorPathProvider = provider;
            CharacterPathProvider = characterProvider;
            _autoContext = null;
            _manualExtdata = false;
            _extdata = null;
            _all.RemoveAll(q => q.Source == "Installed");
            Select(null);
            FilterRows();
        }
    }

}
