using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Brush = System.Windows.Media.Brush;
using Control = System.Windows.Controls.Control;
using Panel = System.Windows.Controls.Panel;

namespace OrbitWeave.Ui;

// Applique les surcharges d'un profil à un élément identifié, de façon réversible :
// la valeur locale d'origine de chaque propriété touchée est gardée et rétablie avant chaque application.
public static class UiOverrides
{
    public static readonly string[] Properties =
    [
        "Visible", "Opacity", "OffsetX", "OffsetY", "Width", "Height", "Rotation", "Scale", "ZIndex",
        "Background", "Foreground", "BorderBrush", "BorderThickness", "CornerRadius",
        "FontSize", "FontWeight", "FontFamily", "Text", "ToolTip", "Enabled"
    ];

    private static readonly ConditionalWeakTable<FrameworkElement, Dictionary<DependencyProperty, object>> Originals = new();
    private static readonly ConditionalWeakTable<FrameworkElement, StateTracker> Trackers = new();

    private sealed class StateTracker
    {
        public bool Pressed;
        public bool Applying;
        public bool Initialized;
        public string? LastState;
    }

    public static Dictionary<string, string> Resolve(UiProfile profile, string id, string? state = null, int? screenWidth = null)
    {
        var values = new Dictionary<string, string>();
        var template = id.Split(':', 2)[0];
        if (template != id && profile.Elements.TryGetValue(template, out var shared))
            foreach (var (key, value) in shared) values[key] = value;
        if (profile.Elements.TryGetValue(id, out var own))
            foreach (var (key, value) in own) values[key] = value;
        if (state is not null)
        {
            if (profile.States.TryGetValue(template, out var sharedStates) &&
                sharedStates.TryGetValue(state, out var sharedState))
                foreach (var (key, value) in sharedState) values[key] = value;
            if (profile.States.TryGetValue(id, out var ownStates) &&
                ownStates.TryGetValue(state, out var ownState))
                foreach (var (key, value) in ownState) values[key] = value;
        }
        var band = UiResolutionBands.ForWidth(screenWidth ?? UiRuntime.ActiveScreenWidth);
        if (profile.Resolutions.TryGetValue(band, out var elements))
        {
            if (template != id && elements.TryGetValue(template, out var sharedResolution))
                foreach (var (key, value) in sharedResolution) values[key] = value;
            if (elements.TryGetValue(id, out var ownResolution))
                foreach (var (key, value) in ownResolution) values[key] = value;
        }
        if ((id.StartsWith("wheel.label.title:", StringComparison.Ordinal) || id.StartsWith("wheel.label.subtitle:", StringComparison.Ordinal)) &&
            profile.Layout.Attachments.TryGetValue(id, out var attachment))
        {
            values["OffsetX"] = ((Number(values.GetValueOrDefault("OffsetX") ?? "0") ?? 0) + attachment.X).ToString(CultureInfo.InvariantCulture);
            values["OffsetY"] = ((Number(values.GetValueOrDefault("OffsetY") ?? "0") ?? 0) + attachment.Y).ToString(CultureInfo.InvariantCulture);
        }
        return values;
    }

    private static bool HasStates(UiProfile profile, string id) =>
        profile.States.ContainsKey(id) || profile.States.ContainsKey(id.Split(':', 2)[0]);

    private static string? CurrentState(FrameworkElement element, StateTracker? tracker) =>
        !element.IsEnabled ? "Disabled" : tracker?.Pressed == true ? "Pressed" : element.IsMouseOver ? "Hover" : null;

    private static StateTracker Track(FrameworkElement element)
    {
        if (Trackers.TryGetValue(element, out var existing)) return existing;
        var tracker = new StateTracker();
        Trackers.Add(element, tracker);
        void Refresh()
        {
            if (tracker.Applying || UiId.Get(element) is not { } id ||
                !(HasStates(UiRuntime.Profile, id) || UiMotion.HasAnimation(UiRuntime.Profile, id))) return;
            Apply(element, UiRuntime.Profile);
        }
        element.MouseEnter += (_, _) => Refresh();
        element.MouseLeave += (_, _) => { tracker.Pressed = false; Refresh(); };
        element.PreviewMouseLeftButtonDown += (_, _) => { tracker.Pressed = true; Refresh(); };
        element.PreviewMouseLeftButtonUp += (_, _) => { tracker.Pressed = false; Refresh(); };
        element.IsEnabledChanged += (_, _) => Refresh();
        return tracker;
    }

    public static bool Supports(FrameworkElement element, string property) => property switch
    {
        "Visible" or "Opacity" or "OffsetX" or "OffsetY" or "Rotation" or "Scale" or "ZIndex" or "ToolTip" or "Enabled" => true,
        _ => Target(element, property) is not null
    };

