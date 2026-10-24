using System.IO;
using System.Windows;
using ClassicMhTools.Core.CustomQuests;
using MH4UModern.Services;
using MH4UModern.ViewModels;
using Microsoft.Win32;

namespace MH4UModern.Views;

public partial class CustomQuestsView
{
    private void RathianHelpers_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        if (!ConfirmClose()) return;
        StartHelperDraft();
    });

    private void StartHelperDraft()
    {
        var draft = StoryHelperExperiment.CreateRathian(NextEventId(), QuestPresentationService.Catalog);
        Select(new(draft.Build(), "Draft", "Unsaved helper experiment", Creation: draft), true);
        SetStatus("Experimental Rathian draft; no game files or mods changed");
    }

    private void CreatorHelperExport_Requested(object? sender, EventArgs e) => Run(() =>
    {
        System.Windows.Input.Keyboard.ClearFocus();
        var draft = Creator?.BuildDraft() ?? throw new InvalidOperationException("Open a native quest first.");
        QuestCreationPolicy.ValidateForInstallation(draft, QuestPresentationService.Catalog);
        if (!draft.ExperimentalStoryHelpers) return;
        var input = new OpenFileDialog { Title = "Select decompressed code.bin from the inspected USA update", Filter = "Decompressed executable|code.bin|Binary files|*.bin" };
        if (input.ShowDialog(Owner) != true) return;
        if (new FileInfo(input.FileName).Length != StoryHelperExperiment.CodeLength) throw new InvalidDataException("Unexpected code.bin length. The unmodified, decompressed USA update executable is required.");
        var bundle = StoryHelperExperiment.BuildBundle(draft, File.ReadAllBytes(input.FileName), QuestPresentationService.Catalog);
        var output = new SaveFileDialog { Title = "Export experimental quest and patch (not installed)", Filter = "Experiment bundle|*.zip", FileName = $"MH4U-Ace-Hunters-{draft.Id}-EXPERIMENTAL.zip", AddExtension = true };
        if (output.ShowDialog(Owner) != true) return;
        if (!Path.GetExtension(output.FileName).Equals(".zip", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Export to a .zip file, not to a game or save file.");
        if (File.Exists(output.FileName)) File.Copy(output.FileName, output.FileName + "." + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ".bak", false);
        var temp = output.FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temp, bundle); File.Move(temp, output.FileName, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        SetStatus("Experimental bundle exported; quest and patch NOT installed: " + output.FileName);
        MessageBox.Show(Owner, "Bundle exported, not installed. In-game helper behavior is unverified.\n\nUse an offline test profile with copied saves. Follow README.txt in the bundle. Unchecking the setting does not remove an installed code patch.", "Ace Hunters experiment", MessageBoxButton.OK, MessageBoxImage.Warning);
    });

    internal void VerifyHelperWorkflowSmoke()
    {
        if (!App.IsSmokeTest) throw new InvalidOperationException("Smoke-only operation.");
        Select(null);
        StartHelperDraft();
        var bytes = Creator!.Build().ToArray();
        if (!SaveLibraryCore() || Creator!.IsDirty) throw new InvalidOperationException("Helper draft did not save locally.");
        CreatorPanel.VerifyHelperControlSmoke(toggle: true);
        if (!Creator.IsDirty || Creator.ExperimentalStoryHelpers || !bytes.AsSpan().SequenceEqual(Creator.Build().ToArray()))
            throw new InvalidOperationException("Helper intent is not dirty-tracked or changed MIB flags.");
        if (!SaveLibraryCore() || ReadRow(_sourcePath!, "Library").Creation!.ExperimentalStoryHelpers)
            throw new InvalidOperationException("Helper-off source failed to persist.");
        CreatorPanel.VerifyHelperControlSmoke(toggle: true);
        if (!SaveLibraryCore() || !ReadRow(_sourcePath!, "Library").Creation!.ExperimentalStoryHelpers)
            throw new InvalidOperationException("Helper-on source failed to persist.");
        var path = _sourcePath!;
        Select(null); Select(ReadRow(path, "Library"));
        Creator!.Map = QuestCreationChoices.Maps.Single(m => m.Id == 11);
        var rejected = false;
        try { Creator.BuildDraft(); } catch (InvalidDataException) { rejected = true; }
        if (!rejected || !Creator.CanChooseStoryHelpers) throw new InvalidOperationException("Wrong-map helpers were accepted or could not be unchecked.");
        Creator.ExperimentalStoryHelpers = false;
        if (Creator.CanChooseStoryHelpers) throw new InvalidOperationException("Helper selection is enabled outside Battlequarters.");
        Select(ReadRow(path, "Library"));
        CreatorPanel.VerifyHelperControlSmoke();
    }

    internal void VerifyHelperRenderSmoke() => CreatorPanel.VerifyHelperControlSmoke();
}
