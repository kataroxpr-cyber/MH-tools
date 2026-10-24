using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MH4UModern.Models;
using MH4UModern.Services;
using MH4UModern.ViewModels;
using Microsoft.Win32;

namespace MH4UModern;

public partial class MainWindow
{
    private readonly EmulatorSaveService _emulatorSaves = new();
    private readonly EmulatorPathSettings _emulatorSettings = new(App.IsSmokeTest ? App.SmokeTestDirectory : null);
    private string? _emulatorPath;
    private string _emulatorStatus = "No emulator path set";
    private bool _emulatorBusy;
    private bool _suppressEmulatorSelection;
    private bool _suppressEmulatorSaveSelection;
    private SaveIdentity? _smokeIdentity;

    public SaveIdentity? HunterIdentity => _smokeIdentity ?? (Character is null ? null : SaveIdentity.FromCharacter(Character.Model));
    public bool HasCharacter => Character is not null;
    public ObservableCollection<EmulatorSaveInfo> EmulatorSaves { get; } = new();
    public ObservableCollection<string> EmulatorLocations { get; } = new();
    public string EmulatorPathDisplay => _emulatorPath ?? "Not connected";
    public string EmulatorStatus => _emulatorStatus;
    public bool EmulatorControlsEnabled => !_emulatorBusy;
    public bool CanRefreshEmulator => !_emulatorBusy && _emulatorPath is not null;
    public string BackupSummary => $"{Backups.Count} backup{(Backups.Count == 1 ? string.Empty : "s")}";

