using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using OrbitWeave.Ui;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace OrbitWeave.Orbit;

internal sealed class OrbitView(Canvas canvas)
{
    private static readonly FontFamily SymbolFont = new("Segoe UI Symbol");
    private static readonly FontFamily TextFont = new("Segoe UI Variable Text, Segoe UI");

    private static double Token(string key) => (double)System.Windows.Application.Current.FindResource(key);
    private static Brush BrushToken(string key) => (Brush)System.Windows.Application.Current.FindResource(key);

    public void Clear() => canvas.Children.Clear();

    public void Add(UIElement element) => canvas.Children.Add(element);

    public Button Node(string icon, bool center)
    {
        var size = center ? OrbitLayout.HubDiameter : OrbitLayout.SectionDiameter;
        var content = center
            ? (UIElement)new Ellipse { Width = Token("HubDotDiameter"), Height = Token("HubDotDiameter"),
                Fill = BrushToken("HubDotBrush"), IsHitTestVisible = false }
            : new TextBlock { Text = icon, FontSize = Token("SectionIconSize"), Foreground = BrushToken("WidgetPrimaryTextBrush"),
                FontFamily = SymbolFont, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center };
        return new Button { Width = size, Height = size, Content = content, Opacity = 0,
            CacheMode = new BitmapCache { RenderAtScale = 1 },
            Style = (Style)System.Windows.Application.Current.FindResource("OrbitNode") };
    }

    public Border Label(string name, int count)
    {
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = name, FontSize = Token("LabelTitleSize"), FontWeight = FontWeights.SemiBold,
            Foreground = BrushToken("WidgetPrimaryTextBrush"), FontFamily = TextFont, TextAlignment = TextAlignment.Center,
            MaxWidth = Token("LabelMaxWidth"), TextTrimming = TextTrimming.CharacterEllipsis }.With($"wheel.label.title:{name}"));
        text.Children.Add(new TextBlock { Text = count == 1 ? "1 point" : $"{count} points",
            FontSize = Token("LabelSubtitleSize"), Foreground = BrushToken("WidgetSecondaryTextBrush"),
            FontFamily = TextFont, TextAlignment = TextAlignment.Center }.With($"wheel.label.subtitle:{name}"));
        var label = new Border { Child = text, Opacity = 0,
            Style = (Style)System.Windows.Application.Current.FindResource("OrbitLabel") }.With($"wheel.label:{name}");
        return label;
    }

    public Ellipse Halo() => new()
    {
        Width = OrbitLayout.HubHaloDiameter,
        Height = OrbitLayout.HubHaloDiameter,
        Stroke = BrushToken("WidgetHaloBrush"),
        StrokeThickness = Token("HaloThickness"),
        IsHitTestVisible = false
    };

    public Line Spoke(Point from, Point to) => new()
    {
        X1 = from.X, Y1 = from.Y,
        X2 = to.X, Y2 = to.Y,
        Stroke = BrushToken("WidgetSpokeBrush"),
        StrokeThickness = Token("SpokeThickness"), IsHitTestVisible = false
    };
}
