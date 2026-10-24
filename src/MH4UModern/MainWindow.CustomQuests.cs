using System.Buffers.Binary;
using ClassicMhTools.Core.CustomQuests;

namespace MH4UModern;

public partial class MainWindow
{
    private void RunCustomQuestSmokeChecks()
    {
        var data = new byte[0x400];
        BinaryPrimitives.WriteUInt32LittleEndian(data, 0xA0);
        "v005"u8.CopyTo(data.AsSpan(4));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0xC0), 60013);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0xC2), 10);
        data[0xC4] = 11;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0xA8), 12000);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x28), 0x200);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x200), 0x240);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x240), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x244), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x268), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x26C), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x290), uint.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0xBC), 0x100);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x100), 0x120);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x120), 0x140);
        System.Text.Encoding.Unicode.GetBytes("Synthetic quest").CopyTo(data, 0x140);
        CustomQuestsPage.LoadSmokeQuest(new QuestDocument(data));
        NavCustomQuests.IsChecked = true;
        ShowPage("CustomQuests");
        var previousWidth = Width;
        var previousHeight = Height;
        var previousLight = _lightTheme;
        try
        {
            foreach (var light in new[] { false, true })
            {
                if (_lightTheme != light) ToggleTheme_Click(this, new System.Windows.RoutedEventArgs());
                foreach (var (width, height, name) in new[] { (1080d, 700d, "Minimum"), (1320d, 820d, "Default"), (1720d, 1020d, "Wide") })
                {
                    Width = width;
                    Height = height;
                    UpdateLayout();
                    CustomQuestsPage.VerifyEditorRender();
                    if (CustomQuestsPage.ActualWidth < 750 || CustomQuestsPage.ActualHeight < 500)
                        throw new InvalidOperationException("Custom Quests layout is too small.");
                    CaptureSmokeScreenshot($"CustomQuests-{name}-{(light ? "Light" : "Dark")}");
                    CustomQuestsPage.CloseSmokeEditor();
                    CustomQuestsPage.VerifyBrowserRender();
                    CaptureSmokeScreenshot($"CustomQuests-List-{name}-{(light ? "Light" : "Dark")}");
                    CustomQuestsPage.OpenSmokeRow();
                    CustomQuestsPage.VerifyEditorRender();
                }
            }
            CustomQuestsPage.CloseSmokeEditor();
            CustomQuestsPage.VerifyAutomaticConnection(new QuestDocument(data));
            CustomQuestsPage.StartCreatorSmoke();
            CustomQuestsPage.VerifyCreatorNamedOptionsSmoke();
            foreach (var light in new[] { false, true })
            {
                if (_lightTheme != light) ToggleTheme_Click(this, new System.Windows.RoutedEventArgs());
                foreach (var (width, height, name) in new[] { (1080d, 700d, "Minimum"), (1320d, 820d, "Default"), (1720d, 1020d, "Wide") })
                {
                    Width = width; Height = height;
                    for (var tab = 0; tab < 3; tab++)
                    {
                        UpdateLayout();
                        CustomQuestsPage.VerifyCreatorTab(tab);
                        CaptureSmokeScreenshot($"QuestCreator-Tab{tab}-{name}-{(light ? "Light" : "Dark")}");
                    }
                }
                Width = light ? 1320 : 1080; Height = light ? 820 : 700;
                CustomQuestsPage.ShowRequirementsSmoke();
                CaptureSmokeScreenshot($"QuestCreator-Requirements-{(light ? "Light" : "Dark")}");
                CustomQuestsPage.OpenMonsterSettingsSmoke();
                CaptureSmokeScreenshot($"QuestCreator-Monster-Settings-{(light ? "Light" : "Dark")}");
                CustomQuestsPage.VerifyMonsterSettingsSmoke();
                foreach (var map in new[] { true, false })
                {
                    CustomQuestsPage.OpenCreatorPickerSmoke(map);
                    CaptureSmokeScreenshot($"QuestCreator-{(map ? "Map" : "Monster")}-Picker-{(light ? "Light" : "Dark")}");
                    CustomQuestsPage.VerifyCreatorPickerSmoke(map);
                }
                foreach (var reward in new[] { false, true })
                {
                    CustomQuestsPage.OpenCreatorItemPickerSmoke(reward);
                    CaptureSmokeScreenshot($"QuestCreator-{(reward ? "Reward" : "Supply")}-Picker-{(light ? "Light" : "Dark")}");
                    CustomQuestsPage.VerifyCreatorItemPickerSmoke(reward);
                }
            }
            CustomQuestsPage.FinishCreatorSmoke();
            CustomQuestsPage.VerifyHelperWorkflowSmoke();
            foreach (var light in new[] { false, true })
            {
                if (_lightTheme != light) ToggleTheme_Click(this, new System.Windows.RoutedEventArgs());
                foreach (var (width, height, name) in new[] { (1080d, 700d, "Minimum"), (1320d, 820d, "Default") })
                {
                    Width = width; Height = height; UpdateLayout();
                    CustomQuestsPage.VerifyHelperRenderSmoke();
                    CaptureSmokeScreenshot($"QuestHelpers-{name}-{(light ? "Light" : "Dark")}");
                }
            }
            CustomQuestsPage.DiscardSmokeEditor();
            CustomQuestsPage.VerifySaveInstallFlowSmoke();
            CustomQuestsPage.VerifyImportedAliasingSmoke();
            foreach (var light in new[] { false, true })
            {
                if (_lightTheme != light) ToggleTheme_Click(this, new System.Windows.RoutedEventArgs());
                CustomQuestsPage.VerifyUnifiedImportedSmoke();
                foreach (var (width, height, name) in new[] { (1080d, 700d, "Minimum"), (1320d, 820d, "Default"), (1720d, 1020d, "Wide") })
                {
                    Width = width; Height = height;
                    for (var tab = 0; tab < 3; tab++) { UpdateLayout(); CustomQuestsPage.VerifyCreatorTab(tab); CaptureSmokeScreenshot($"QuestImported-Tab{tab}-{name}-{(light ? "Light" : "Dark")}"); }
                }
                Width = light ? 1320 : 1080; Height = light ? 820 : 700;
                CustomQuestsPage.OpenIntruderDetailsSmoke(); CaptureSmokeScreenshot($"QuestImported-Intruder-Details-{(light ? "Light" : "Dark")}"); CustomQuestsPage.CloseIntruderDetailsSmoke();
                CustomQuestsPage.DiscardSmokeEditor();
            }
            foreach (var light in new[] { false, true })
            {
                if (_lightTheme != light) ToggleTheme_Click(this, new System.Windows.RoutedEventArgs());
                Width = light ? 1320 : 1080; Height = light ? 820 : 700;
                CustomQuestsPage.VerifyExpansionSmoke();
                CustomQuestsPage.OpenArtworkPickerSmoke(); CaptureSmokeScreenshot($"QuestArtwork-Picker-{(light ? "Light" : "Dark")}"); CustomQuestsPage.FinishArtworkPickerSmoke();
                CustomQuestsPage.VerifyCreatorTab(1); CaptureSmokeScreenshot($"QuestExpanded-Objectives-{(light ? "Light" : "Dark")}");
                CustomQuestsPage.OpenIntruderDetailsSmoke(); CaptureSmokeScreenshot($"QuestExpanded-Intruder-Details-{(light ? "Light" : "Dark")}"); CustomQuestsPage.CloseIntruderDetailsSmoke();
                CustomQuestsPage.VerifyMapPreviewSmoke($"QuestSpawn-Map-{(light ? "Light" : "Dark")}");
                CustomQuestsPage.DiscardSmokeEditor();
            }
            var fixtures = Environment.GetEnvironmentVariable("CLASSIC_MH_QUEST_FIXTURES");
            if (!string.IsNullOrWhiteSpace(fixtures))
            {
                CustomQuestsPage.LoadPrivateSmokeFixtures(fixtures);
                Width = 1320;
                Height = 820;
                UpdateLayout();
                CaptureSmokeScreenshot("CustomQuests-Populated-Fixture");
                CustomQuestsPage.CloseSmokeEditor();
                CustomQuestsPage.VerifyBrowserRender();
                CaptureSmokeScreenshot("CustomQuests-Populated-List");
            }
        }
        finally
        {
            CustomQuestsPage.DiscardSmokeEditor();
            Width = previousWidth;
            Height = previousHeight;
            if (_lightTheme != previousLight) ToggleTheme_Click(this, new System.Windows.RoutedEventArgs());
        }
    }
}