    // Valeur actuellement affichée, au format d'une surcharge (sert d'indication dans l'éditeur).
    public static string? Describe(FrameworkElement element, string property)
    {
        object? value = property switch
        {
            "Visible" => element.Visibility == Visibility.Visible,
            "Opacity" => Math.Round(element.Opacity, 2),
            "OffsetX" or "OffsetY" or "Rotation" => 0.0,
            "Scale" => 1.0,
            "ZIndex" => Panel.GetZIndex(element),
            "Enabled" => element.IsEnabled,
            "ToolTip" => element.ToolTip as string,
            "Width" => element.ActualWidth > 0 ? Math.Round(element.ActualWidth, 1) : element.Width,
            "Height" => element.ActualHeight > 0 ? Math.Round(element.ActualHeight, 1) : element.Height,
            _ => Target(element, property) is { } dp ? element.GetValue(dp) : null
        };
        return value switch
        {
            null => null,
            bool flag => flag ? "true" : "false",
            double number when double.IsNaN(number) => null,
            double number => number.ToString(CultureInfo.InvariantCulture),
            SolidColorBrush brush => UiTokens.Format(brush),
            Brush => "(dégradé)",
            Thickness thickness => thickness.Left == thickness.Top && thickness.Top == thickness.Right && thickness.Right == thickness.Bottom
                ? thickness.Left.ToString(CultureInfo.InvariantCulture) : new ThicknessConverter().ConvertToInvariantString(thickness),
            CornerRadius radius => radius.TopLeft == radius.BottomRight && radius.TopLeft == radius.TopRight && radius.TopLeft == radius.BottomLeft
                ? radius.TopLeft.ToString(CultureInfo.InvariantCulture) : new CornerRadiusConverter().ConvertToInvariantString(radius),
            FontWeight weight => weight.ToString(),
            FontFamily family => family.Source,
            string or int => Convert.ToString(value, CultureInfo.InvariantCulture),
            _ => null
        };
    }

    // Opacité visée par les animations d'apparition.
    public static double Opacity(FrameworkElement element, UiProfile profile)
    {
        if (UiId.Get(element) is not { } id) return 1;
        if (id == "wheel.hub" && Resolve(profile, id).GetValueOrDefault("Visible") == "false") return 0.01;
        if (UiId.IsProtected(element)) return 1;
        Trackers.TryGetValue(element, out var tracker);
        return Resolve(profile, id, CurrentState(element, tracker)).TryGetValue("Opacity", out var text) && Number(text) is { } value
            ? Math.Clamp(value, 0, 1) : 1;
    }

    public static void ApplyTree(DependencyObject root, UiProfile profile)
    {
        if (root is FrameworkElement element) Apply(element, profile);
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            ApplyTree(child, profile);
    }

    public static void Apply(FrameworkElement element, UiProfile profile)
    {
        if (UiId.Get(element) is not { } id) return;
        var tracker = HasStates(profile, id) || UiMotion.HasAnimation(profile, id) ? Track(element) : null;
        if (tracker?.Applying == true) return;
        if (tracker is not null) tracker.Applying = true;
        try
        {
            var state = CurrentState(element, tracker);
            var animate = tracker?.Initialized == true && tracker.LastState != state && UiMotion.HasAnimation(profile, id);
            var fromOpacity = element.Opacity;
            UiMotion.Clear(element);
            ApplyValues(element, profile, id, tracker);
            if (tracker is not null) { tracker.LastState = state; tracker.Initialized = true; }
            if (animate) UiMotion.Play(element, profile, id, fromOpacity);
        }
        finally { if (tracker is not null) tracker.Applying = false; }
    }

    private static void ApplyValues(FrameworkElement element, UiProfile profile, string id, StateTracker? tracker)
    {
        var values = Resolve(profile, id, CurrentState(element, tracker));
        if (UiId.IsProtected(element))
        {
            if (id == "wheel.hub" && values.GetValueOrDefault("Visible") == "false")
            {
                values.Remove("Visible");
                values["Opacity"] = "0.01"; // Le centre masqué garde une zone cliquable de secours.
            }
            else { values.Remove("Visible"); values.Remove("Opacity"); }
        }
        if (Originals.TryGetValue(element, out var previous))
        {
            foreach (var (dp, original) in previous) Restore(element, dp, original);
            previous.Clear();
        }
        if (values.Count == 0) return;
        var originals = Originals.GetOrCreateValue(element);

        foreach (var (property, text) in values)
        {
            if (property is "OffsetX" or "OffsetY" or "Rotation" or "Scale") continue;
            var dp = property switch
            {
                "Visible" => UIElement.VisibilityProperty,
                "Opacity" => UIElement.OpacityProperty,
                "ZIndex" => Panel.ZIndexProperty,
                "Enabled" => UIElement.IsEnabledProperty,
                "ToolTip" => FrameworkElement.ToolTipProperty,
                _ => Target(element, property)
            };
            if (dp is null || Parse(property, dp, text) is not { } value) continue;
            Remember(element, originals, dp);
            element.SetValue(dp, value);
        }
        ApplyGeometry(element, values, originals);
    }

