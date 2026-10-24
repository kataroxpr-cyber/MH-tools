using mh4edit;

namespace MH4UModern.ViewModels;

public sealed class QuestRowViewModel : BindableBase
{
    public QuestRowViewModel(int index, GuildQuest model)
    {
        Index = index;
        Model = model;
    }

    public int Index { get; }
    public int Slot => Index + 1;
    public GuildQuest Model { get; }
    public string Owner => string.IsNullOrWhiteSpace(Model.Owner) ? "(No owner)" : Model.Owner.TrimEnd('\0');
    public string MonsterA => MonsterName(Model.MonsterAId);
    public string MonsterB => Model.MonsterBId == 0 ? "—" : MonsterName(Model.MonsterBId);
    public string Level => $"Lv {Model.CurrentLevel}";
    public string Summary => $"{MonsterA}{(Model.MonsterBId == 0 ? string.Empty : $" + {MonsterB}")} · {Level}";

    public void Refresh()
    {
        Raise(nameof(Owner));
        Raise(nameof(MonsterA));
        Raise(nameof(MonsterB));
        Raise(nameof(Level));
        Raise(nameof(Summary));
    }

    private static string MonsterName(byte id)
        => id < GuildQuestStatic.allMonsters.Length ? GuildQuestStatic.allMonsters[id] : $"Monster {id}";
}
