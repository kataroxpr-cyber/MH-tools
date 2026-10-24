using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MH4UModern.Services;

namespace MH4UModern;

public partial class DecorationPickerWindow : Window
{
    private readonly ObservableCollection<string> _skills = new();
    private readonly ObservableCollection<DecorationChoice> _decorations = new();
    private readonly ushort _currentId;

    public ushort SelectedDecorationId { get; private set; }

    public DecorationPickerWindow(ushort currentId)
    {
        InitializeComponent();
        _currentId = currentId;
        SelectedDecorationId = currentId;
        SkillList.ItemsSource = _skills;
        DecorationList.ItemsSource = _decorations;

        var current = DecorationCatalogService.Get(currentId);
        CurrentText.Text = currentId == 0 || current is null ? "Current: None" : $"Current: {current.Name}";
        RefreshSkills();

        var currentSkill = current?.Effects.FirstOrDefault(effect => effect.Points > 0)?.Skill;
        if (!string.IsNullOrWhiteSpace(currentSkill))
            SkillList.SelectedItem = currentSkill;
    }

    private void SkillSearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshSkills();

    private void RefreshSkills()
    {
        var query = SkillSearchBox?.Text?.Trim() ?? string.Empty;
        var selected = SkillList?.SelectedItem as string;
        _skills.Clear();
        foreach (var skill in DecorationCatalogService.Skills.Where(skill => query.Length == 0 || skill.Contains(query, StringComparison.OrdinalIgnoreCase)))
            _skills.Add(skill);
        if (SkillList is not null && selected is not null && _skills.Contains(selected)) SkillList.SelectedItem = selected;
    }

    private void SkillList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _decorations.Clear();
        if (SkillList.SelectedItem is not string skill)
        {
            DecorationPrompt.Text = "Select a skill to see its available decorations.";
            return;
        }
        DecorationPrompt.Text = $"Choose which {skill} decoration to place in this slot.";
        foreach (var decoration in DecorationCatalogService.ForSkill(skill)) _decorations.Add(decoration);
    }

    private void DecorationList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => SelectButton.IsEnabled = DecorationList.SelectedItem is DecorationChoice;

    private void DecorationList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DecorationList.SelectedItem is DecorationChoice) AcceptSelection();
    }

    private void Select_Click(object sender, RoutedEventArgs e) => AcceptSelection();

    private void AcceptSelection()
    {
        if (DecorationList.SelectedItem is not DecorationChoice choice) return;
        SelectedDecorationId = choice.EditorId;
        DialogResult = true;
    }

    private void None_Click(object sender, RoutedEventArgs e)
    {
        SelectedDecorationId = 0;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        SelectedDecorationId = _currentId;
        DialogResult = false;
    }
}
