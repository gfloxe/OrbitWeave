using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brush = System.Windows.Media.Brush;
using Image = System.Windows.Controls.Image;

namespace OrbitWeave.Ui;

// Construit les éléments libres sur une toile sans modifier les contrôles d'origine.
public static class UiCustomElements
{
    public static void AddTo(Canvas canvas, UiProfile profile, string parent, Orbit.WheelGeometry? geometry = null)
    {
        foreach (var item in profile.CustomElements.Values.Where(item => item.Parent == parent))
        {
            var element = Create(item);
            var anchor = Anchor(item.AttachTo, geometry) + profile.Layout.Attachments.GetValueOrDefault(item.Id);
            Canvas.SetLeft(element, item.X + anchor.X);
            Canvas.SetTop(element, item.Y + anchor.Y);
            canvas.Children.Add(element);
            UiOverrides.ApplyTree(element, profile);
        }
    }

    public static Orbit.WheelPoint Anchor(string id, Orbit.WheelGeometry? geometry)
    {
        if (geometry is null) return new();
        if (id == "wheel.hub") return geometry.HubCenter;
        if (id.StartsWith("wheel.section:", StringComparison.Ordinal))
            return geometry.Sections.FirstOrDefault(item => item.Name.Equals(id["wheel.section:".Length..], StringComparison.OrdinalIgnoreCase)).Center;
        return new();
    }

    public static FrameworkElement Create(UiCustomElement item)
    {
        FrameworkElement element = item.Kind switch
        {
            "Image" => CreateImage(item),
            "Shape" => new Border { Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x3E, 0xA5)),
                CornerRadius = new CornerRadius(4) },
            "Separator" => new Border { Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x3E, 0xA5)) },
            _ => CreateText(item)
        };
        element.Width = Math.Max(8, item.Width);
        element.Height = Math.Max(1, item.Height);
        element.With(item.Id);
        return element;
    }

    private static FrameworkElement CreateText(UiCustomElement item)
    {
        var text = new TextBlock { Text = item.Content, TextWrapping = TextWrapping.Wrap,
            FontSize = 16, FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI") };
        if (item.Parent == "wheel") text.Foreground = Brushes.White;
        else text.SetResourceReference(TextBlock.ForegroundProperty, "SettingsTextBrush");
        return text;
    }

    private static FrameworkElement CreateImage(UiCustomElement item)
    {
        try
        {
            if (File.Exists(item.Content))
            {
                using var file = File.OpenRead(item.Content);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = file;
                bitmap.EndInit();
                bitmap.Freeze();
                return new Image { Source = bitmap, Stretch = Stretch.Uniform };
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Le profil reste éditable même si son image a disparu.
        }
        return new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = "Image introuvable", HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray } };
    }
}
