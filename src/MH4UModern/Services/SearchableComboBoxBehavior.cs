using System.Collections;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Threading;

namespace MH4UModern.Services;

/// <summary>
/// Small per-combo autocomplete/filter helper. Each ComboBox receives its own filtered
/// collection, so typing in one picker never filters another picker that happens to use
/// the same upstream list.
/// </summary>
public static class SearchableComboBoxBehavior
{
    private sealed class State
    {
        public List<object> Source { get; set; } = new();
        public ObservableCollection<object> Filtered { get; } = new();
        public string DisplayMemberPath { get; set; } = string.Empty;
        public TextBox? Editor { get; set; }
        public bool Hooked { get; set; }
        public bool Suppress { get; set; }
    }

    private static readonly ConditionalWeakTable<ComboBox, State> States = new();

    public static void Configure(
        ComboBox combo,
        IEnumerable? items,
        string displayMemberPath,
        string selectedValuePath,
        object? selectedValue,
        bool enabled = true)
    {
        var state = States.GetValue(combo, _ => new State());
        state.Source = items?.Cast<object>().ToList() ?? new List<object>();
        state.DisplayMemberPath = displayMemberPath;

        state.Suppress = true;
        try
        {
            Replace(state.Filtered, state.Source);
            combo.DisplayMemberPath = displayMemberPath;
            combo.SelectedValuePath = selectedValuePath;
            combo.ItemsSource = state.Filtered;
            combo.IsEditable = true;
            combo.IsTextSearchEnabled = false;
            combo.StaysOpenOnEdit = true;
            combo.IsEnabled = enabled;
            combo.SelectedValue = selectedValue;
        }
        finally
        {
            state.Suppress = false;
        }

        EnsureHooks(combo, state);
    }

    private static void EnsureHooks(ComboBox combo, State state)
    {
        if (!state.Hooked)
        {
            state.Hooked = true;
            combo.Loaded += (_, _) => HookEditor(combo, state);
            combo.DropDownOpened += (_, _) =>
            {
                HookEditor(combo, state);
                if (state.Editor is null) return;
                var selectedText = Display(combo.SelectedItem, state.DisplayMemberPath);
                if (string.Equals(state.Editor.Text, selectedText, StringComparison.OrdinalIgnoreCase))
                {
                    state.Suppress = true;
                    try { Replace(state.Filtered, state.Source); }
                    finally { state.Suppress = false; }
                }
            };
        }

        combo.Dispatcher.BeginInvoke(new Action(() => HookEditor(combo, state)), DispatcherPriority.Loaded);
    }

    private static void HookEditor(ComboBox combo, State state)
    {
        combo.ApplyTemplate();
        if (combo.Template.FindName("PART_EditableTextBox", combo) is not TextBox editor) return;
        if (ReferenceEquals(state.Editor, editor)) return;

        if (state.Editor is not null)
            state.Editor.TextChanged -= Editor_TextChanged;

        state.Editor = editor;
        editor.Tag = combo;
        editor.TextChanged += Editor_TextChanged;
        editor.GotKeyboardFocus += (_, _) => editor.SelectAll();
    }

    private static void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox { Tag: ComboBox combo } editor) return;
        if (!States.TryGetValue(combo, out var state) || state.Suppress || !editor.IsKeyboardFocusWithin) return;

        var query = editor.Text.Trim();
        var caret = editor.CaretIndex;
        var filtered = query.Length == 0
            ? state.Source
            : state.Source.Where(item => Display(item, state.DisplayMemberPath).Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        state.Suppress = true;
        try
        {
            Replace(state.Filtered, filtered);
            combo.Text = query;
            editor.Text = query;
            editor.CaretIndex = Math.Min(caret, editor.Text.Length);
            if (!combo.IsDropDownOpen) combo.IsDropDownOpen = true;
        }
        finally
        {
            state.Suppress = false;
        }
    }

    private static string Display(object? item, string property)
    {
        if (item is null) return string.Empty;
        if (string.IsNullOrWhiteSpace(property)) return item.ToString() ?? string.Empty;
        try
        {
            var p = item.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public);
            return p?.GetValue(item)?.ToString() ?? item.ToString() ?? string.Empty;
        }
        catch
        {
            return item.ToString() ?? string.Empty;
        }
    }

    private static void Replace(ObservableCollection<object> target, IEnumerable<object> items)
    {
        target.Clear();
        foreach (var item in items) target.Add(item);
    }
}