    private void CharacterIdentity_Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CharacterEditorViewModel.Name) or nameof(CharacterEditorViewModel.Gender) or nameof(CharacterEditorViewModel.HunterRank))
            OnPropertyChanged(nameof(HunterIdentity));
    }

    private async Task RestoreEmulatorPathAsync()
    {
        _emulatorPath = _emulatorSettings.Load();
        OnPropertyChanged(nameof(EmulatorPathDisplay));
        OnPropertyChanged(nameof(CanRefreshEmulator));
        if (_emulatorPath is not null) await RefreshEmulatorAsync(_emulatorPath, persist: false);
    }

    private void SetEmulatorBusy(bool busy)
    {
        _emulatorBusy = busy;
        OnPropertyChanged(nameof(EmulatorControlsEnabled));
        OnPropertyChanged(nameof(CanRefreshEmulator));
    }

    private void SetEmulatorStatus(string text)
    {
        _emulatorStatus = text;
        OnPropertyChanged(nameof(EmulatorStatus));
    }

    private async void SetEmulatorPath_Click(object sender, RoutedEventArgs e)
    {
        if (_emulatorBusy) return;
        try
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select Azahar's user folder or SDMC storage folder",
                InitialDirectory = _emulatorPath ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Multiselect = false
            };
            if (dialog.ShowDialog(this) == true) await RefreshEmulatorAsync(dialog.FolderName, persist: true);
        }
        catch (Exception ex) { SetEmulatorStatus("Folder selection failed: " + ex.GetBaseException().Message); }
    }

    private async void DetectEmulator_Click(object sender, RoutedEventArgs e)
    {
        if (_emulatorBusy) return;
        SetEmulatorBusy(true);
        SetEmulatorStatus("Detecting Azahar folders...");
        try
        {
            var locations = await Task.Run(() => _emulatorSaves.DetectLocations(GetAzaharCandidates()));
            _suppressEmulatorSelection = true;
            try
            {
                EmulatorLocations.Clear();
                foreach (var path in locations) EmulatorLocations.Add(path);
                EmulatorLocationPicker.Visibility = locations.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            }
            finally { _suppressEmulatorSelection = false; }
            if (locations.Count == 1)
            {
                SetEmulatorBusy(false);
                await RefreshEmulatorAsync(locations[0], persist: true);
            }
            else SetEmulatorStatus(locations.Count == 0 ? "Azahar was not found in the standard locations. Select a folder." : $"{locations.Count} storage folders found. Choose a path.");
        }
        catch (Exception ex) { SetEmulatorStatus("Detection failed: " + ex.GetBaseException().Message); }
        finally { SetEmulatorBusy(false); }
    }

    private static IEnumerable<string> GetAzaharCandidates()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var paths = new List<string>
        {
            Path.Combine(roaming, "Azahar"),
            Path.Combine(local, "Azahar"),
            Path.Combine(local, "Programs", "Azahar"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Azahar"),
            Path.Combine(documents, "Azahar"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Azahar"),
            AppContext.BaseDirectory
        };
        foreach (var process in Process.GetProcessesByName("azahar"))
        {
            using (process)
            {
                try { if (process.MainModule?.FileName is string exe) paths.Add(Path.GetDirectoryName(exe)!); }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException) { }
            }
        }
        return paths;
    }

    private async void EmulatorLocation_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_suppressEmulatorSelection && !_emulatorBusy && EmulatorLocationPicker.SelectedItem is string path)
            await RefreshEmulatorAsync(path, persist: true);
    }

    private async void RefreshEmulator_Click(object sender, RoutedEventArgs e)
    {
        if (!_emulatorBusy && _emulatorPath is not null) await RefreshEmulatorAsync(_emulatorPath, persist: false);
    }

    private async Task RefreshEmulatorAsync(string path, bool persist)
    {
        SetEmulatorBusy(true);
        SetEmulatorStatus("Reading MH4U saves...");
        try
        {
            var result = await Task.Run(() => _emulatorSaves.Scan(path));
            if (persist) _emulatorSettings.Save(result.StoragePath);
            _emulatorPath = result.StoragePath;
            EmulatorSaves.Clear();
            foreach (var save in result.Saves) EmulatorSaves.Add(save);
            SynchronizeEmulatorSelection();
            OnPropertyChanged(nameof(EmulatorPathDisplay));
            var unreadable = result.Saves.Count(save => !save.IsReadable);
            var status = result.Saves.Count == 0 ? "No MH4U USA/Europe save files found" : $"{result.Saves.Count} save file{(result.Saves.Count == 1 ? string.Empty : "s")} found";
            if (unreadable > 0) status += $" | {unreadable} unreadable";
            if (result.Warnings.Count > 0) status += $" | {result.Warnings.Count} read warning(s)";
            SetEmulatorStatus(status);
            if (CustomQuestsPage.Visibility == Visibility.Visible && !App.IsSmokeTest) CustomQuestsPage.UseConfiguredEmulatorPath();
        }
        catch (Exception ex)
        {
            if (!persist) EmulatorSaves.Clear();
            SetEmulatorStatus((persist ? "Could not connect this folder: " : "Could not refresh the active path: ") + ex.GetBaseException().Message);
        }
        finally { SetEmulatorBusy(false); }
    }

    private void ClearEmulatorPath_Click(object sender, RoutedEventArgs e)
    {
        if (_emulatorBusy) return;
        try
        {
            _emulatorSettings.Save(null);
            _emulatorPath = null;
            EmulatorSaves.Clear();
            EmulatorLocations.Clear();
            EmulatorLocationPicker.Visibility = Visibility.Collapsed;
            OnPropertyChanged(nameof(EmulatorPathDisplay));
            OnPropertyChanged(nameof(CanRefreshEmulator));
            SetEmulatorStatus("No emulator path set");
            if (CustomQuestsPage.Visibility == Visibility.Visible && !App.IsSmokeTest) CustomQuestsPage.UseConfiguredEmulatorPath();
        }
        catch (Exception ex) { SetEmulatorStatus("Could not clear emulator settings: " + ex.Message); }
    }

    private void EmulatorSaveRows_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _emulatorBusy || _suppressEmulatorSaveSelection) return;
        if (EmulatorSaveRows.SelectedItem is not EmulatorSaveInfo { IsReadable: true } save) return;
        if (_character is not null && string.Equals(CurrentFilePath, save.Path, StringComparison.OrdinalIgnoreCase)) return;
        try { LoadSave(save.Path); }
        finally { SynchronizeEmulatorSelection(); }
    }

    private void SynchronizeEmulatorSelection()
    {
        var previous = _suppressEmulatorSaveSelection;
        _suppressEmulatorSaveSelection = true;
        try
        {
            EmulatorSaveRows.SelectedItem = _character is null ? null : EmulatorSaves.FirstOrDefault(save =>
                save.IsReadable && string.Equals(save.Path, CurrentFilePath, StringComparison.OrdinalIgnoreCase));
        }
        finally { _suppressEmulatorSaveSelection = previous; }
    }

    private void RunOverviewSmokeChecks()
    {
        VerifyEmulatorPathForSmoke();
        _suppressEmulatorSaveSelection = true;
        var originalWidth = Width;
        var originalHeight = Height;
        ShowPage("Overview");
        _smokeIdentity = new SaveIdentity("Smoke Hunter", 1, 999, 123456);
        _emulatorPath = Path.Combine(App.SmokeTestDirectory, "Azahar", "sdmc");
        _emulatorSettings.Save(_emulatorPath);
        if (_emulatorSettings.Load() != _emulatorPath) throw new InvalidOperationException("Emulator settings did not round-trip.");
        EmulatorSaves.Add(new EmulatorSaveInfo(Path.Combine(_emulatorPath, "user1"), "USA", 81920, new DateTime(2026, 9, 24, 12, 30, 0), _smokeIdentity, null));
        EmulatorSaves.Add(new EmulatorSaveInfo(Path.Combine(_emulatorPath, "user2"), "Europe", 81920, new DateTime(2026, 9, 23, 12, 30, 0), new SaveIdentity("Test Hunter", 0, 125, 999), null));
        EmulatorSaves.Add(new EmulatorSaveInfo(Path.Combine(_emulatorPath, "user3"), "USA", 4, new DateTime(2026, 9, 22, 12, 30, 0), null, "Synthetic unreadable slot"));
        OnPropertyChanged(nameof(HunterIdentity));
        OnPropertyChanged(nameof(EmulatorPathDisplay));
        OnPropertyChanged(nameof(CanRefreshEmulator));
        SetEmulatorStatus("3 save files found | 1 unreadable");
        EmulatorSaveRows.SelectedItem = EmulatorSaves[0];
        foreach (var light in new[] { false, true })
        {
            if (_lightTheme != light) ToggleTheme_Click(this, new RoutedEventArgs());
            foreach (var size in new[] { (Width: MinWidth, Height: MinHeight, Label: "Minimum"), (Width: originalWidth, Height: originalHeight, Label: "Default"), (Width: 1600d, Height: 1000d, Label: "Wide") })
            {
                Width = size.Width;
                Height = size.Height;
                CaptureSmokeScreenshot($"Overview-{(light ? "Light" : "Dark")}-{size.Label}");
                AssertOverviewAlignment();
                OverviewScroll.ScrollToBottom();
                CaptureSmokeScreenshot($"Overview-{(light ? "Light" : "Dark")}-{size.Label}-Bottom");
                OverviewScroll.ScrollToTop();
            }
        }
        Width = originalWidth;
        Height = originalHeight;
        if (_lightTheme) ToggleTheme_Click(this, new RoutedEventArgs());
        _smokeIdentity = null;
        _emulatorPath = null;
        EmulatorSaves.Clear();
        _emulatorSettings.Save(null);
        OnPropertyChanged(nameof(HunterIdentity));
        OnPropertyChanged(nameof(EmulatorPathDisplay));
        OnPropertyChanged(nameof(CanRefreshEmulator));
        SetEmulatorStatus("No emulator path set");
        _suppressEmulatorSaveSelection = false;
    }

    private void VerifyEmulatorPathForSmoke()
    {
        var path = Environment.GetEnvironmentVariable("CLASSIC_MH_TOOLS_VERIFY_EMULATOR_PATH");
        if (string.IsNullOrWhiteSpace(path)) return;
        var storage = _emulatorSaves.ResolveStoragePath(path);
        var locations = _emulatorSaves.DetectLocations(GetAzaharCandidates().Append(Path.Combine(path, "config")));
        if (!locations.Contains(storage, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("The requested Azahar folder was not detected.");
        var result = _emulatorSaves.Scan(storage);
        var originalHashes = result.Saves.ToDictionary(save => save.Path, save => HashSaveFile(save.Path));
        try
        {
            foreach (var save in result.Saves) EmulatorSaves.Add(save);
            foreach (var save in result.Saves.Where(save => save.IsReadable))
            {
                EmulatorSaveRows.SelectedItem = save;
                if (_character is null || !ReferenceEquals(EmulatorSaveRows.SelectedItem, save) ||
                    HunterIdentity?.Name != save.Character!.Name || CurrentFilePath != save.Path || _dirty.IsDirty)
                    throw new InvalidOperationException("Selecting a discovered character did not load and highlight it cleanly.");
            }
            var activePath = _character is null ? null : CurrentFilePath;
            _suppressEmulatorSaveSelection = true;
            try
            {
                EmulatorSaves.Clear();
                foreach (var save in _emulatorSaves.Scan(storage).Saves) EmulatorSaves.Add(save);
            }
            finally { _suppressEmulatorSaveSelection = false; }
            SynchronizeEmulatorSelection();
            if (activePath is not null && (EmulatorSaveRows.SelectedItem as EmulatorSaveInfo)?.Path != activePath)
                throw new InvalidOperationException("Refreshing the list lost the active character highlight.");
            foreach (var original in originalHashes)
                if (HashSaveFile(original.Key) != original.Value) throw new InvalidOperationException("A verified save file changed during the read-only check.");
            File.WriteAllText(Path.Combine(App.SmokeTestDirectory, "emulator-verification.json"), JsonSerializer.Serialize(new
            {
                storagePath = storage,
                detected = true,
                saves = result.Saves.Count,
                readable = result.Saves.Count(save => save.IsReadable),
                selectionAndRefreshVerified = result.Saves.Any(save => save.IsReadable),
                saveHashesUnchanged = true
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            _suppressEmulatorSaveSelection = true;
            EmulatorSaves.Clear();
            Character = null;
            _save?.ms.Dispose();
            _save = null;
            _character = null;
            _snapshot = null;
            _equipmentTail = Array.Empty<mh4edit.MonHunEquip>();
            _undo.Clear();
            _dirty.MarkClean();
            CurrentFileName = "No save loaded";
            CurrentFilePath = "No character save selected";
            RebuildCollections();
            RefreshBackups();
            RefreshValidation();
            SaveButton.IsEnabled = false;
            _suppressEmulatorSaveSelection = false;
        }
    }

    private static string HashSaveFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private void AssertOverviewAlignment()
    {
        var character = OverviewCharacterSection.TranslatePoint(new Point(), OverviewSections);
        var recent = OverviewRecentSection.TranslatePoint(new Point(), OverviewSections);
        var safety = OverviewSafetySection.TranslatePoint(new Point(), OverviewSections);
        var actions = OverviewActionsSection.TranslatePoint(new Point(), OverviewSections);
        var emulator = OverviewEmulatorSection.TranslatePoint(new Point(), OverviewSections);
        var emulatorTitle = EmulatorHeading.TranslatePoint(new Point(), OverviewSections);
        var actionsTitle = QuickActionsHeading.TranslatePoint(new Point(), OverviewSections);
        if (Math.Abs(character.X - recent.X) > 1 || Math.Abs(safety.X - actions.X) > 1 ||
            Math.Abs(character.X - emulator.X) > 1 || Math.Abs(emulatorTitle.Y - actionsTitle.Y) > 1 ||
            Math.Abs(OverviewCharacterSection.ActualWidth - OverviewRecentSection.ActualWidth) > 1 ||
            Math.Abs(OverviewSafetySection.ActualWidth - OverviewActionsSection.ActualWidth) > 1)
            throw new InvalidOperationException("Overview columns are not aligned.");
        if (ActiveHunterName.Text != "Smoke Hunter" || EmulatorSaveRows.Items.Count != 3)
            throw new InvalidOperationException("Overview identity or emulator row bindings did not render.");
        var row = (ListBoxItem)EmulatorSaveRows.ItemContainerGenerator.ContainerFromIndex(0);
        var highlight = (System.Windows.Controls.Border)row.Template.FindName("RowHighlight", row);
        if (!row.IsSelected || !Equals(highlight.Background, FindResource("SelectionBrush")))
            throw new InvalidOperationException("The selected emulator character is not highlighted.");
        var unreadable = (ListBoxItem)EmulatorSaveRows.ItemContainerGenerator.ContainerFromIndex(2);
        if (unreadable.IsEnabled) throw new InvalidOperationException("Unreadable emulator rows must not load a save.");
    }
}
