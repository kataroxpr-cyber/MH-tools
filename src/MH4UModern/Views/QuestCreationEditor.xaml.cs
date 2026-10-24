using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using MH4UModern.ViewModels;
using ClassicMhTools.Core.CustomQuests;

namespace MH4UModern.Views;

public partial class QuestCreationEditor : UserControl
{
    private IQuestEditorLayout? Model => DataContext as IQuestEditorLayout;
    public event EventHandler<QuestChoiceRequest>? PickerRequested;
    public event EventHandler<QuestMonsterRequest>? MonsterRequested;
    public event EventHandler? HelperExportRequested;
    private void HelperExport_Click(object sender, RoutedEventArgs e) => HelperExportRequested?.Invoke(this, EventArgs.Empty);
    public QuestCreationEditor()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            MapPreview.Close(); Tabs.SelectedIndex = 0;
            // Wait for the new ItemsSource before selecting; immediate selection can be cleared by rebinding.
            var context = e.NewValue;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.DataBind, new Action(() =>
            {
                if (ReferenceEquals(DataContext, context) && LanguagePicker.Items.Count != 0) LanguagePicker.SelectedIndex = 0;
            }));
        };
    }
    private void Map_Click(object sender, RoutedEventArgs e)
    {
        if (Model is { } model) PickerRequested?.Invoke(this, new("Choose map", model.MapChoices, model.Map, choice => model.Map = choice));
    }
    private void EditMonster(CreationMonster row)
    {
        if (Model is not { } model) return;
        MonsterRequested?.Invoke(this, new(row, false, changed => model.ApplyMonsterEdit(row, changed), model.IntruderFields));
    }
    private void Monster_Click(object sender, MouseButtonEventArgs e)
    { if (ItemsControl.ContainerFromElement(MonsterRows, e.OriginalSource as DependencyObject) is ListBoxItem { DataContext: CreationMonster row }) { EditMonster(row); e.Handled = true; } }
    private void Monster_KeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Enter && MonsterRows.SelectedItem is CreationMonster row) { EditMonster(row); e.Handled = true; } }
    private void ObjectiveTarget_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is CreationObjective row && row.Targets.Count != 0)
            PickerRequested?.Invoke(this, new(row.Type?.Id == 2 ? "Choose item" : "Choose monster", row.Targets, row.Target, choice => row.Target = choice));
    }
    private void ItemPicker_Requested(object? sender, QuestChoiceRequest e) => PickerRequested?.Invoke(this, e);
    private void AddMonster_Click(object sender, RoutedEventArgs e)
    {
        if (Model == null || !Model.CanAddMonster) return;
        var model = Model;
        MonsterRequested?.Invoke(this, new(new CreationMonster(new QuestMonsterDraft(), (byte)(model.Map?.Id ?? 11)), true, row =>
        {
            if (row == null || !model.CanAddMonster) return;
            model.Monsters.Add(row); model.ApplySharedStats(row);
            if (row.Kind?.Id == 1) { model.IntruderFields[0].Value = row.IntruderDelay.Value; model.IntruderFields[1].Value = row.IntruderAppearance.Value; }
            MonsterRows.SelectedItem = row; MonsterRows.ScrollIntoView(row);
        }, model.IntruderFields));
    }
    private void Artwork_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is CreationNumber field)
            PickerRequested?.Invoke(this, new("Choose quest artwork", QuestCreationChoices.Previews, field.SelectedChoice, choice => field.SelectedChoice = choice));
    }
    private void Spawn_Opened(object? sender, EventArgs e)
    {
        if (sender is CreationFieldInput { DataContext: CreationNumber field } input && field.Name is "Hunter spawn" or "Hunter spawn area" && Model?.Map != null)
            MapPreview.Open(input, (byte)Model.Map.Id);
    }
    private void Spawn_Closed(object? sender, EventArgs e) => MapPreview.Close();
    private void Presets_Click(object sender, RoutedEventArgs e)
    {
        if (Model is { IsLayoutLocked: false } && MessageBox.Show(Window.GetWindow(this), "Replace supplies and rewards with the current rank/monster presets?", "Quest presets", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes) Model.ApplyPresets();
    }
    private void CopyEnglish_Click(object sender, RoutedEventArgs e)
    {
        if (Model != null && MessageBox.Show(Window.GetWindow(this), "Replace all translated text with the English text?", "Quest text",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)
        {
            try { Model.CopyEnglish(); }
            catch (System.IO.InvalidDataException ex) { MessageBox.Show(Window.GetWindow(this), ex.Message, "Quest text", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
    }
    internal void ShowSmokeTab(int index)
    {
        Tabs.SelectedIndex = index;
        if (index == 0) GeneralScroll.ScrollToTop();
        Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        UpdateLayout();
        if (Tabs.ActualWidth < 700 || Tabs.ActualHeight < 220) throw new InvalidOperationException("Quest creator tab is too small.");
        if (Tabs.Items.Count != 3) throw new InvalidOperationException("Expected three creation tabs.");
        if (index == 0 && (GeneralFields.Items.Count != 14 || GeneralFields.ActualHeight < 300))
            throw new InvalidOperationException($"Quest creator general fields did not render: {GeneralFields.Items.Count} fields, height {GeneralFields.ActualHeight}.");
        if (index == 0 && ArtworkRows.Items.Count != 5) throw new InvalidOperationException("Compact General artwork is missing.");
        if (index == 0 && LanguagePicker.SelectedItem == null) throw new InvalidOperationException("Quest language selection was lost on reopen.");
        if (index == 0 && HelperSettings.Visibility != Model?.HelperVisibility) throw new InvalidOperationException("Helper settings visibility is inconsistent with this quest editor.");
        if (index == 1 && (ObjectiveFields.Items.Count != 3 || MonsterRows.Items.Count != Model?.Monsters.Count)) throw new InvalidOperationException("Objective/monster controls did not render.");
        if (index == 2) { SupplyBox.SelectSmoke(0); SupplyBox.VerifySmokeRender(); RewardBox.SelectSmoke(0); RewardBox.VerifySmokeRender(); }
    }
    internal void OpenMapSmoke() => Map_Click(this, new());
    internal void VerifyHelperControlSmoke(bool toggle = false)
    {
        ShowSmokeTab(0);
        if (!HelperSettings.IsVisible || !HelperCheckBox.IsEnabled || HelperCheckBox.IsChecked != Model!.ExperimentalStoryHelpers ||
            HelperExportButton.IsEnabled != Model.ExperimentalStoryHelpers || HelperSettings.ActualWidth < 280)
            throw new InvalidOperationException("Experimental helper controls failed to render/bind.");
        if (!toggle) return;
        var before = Model.ExperimentalStoryHelpers;
        HelperCheckBox.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, !before);
        Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        if (Model.ExperimentalStoryHelpers == before || HelperExportButton.IsEnabled == before)
            throw new InvalidOperationException("Helper checkbox did not update authoring intent and export availability.");
    }
    internal void OpenMonsterSmoke(int index = 0) => EditMonster(Model!.Monsters[index]);
    internal void VerifyContextualSmoke()
    {
        ShowSmokeTab(1);
        if (WaveConditions.Items.Count != Model!.WaveConditions.Count) throw new InvalidOperationException("Wave controls do not match occupied small-monster waves.");
        if (Children<CreationFieldInput>(this).Any(c => Model.IntruderFields.Contains(c.DataContext))) throw new InvalidOperationException("Intruder controls are still on the parent editor.");
        ShowSmokeTab(0);
        if (ArtworkRows.ItemContainerGenerator.ContainerFromIndex(0) is not FrameworkElement art || art.ActualWidth > 52 || art.ActualHeight > 52) throw new InvalidOperationException("General artwork is not compact.");
    }
    internal void AddMonsterSmoke() => AddMonster_Click(this, new());
    internal void OpenArtworkSmoke(int index) => Artwork_Click(new Button { DataContext = Model!.Artwork[index] }, new());
    internal void VerifyMapPreviewSmoke(string file)
    {
        ShowSmokeTab(0);
        var input = Children<CreationFieldInput>(GeneralFields).Single(c => c.DataContext == Model!.General[15]);
        MapPreview.Open(input, (byte)Model!.Map!.Id); MapPreview.SaveSmoke(file); MapPreview.Close();
    }
    internal QuestItemBoxEditor SupplySmoke => SupplyBox;
    internal QuestItemBoxEditor RewardSmoke => RewardBox;
    private static IEnumerable<T> Children<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in Children<T>(child)) yield return nested;
        }
    }
    internal void ShowRequirementsSmoke()
    {
        ShowSmokeTab(0); GeneralScroll.ScrollToEnd(); UpdateLayout();
        foreach (var (index, id) in new[] { (10, 32u), (11, 36u) })
        {
            var input = Children<CreationFieldInput>(GeneralFields).Single(c => c.DataContext == Model!.General[index]);
            input.ChooseSmoke(id);
        }
    }
}
