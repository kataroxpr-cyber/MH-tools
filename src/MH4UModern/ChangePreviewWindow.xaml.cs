using System.Collections.ObjectModel;
using System.Windows;
using MH4UModern.Models;

namespace MH4UModern;

public partial class ChangePreviewWindow : Window
{
    public ObservableCollection<ChangeRecord> Changes { get; }
    public ObservableCollection<string> Warnings { get; }

    public ChangePreviewWindow(IEnumerable<ChangeRecord> changes, IEnumerable<string> warnings)
    {
        InitializeComponent();
        Changes = new ObservableCollection<ChangeRecord>(changes);
        Warnings = new ObservableCollection<string>(warnings);
        DataContext = this;
        WarningsPanel.Visibility = Warnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
