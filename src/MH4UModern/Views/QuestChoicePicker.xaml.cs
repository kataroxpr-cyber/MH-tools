using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MH4UModern.ViewModels;

namespace MH4UModern.Views;

public sealed class QuestChoiceRequest : EventArgs
{
    public string Title { get; }
    public IReadOnlyList<QuestChoice> Choices { get; }
    public QuestChoice? Current { get; }
    public Action<QuestChoice> Apply { get; }
    public QuestChoiceRequest(string title, IReadOnlyList<QuestChoice> choices, QuestChoice? current, Action<QuestChoice> apply)
    { Title = title; Choices = choices; Current = current; Apply = apply; }
}

public partial class QuestChoicePicker : UserControl
{
    private const int PageSize = 48;
    private IReadOnlyList<QuestChoice> _source = [];
    private QuestChoice[] _filtered = [];
    private QuestChoice? _current;
    private Action<QuestChoice>? _apply;
    private IInputElement? _restoreFocus;
    private int _page;
    public bool IsOpen => Visibility == Visibility.Visible;
    public QuestChoicePicker() => InitializeComponent();

    public void Open(QuestChoiceRequest request)
    {
        _restoreFocus = Keyboard.FocusedElement;
        _source = request.Choices; _current = request.Current; _apply = request.Apply;
        TitleLabel.Text = request.Title;
        QueryBox.Text = "";
        _page = Math.Max(0, _source.ToList().FindIndex(c => c.Id == _current?.Id)) / PageSize;
        Visibility = Visibility.Visible;
        Refresh(); QueryBox.Focus();
    }
    public void Close()
    {
        Visibility = Visibility.Collapsed;
        _apply = null; _source = []; _filtered = []; ChoiceRows.ItemsSource = null;
        if (_restoreFocus != null) Keyboard.Focus(_restoreFocus);
        _restoreFocus = null;
    }
    public bool HandleKey(KeyEventArgs e)
    {
        if (!IsOpen) return false;
        if (e.Key == Key.Escape) { Close(); return true; }
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            if (e.Key == Key.F) QueryBox.Focus();
            return e.Key is Key.F or Key.S or Key.O or Key.Z or Key.Y;
        }
        return false;
    }
    private void Query_Changed(object sender, TextChangedEventArgs e) { if (IsOpen) { _page = 0; Refresh(); } }
    private void Refresh()
    {
        var query = QueryBox.Text.Trim();
        _filtered = _source.Where(c => c.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || c.Id.ToString().Contains(query)).ToArray();
        var pages = Math.Max(1, (_filtered.Length + PageSize - 1) / PageSize);
        _page = Math.Clamp(_page, 0, pages - 1);
        var visible = _filtered.Skip(_page * PageSize).Take(PageSize).ToArray();
        ChoiceRows.ItemsSource = visible;
        ChoiceRows.SelectedItem = visible.FirstOrDefault(c => c.Id == _current?.Id);
        PreviousButton.IsEnabled = _page > 0; NextButton.IsEnabled = _page < pages - 1;
        PageLabel.Text = $"{_page + 1} / {pages}"; CountLabel.Text = $"{_filtered.Length} options";
        EmptyLabel.Visibility = _filtered.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (visible.Length > 0) ChoiceRows.ScrollIntoView(visible[0]);
    }
    private void Choose(QuestChoice choice)
    {
        var apply = _apply;
        Close(); apply?.Invoke(choice);
    }
    private void Choice_Click(object sender, MouseButtonEventArgs e)
    {
        var item = ItemsControl.ContainerFromElement(ChoiceRows, e.OriginalSource as DependencyObject) as ListBoxItem;
        if (item?.DataContext is QuestChoice choice) { Choose(choice); e.Handled = true; }
    }
    private void Choice_KeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Enter && ChoiceRows.SelectedItem is QuestChoice choice) { Choose(choice); e.Handled = true; } }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Previous_Click(object sender, RoutedEventArgs e) { _page--; Refresh(); }
    private void Next_Click(object sender, RoutedEventArgs e) { _page++; Refresh(); }

    internal void VerifySmokeRender()
    {
        Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        UpdateLayout();
        if (!IsOpen || ActualWidth < 700 || ActualHeight < 300 || ChoiceRows.Items.Count == 0 || ChoiceRows.Items.Count > PageSize)
            throw new InvalidOperationException("Icon picker did not render at usable dimensions.");
        var item = ChoiceRows.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
        if (item == null || item.ActualWidth < 100 || item.ActualHeight < 100 || ((QuestChoice)item.DataContext).Icon == null)
            throw new InvalidOperationException("Icon picker tile/image did not render.");
    }
    internal void SearchSmoke(string text) => QueryBox.Text = text;
    internal int SmokeMatchCount => _filtered.Length;
    internal void ChooseSmoke(uint id) => Choose(_filtered.Single(c => c.Id == id));
}
