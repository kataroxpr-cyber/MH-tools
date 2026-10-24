using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MH4UModern;

public partial class ColorPickerWindow : Window
{
    private double _h;
    private double _s;
    private double _v;
    private bool _dragSpectrum;
    private bool _dragHue;

    public Color SelectedColor { get; private set; }

    public ColorPickerWindow(string label, Color initialColor)
    {
        InitializeComponent();
        PickerTitle.Text = $"Pick {label}";
        SelectedColor = initialColor;
        (_h, _s, _v) = RgbToHsv(initialColor);
        BuildPalette();
        Loaded += (_, _) => UpdateUi();
    }

    private void BuildPalette()
    {
        PalettePanel.Children.Clear();

        // Neutral row first, matching the familiar palette layout from graphics editors.
        for (var i = 0; i < 10; i++)
        {
            var value = 1.0 - i / 9.0;
            AddSwatch(HsvToColor(0, 0, value));
        }

        // Twelve hue families with ten progressively richer/deeper swatches each.
        for (var row = 0; row < 12; row++)
        {
            var hue = row * 30.0;
            for (var col = 0; col < 10; col++)
            {
                var saturation = 0.18 + 0.82 * (col / 9.0);
                var value = 1.0 - 0.42 * (col / 9.0);
                AddSwatch(HsvToColor(hue, saturation, value));
            }
        }
    }

    private void AddSwatch(Color color)
    {
        var swatch = new Border
        {
            Width = 16,
            Height = 16,
            Margin = new Thickness(1.5),
            CornerRadius = new CornerRadius(2),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)),
            Background = new SolidColorBrush(color),
            Tag = color,
            Cursor = Cursors.Hand,
            ToolTip = $"#{color.R:X2}{color.G:X2}{color.B:X2}"
        };
        swatch.MouseLeftButtonDown += PaletteSwatch_MouseLeftButtonDown;
        PalettePanel.Children.Add(swatch);
    }

    private void PaletteSwatch_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: Color color }) return;
        SelectedColor = color;
        (_h, _s, _v) = RgbToHsv(color);
        UpdateUi();
        e.Handled = true;
    }

    private void Spectrum_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragSpectrum = true;
        SpectrumCanvas.CaptureMouse();
        UpdateSpectrumFromPoint(e.GetPosition(SpectrumCanvas));
    }

    private void Spectrum_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragSpectrum)
            UpdateSpectrumFromPoint(e.GetPosition(SpectrumCanvas));
    }

    private void Spectrum_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragSpectrum) return;
        UpdateSpectrumFromPoint(e.GetPosition(SpectrumCanvas));
        _dragSpectrum = false;
        SpectrumCanvas.ReleaseMouseCapture();
    }

    private void Hue_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragHue = true;
        HueCanvas.CaptureMouse();
        UpdateHueFromPoint(e.GetPosition(HueCanvas));
    }

    private void Hue_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragHue)
            UpdateHueFromPoint(e.GetPosition(HueCanvas));
    }

    private void Hue_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragHue) return;
        UpdateHueFromPoint(e.GetPosition(HueCanvas));
        _dragHue = false;
        HueCanvas.ReleaseMouseCapture();
    }

    private void UpdateSpectrumFromPoint(Point p)
    {
        if (SpectrumCanvas.ActualWidth <= 0 || SpectrumCanvas.ActualHeight <= 0) return;
        _s = Math.Clamp(p.X / SpectrumCanvas.ActualWidth, 0, 1);
        _v = 1.0 - Math.Clamp(p.Y / SpectrumCanvas.ActualHeight, 0, 1);
        SelectedColor = HsvToColor(_h, _s, _v);
        UpdateUi();
    }

    private void UpdateHueFromPoint(Point p)
    {
        if (HueCanvas.ActualWidth <= 0) return;
        _h = Math.Clamp(p.X / HueCanvas.ActualWidth, 0, 1) * 360.0;
        if (_h >= 360.0) _h = 359.999;
        SelectedColor = HsvToColor(_h, _s, _v);
        UpdateUi();
    }

    private void PickerSurface_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateMarkers();

    private void UpdateUi()
    {
        var hueColor = HsvToColor(_h, 1, 1);
        SpectrumBase.Background = new SolidColorBrush(hueColor);
        SelectedPreview.Background = new SolidColorBrush(SelectedColor);
        HexText.Text = $"#{SelectedColor.R:X2}{SelectedColor.G:X2}{SelectedColor.B:X2}";
        RgbText.Text = $"RGB {SelectedColor.R}, {SelectedColor.G}, {SelectedColor.B}";
        UpdateMarkers();
    }

    private void UpdateMarkers()
    {
        if (SpectrumCanvas.ActualWidth > 0 && SpectrumCanvas.ActualHeight > 0)
        {
            var left = _s * SpectrumCanvas.ActualWidth - SpectrumMarker.Width / 2;
            var top = (1.0 - _v) * SpectrumCanvas.ActualHeight - SpectrumMarker.Height / 2;
            Canvas.SetLeft(SpectrumMarker, left);
            Canvas.SetTop(SpectrumMarker, top);
        }

        if (HueCanvas.ActualWidth > 0)
        {
            var left = (_h / 360.0) * HueCanvas.ActualWidth - HueMarker.Width / 2;
            Canvas.SetLeft(HueMarker, left);
            Canvas.SetTop(HueMarker, 0);
        }
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            DialogResult = true;
            Close();
        }
    }

    private static (double H, double S, double V) RgbToHsv(Color color)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        double h;
        if (delta < 0.000001) h = 0;
        else if (Math.Abs(max - r) < 0.000001) h = 60 * (((g - b) / delta) % 6);
        else if (Math.Abs(max - g) < 0.000001) h = 60 * (((b - r) / delta) + 2);
        else h = 60 * (((r - g) / delta) + 4);
        if (h < 0) h += 360;

        var s = max <= 0 ? 0 : delta / max;
        return (h, s, max);
    }

    private static Color HsvToColor(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        s = Math.Clamp(s, 0, 1);
        v = Math.Clamp(v, 0, 1);
        var c = v * s;
        var x = c * (1 - Math.Abs((h / 60.0 % 2) - 1));
        var m = v - c;

        double r1, g1, b1;
        if (h < 60) (r1, g1, b1) = (c, x, 0);
        else if (h < 120) (r1, g1, b1) = (x, c, 0);
        else if (h < 180) (r1, g1, b1) = (0, c, x);
        else if (h < 240) (r1, g1, b1) = (0, x, c);
        else if (h < 300) (r1, g1, b1) = (x, 0, c);
        else (r1, g1, b1) = (c, 0, x);

        return Color.FromRgb(
            (byte)Math.Clamp((int)Math.Round((r1 + m) * 255), 0, 255),
            (byte)Math.Clamp((int)Math.Round((g1 + m) * 255), 0, 255),
            (byte)Math.Clamp((int)Math.Round((b1 + m) * 255), 0, 255));
    }
}
