using System.Windows;

namespace OrbitWeave.Orbit;

public static class AdaptiveViewport
{
    public const double Margin = 24;

    public static Rect Fit(Rect content, Rect screen, Rect original, bool customized)
    {
        if (!customized || content.IsEmpty) return original;
        content.Inflate(Margin, Margin);
        return Rect.Intersect(content, screen);
    }

    public static Vector LimitTranslation(Rect element, Rect screen, Vector proposed)
    {
        if (element.IsEmpty || screen.IsEmpty) return new Vector();
        static double Limit(double value, double min, double max) => min <= max ? Math.Clamp(value, min, max) : 0;
        return new Vector(Limit(proposed.X, screen.Left - element.Left, screen.Right - element.Right),
            Limit(proposed.Y, screen.Top - element.Top, screen.Bottom - element.Bottom));
    }

    public static Rect KeepInside(Rect content, Rect screen)
    {
        if (content.IsEmpty) return content;
        var width = Math.Min(content.Width, screen.Width);
        var height = Math.Min(content.Height, screen.Height);
        return new Rect(Math.Clamp(content.Left, screen.Left, screen.Right - width),
            Math.Clamp(content.Top, screen.Top, screen.Bottom - height), width, height);
    }

    public static double ScaleToFit(Rect content, Rect work, double originalScale, bool customized)
    {
        if (!customized || content.IsEmpty) return originalScale;
        return Math.Min(1, Math.Min(work.Width / (content.Width + Margin * 2), work.Height / (content.Height + Margin * 2)));
    }
}
