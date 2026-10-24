using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MH4UModern.ViewModels;

namespace MH4UModern.Views;

public sealed class QuestMonsterRequest(CreationMonster monster, bool isNew, Action<CreationMonster?> apply, IReadOnlyList<CreationNumber>? intruderSettings = null) : EventArgs
{
    public CreationMonster Monster { get; } = monster;
    public bool IsNew { get; } = isNew;
    public Action<CreationMonster?> Apply { get; } = apply;
    public IReadOnlyList<CreationNumber>? IntruderSettings { get; } = intruderSettings;
}

public partial class QuestMonsterEditor : UserControl
{
    private Action<CreationMonster?>? _apply;
    private IInputElement? _restoreFocus;
    public bool IsOpen => Visibility == Visibility.Visible;
    public event EventHandler<QuestChoiceRequest>? PickerRequested;
    public QuestMonsterEditor() => InitializeComponent();
    public void Open(QuestMonsterRequest request)
    {
        _restoreFocus = Keyboard.FocusedElement; _apply = request.Apply;
        var copy = request.Monster.Copy();
        if (request.IntruderSettings != null) { copy.IntruderDelay.Value = request.IntruderSettings[0].Value; copy.IntruderAppearance.Value = request.IntruderSettings[1].Value; }
        DataContext = copy;
        RemoveButton.Visibility = request.IsNew || copy.IsLayoutLocked ? Visibility.Collapsed : Visibility.Visible;
        ErrorLabel.Text = ""; Visibility = Visibility.Visible; MonsterChoice.Focus();
    }
    public void Close()
    {
        MapPreview.Close();
        Visibility = Visibility.Collapsed; _apply = null; DataContext = null;
        if (_restoreFocus != null) Keyboard.Focus(_restoreFocus);
        _restoreFocus = null;
    }
    public bool HandleKey(KeyEventArgs e)
    {
        if (!IsOpen) return false;
        if (e.Key == Key.Escape) { Close(); return true; }
        return (Keyboard.Modifiers & ModifierKeys.Control) != 0 && e.Key is Key.S or Key.O or Key.Z or Key.Y or Key.F;
    }
    private void Choose_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is CreationMonster draft) PickerRequested?.Invoke(this, new("Choose monster", draft.Species, draft.Monster, choice => draft.Monster = choice));
    }
    private void Done_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not CreationMonster draft) return;
        try { _ = draft.Build(); }
        catch (InvalidDataException ex) { ErrorLabel.Text = ex.Message; return; }
        var apply = _apply; Close(); apply?.Invoke(draft);
    }
    private void Remove_Click(object sender, RoutedEventArgs e) { var apply = _apply; Close(); apply?.Invoke(null); }
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private void Spawn_Opened(object? sender, EventArgs e)
    {
        if (sender is CreationFieldInput { DataContext: CreationNumber { Name: "Spawn area" } } input && DataContext is CreationMonster draft) MapPreview.Open(input, draft.MapId);
    }
    private void Spawn_Closed(object? sender, EventArgs e) => MapPreview.Close();
    internal CreationMonster SmokeDraft => (CreationMonster)DataContext;
    internal void ChooseSmoke() => Choose_Click(this, new());
    internal void DoneSmoke() => Done_Click(this, new());
    internal void RemoveSmoke() => Remove_Click(this, new());
    internal void VerifySmokeRender()
    {
        UpdateLayout();
        if (!IsOpen || Fields.Items.Count != SmokeDraft.Numbers.Count() || Fields.ActualHeight < 60 || MonsterChoice.ActualWidth < 120) throw new InvalidOperationException("Monster settings overlay did not render.");
    }
}
