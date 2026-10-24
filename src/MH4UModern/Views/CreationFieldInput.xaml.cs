using System.Windows;
using System.Windows.Controls;
using MH4UModern.ViewModels;

namespace MH4UModern.Views;

public partial class CreationFieldInput : UserControl
{
    public CreationFieldInput() => InitializeComponent();
    public event EventHandler? ChoiceOpened;
    public event EventHandler? ChoiceClosed;
    private void Choice_Opened(object sender, EventArgs e) => ChoiceOpened?.Invoke(this, e);
    private void Choice_Closed(object sender, EventArgs e) => ChoiceClosed?.Invoke(this, e);
    private void Decrease_Click(object sender, RoutedEventArgs e) => (DataContext as CreationNumber)?.Step(-1);
    private void Increase_Click(object sender, RoutedEventArgs e) => (DataContext as CreationNumber)?.Step(1);
    internal void ChooseSmoke(uint id)
    {
        if (DataContext is not CreationNumber number || ChoiceInput.Visibility != Visibility.Visible || ChoiceInput.ActualWidth < 100 || ChoiceInput.Items.Count != number.Choices.Count)
            throw new InvalidOperationException("Named choice control did not render its options.");
        ChoiceInput.SelectedItem = number.Choices.Single(c => c.Id == id);
        if (number.SelectedChoice?.Id != id) throw new InvalidOperationException("Named choice control did not update its source.");
    }
}
