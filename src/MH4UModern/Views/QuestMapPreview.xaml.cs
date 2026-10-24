using System.Windows;
using System.Windows.Controls;
using MH4UModern.Services;
using MH4UModern.ViewModels;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MH4UModern.Views;

public partial class QuestMapPreview : UserControl
{
    public QuestMapPreview() { InitializeComponent(); Unloaded += (_, _) => Close(); }
    public void Open(FrameworkElement anchor, byte map)
    {
        MapTitle.Text = QuestPresentationService.MapName(map);
        MapImage.Source = QuestPresentationService.MapIcon(map);
        MapAreas.Text = string.Join("  ", QuestCreationChoices.AreasFor(map).Select(z => z.Name));
        MapPopup.PlacementTarget = anchor; MapPopup.IsOpen = true;
    }
    public void Close() => MapPopup.IsOpen = false;
    internal void SaveSmoke(string file)
    {
        Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        if (MapPopup.Child is not FrameworkElement root || !MapPopup.IsOpen || MapImage.Source == null) throw new InvalidOperationException("Map preview did not open with its bitmap.");
        root.UpdateLayout();
        if (root.ActualWidth < 300 || root.ActualHeight < 300) throw new InvalidOperationException("Map preview is too small.");
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(file); encoder.Save(stream);
    }
}
