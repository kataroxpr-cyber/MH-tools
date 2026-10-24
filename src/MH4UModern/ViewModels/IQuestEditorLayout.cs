using System.Collections.ObjectModel;
using System.Windows;

namespace MH4UModern.ViewModels;

public sealed record QuestWaveCondition(int Wave, IReadOnlyList<CreationNumber> Fields);

public interface IQuestEditorLayout
{
    QuestChoice? Map { get; set; }
    QuestChoice? Type { get; set; }
    IReadOnlyList<QuestChoice> MapChoices { get; }
    IReadOnlyList<QuestChoice> TypeChoices { get; }
    IReadOnlyList<CreationNumber> General { get; }
    IReadOnlyList<CreationNumber> MainFields { get; }
    IReadOnlyList<CreationNumber> FenceFields { get; }
    Visibility FenceVisibility { get; }
    Visibility HelperVisibility { get; }
    bool ExperimentalStoryHelpers { get; set; }
    bool CanChooseStoryHelpers { get; }
    IReadOnlyList<CreationNumber> Artwork { get; }
    IReadOnlyList<CreationNumber> IntruderFields { get; }
    IReadOnlyList<QuestWaveCondition> WaveConditions { get; }
    IReadOnlyList<CreationObjective> Objectives { get; }
    ObservableCollection<CreationMonster> Monsters { get; }
    ObservableCollection<CreationItem> Supplies { get; }
    ObservableCollection<CreationItem> Rewards { get; }
    IReadOnlyList<CreationLanguage> Languages { get; }
    CreationNumber Stars { get; }
    string QuestTitle { get; set; }
    int TitleMaxLength { get; }
    bool IsLayoutLocked { get; }
    bool CanAddMonster { get; }
    string PresetStatus { get; }
    void ApplyMonsterEdit(CreationMonster original, CreationMonster? changed);
    void ApplySharedStats(CreationMonster changed);
    void CopyEnglish();
    void ApplyPresets();
}
