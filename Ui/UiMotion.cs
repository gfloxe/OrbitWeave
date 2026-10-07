using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace OrbitWeave.Ui;

// Animation facultative d'un élément lors du passage entre ses états.
public static class UiMotion
{
    private sealed class Active
    {
        public Transform? Underlying;
        public TransformGroup? Wrapper;
    }

    private static readonly ConditionalWeakTable<FrameworkElement, Active> ActiveMotions = new();

    public static Dictionary<string, string> Resolve(UiProfile profile, string id)
    {
        var result = new Dictionary<string, string>();
        var template = id.Split(':', 2)[0];
        if (profile.Animations.TryGetValue(template, out var shared))
            foreach (var (key, value) in shared) result[key] = value;
        if (profile.Animations.TryGetValue(id, out var own))
            foreach (var (key, value) in own) result[key] = value;
        return result;
    }

    public static bool HasAnimation(UiProfile profile, string id) =>
        profile.Animations.ContainsKey(id) || profile.Animations.ContainsKey(id.Split(':', 2)[0]);

    public static void Clear(FrameworkElement element)
    {
        if (!ActiveMotions.TryGetValue(element, out var active)) return;
        element.BeginAnimation(UIElement.OpacityProperty, null);
        if (active.Wrapper is not null && ReferenceEquals(element.RenderTransform, active.Wrapper))
            element.RenderTransform = active.Underlying ?? Transform.Identity;
        active.Wrapper = null;
        active.Underlying = null;
    }

    public static void Play(FrameworkElement element, UiProfile profile, string id, double fromOpacity)
    {
        var values = Resolve(profile, id);
        var type = values.GetValueOrDefault("Type");
        if (type is not ("Fade" or "Scale" or "Slide")) return;
        var duration = Number(values.GetValueOrDefault("DurationMs"), 160, 0, 2000);
        if (duration <= 0) return;
        var delay = Number(values.GetValueOrDefault("DelayMs"), 0, 0, 2000);
        var easing = values.GetValueOrDefault("Easing") switch
        {
            "Linear" => (IEasingFunction?)null,
            "EaseIn" => new CubicEase { EasingMode = EasingMode.EaseIn },
            "EaseInOut" => new CubicEase { EasingMode = EasingMode.EaseInOut },
            _ => new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var length = TimeSpan.FromMilliseconds(duration);
        var start = TimeSpan.FromMilliseconds(delay);
        if (type == "Fade")
        {
            var target = element.Opacity;
            var source = Math.Abs(fromOpacity - target) > 0.01 ? fromOpacity : Math.Max(0, target * 0.72);
            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(source, target, length)
            { BeginTime = start, EasingFunction = easing, FillBehavior = FillBehavior.Stop });
            return;
        }

        var active = ActiveMotions.GetOrCreateValue(element);
        active.Underlying = element.RenderTransform;
        var wrapper = new TransformGroup();
        wrapper.Children.Add(active.Underlying);
        if (type == "Scale")
        {
            var scale = new ScaleTransform(0.9, 0.9, element.ActualWidth / 2, element.ActualHeight / 2);
            wrapper.Children.Add(scale);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.9, 1, length)
            { BeginTime = start, EasingFunction = easing });
            var finish = new DoubleAnimation(0.9, 1, length)
            { BeginTime = start, EasingFunction = easing };
            finish.Completed += (_, _) => { if (ReferenceEquals(element.RenderTransform, wrapper)) Clear(element); };
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, finish);
        }
        else
        {
            var translate = new TranslateTransform(0, 10);
            wrapper.Children.Add(translate);
            var finish = new DoubleAnimation(10, 0, length)
            { BeginTime = start, EasingFunction = easing };
            finish.Completed += (_, _) => { if (ReferenceEquals(element.RenderTransform, wrapper)) Clear(element); };
            translate.BeginAnimation(TranslateTransform.YProperty, finish);
        }
        active.Wrapper = wrapper;
        element.RenderTransform = wrapper;
    }

    private static double Number(string? value, double fallback, double minimum, double maximum) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)
            ? Math.Clamp(number, minimum, maximum) : fallback;
}
