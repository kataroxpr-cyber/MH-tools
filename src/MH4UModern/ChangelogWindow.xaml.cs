using System.Windows;

namespace MH4UModern;

public partial class ChangelogWindow : Window
{
    public ChangelogWindow(string version, IEnumerable<string> changes)
    {
        InitializeComponent();
        VersionText.Text = $"v{version}";
        ChangesList.ItemsSource = changes.ToArray();
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
