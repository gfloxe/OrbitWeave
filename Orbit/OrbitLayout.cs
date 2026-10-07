using System.Windows;

namespace OrbitWeave.Orbit;

internal static class OrbitLayout
{
    public static readonly Point Center = new(560, 560);
    public const double CanvasSize = 1120;
    public const double ViewportWidth = 1060;
    public const double ViewportHeight = 880;
    public const double ViewportOffsetX = (CanvasSize - ViewportWidth) / 2;
    public const double ViewportOffsetY = (CanvasSize - ViewportHeight) / 2;
    public static double HubDiameter => (double)System.Windows.Application.Current.FindResource("HubDiameter");
    public static double HubHaloDiameter => (double)System.Windows.Application.Current.FindResource("HubHaloDiameter");
    public static double SectionDiameter => (double)System.Windows.Application.Current.FindResource("SectionDiameter");

    // Les sections alternent entre deux orbites. Avec un nombre impair, la dernière et la
    // première tomberaient côte à côte sur l'orbite intérieure : la dernière passe à l'extérieur.
    public static double RadiusFor(int index, int count)
    {
        var outer = index % 2 == 1 || (count > 1 && count % 2 == 1 && index == count - 1);
        return (double)System.Windows.Application.Current.FindResource(outer ? "OuterOrbitRadius" : "InnerOrbitRadius");
    }

    public static double AngleFor(int index, int count) =>
        -Math.PI / 2 + index * Math.PI * 2 / Math.Max(1, count);

    public static Point NodeCenter(int index, int count)
    {
        var angle = AngleFor(index, count);
        var radius = RadiusFor(index, count);
        return new Point(Center.X + Math.Cos(angle) * radius,
            Center.Y + Math.Sin(angle) * radius);
    }

    public static Point OnRay(double angle, double radius) =>
        new(Center.X + Math.Cos(angle) * radius,
            Center.Y + Math.Sin(angle) * radius);
}