    private static void ApplyGeometry(FrameworkElement element, Dictionary<string, string> values, Dictionary<DependencyProperty, object> originals)
    {
        var x = values.TryGetValue("OffsetX", out var ox) ? Number(ox) ?? 0 : 0;
        var y = values.TryGetValue("OffsetY", out var oy) ? Number(oy) ?? 0 : 0;
        var angle = values.TryGetValue("Rotation", out var r) ? Number(r) ?? 0 : 0;
        var scale = values.TryGetValue("Scale", out var s) ? Number(s) is > 0 and var positive ? positive : 1 : 1;
        // Dans la roue, Canvas.Left/Top placent l'élément et RenderTransform porte les animations d'ouverture.
        var placed = !double.IsNaN(Canvas.GetLeft(element)) || !double.IsNaN(Canvas.GetTop(element));
        if (placed && (x != 0 || y != 0))
        {
            Remember(element, originals, Canvas.LeftProperty);
            Remember(element, originals, Canvas.TopProperty);
            if (!double.IsNaN(Canvas.GetLeft(element))) Canvas.SetLeft(element, Canvas.GetLeft(element) + x);
            if (!double.IsNaN(Canvas.GetTop(element))) Canvas.SetTop(element, Canvas.GetTop(element) + y);
        }
        var translate = !placed && (x != 0 || y != 0);
        if (angle == 0 && scale == 1 && !translate) return;
        var group = new TransformGroup();
        if (scale != 1) group.Children.Add(new ScaleTransform(scale, scale));
        if (angle != 0) group.Children.Add(new RotateTransform(angle));
        if (translate) group.Children.Add(new TranslateTransform(x, y));
        if (placed)
        {
            Remember(element, originals, FrameworkElement.LayoutTransformProperty);
            element.LayoutTransform = group;
            return;
        }
        Remember(element, originals, UIElement.RenderTransformProperty);
        Remember(element, originals, UIElement.RenderTransformOriginProperty);
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = group;
    }

    private static DependencyProperty? Target(FrameworkElement element, string property) => (property, element) switch
    {
        ("Width", _) => FrameworkElement.WidthProperty,
        ("Height", _) => FrameworkElement.HeightProperty,
        ("Background", Control) => Control.BackgroundProperty,
        ("Background", Border) => Border.BackgroundProperty,
        ("Background", Panel) => Panel.BackgroundProperty,
        ("Background", Shape) => Shape.FillProperty,
        ("Foreground", Control) => Control.ForegroundProperty,
        ("Foreground", TextBlock) => TextBlock.ForegroundProperty,
        ("BorderBrush", Control) => Control.BorderBrushProperty,
        ("BorderBrush", Border) => Border.BorderBrushProperty,
        ("BorderBrush", Shape) => Shape.StrokeProperty,
        ("BorderThickness", Control) => Control.BorderThicknessProperty,
        ("BorderThickness", Border) => Border.BorderThicknessProperty,
        ("BorderThickness", Shape) => Shape.StrokeThicknessProperty,
        ("CornerRadius", Border) => Border.CornerRadiusProperty,
        ("FontSize", Control) => Control.FontSizeProperty,
        ("FontSize", TextBlock) => TextBlock.FontSizeProperty,
        ("FontWeight", Control) => Control.FontWeightProperty,
        ("FontWeight", TextBlock) => TextBlock.FontWeightProperty,
        ("FontFamily", Control) => Control.FontFamilyProperty,
        ("FontFamily", TextBlock) => TextBlock.FontFamilyProperty,
        ("Text", TextBlock) => TextBlock.TextProperty,
        ("Text", HeaderedItemsControl) => HeaderedItemsControl.HeaderProperty,
        ("Text", ContentControl { Content: string or null }) => ContentControl.ContentProperty,
        _ => null
    };

    private static object? Parse(string property, DependencyProperty dp, string text)
    {
        var type = dp.PropertyType;
        try
        {
            if (property == "Visible") return bool.TryParse(text, out var visible) ? (visible ? Visibility.Visible : Visibility.Collapsed) : null;
            if (property == "Opacity") return Number(text) is { } opacity ? Math.Clamp(opacity, 0, 1) : null;
            if (type == typeof(bool)) return bool.TryParse(text, out var flag) ? flag : null;
            if (type == typeof(double)) return Number(text) is >= 0 and var number ? number : null;
            if (type == typeof(int)) return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer) ? integer : null;
            if (type == typeof(Brush)) return UiTokens.ParseColor(text) is { } color ? new SolidColorBrush(color) : null;
            if (type == typeof(string) || type == typeof(object)) return text;
            var converter = TypeDescriptor.GetConverter(type);
            return converter.CanConvertFrom(typeof(string)) ? converter.ConvertFromInvariantString(text) : null;
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or ArgumentException or InvalidOperationException) { return null; }
    }

    private static double? Number(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : null;

    private static void Remember(FrameworkElement element, Dictionary<DependencyProperty, object> originals, DependencyProperty dp)
    {
        if (!originals.ContainsKey(dp)) originals[dp] = element.ReadLocalValue(dp);
    }

    private static void Restore(FrameworkElement element, DependencyProperty dp, object original)
    {
        if (original == DependencyProperty.UnsetValue) element.ClearValue(dp);
        else element.SetValue(dp, original);
    }
}
