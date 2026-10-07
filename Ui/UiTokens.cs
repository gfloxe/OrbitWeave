using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace OrbitWeave.Ui;

public static class UiTokens
{
    // Convertit le texte d'un jeton dans le type de la ressource qu'il remplace ; null si invalide.
    public static object? Convert(object baseValue, string text) => baseValue switch
    {
        double => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) ? number : null,
        SolidColorBrush => ParseColor(text) is { } color ? Frozen(new SolidColorBrush(color)) : null,
        Color => ParseColor(text),
        _ => null
    };

    public static string? Format(object value) => value switch
    {
        double number => number.ToString(CultureInfo.InvariantCulture),
        SolidColorBrush brush => brush.Color.ToString(CultureInfo.InvariantCulture),
        Color color => color.ToString(CultureInfo.InvariantCulture),
        _ => null
    };

    public static Color? ParseColor(string text)
    {
        try { return (Color)ColorConverter.ConvertFromString(text.Trim()); }
        catch (FormatException) { return null; }
        catch (NotSupportedException) { return null; }
    }

    // Réécrit la couche de jetons : valeurs du profil, puis celles du thème courant.
    // Un jeton inconnu ou une valeur invalide est ignoré.
    public static void Apply(ResourceDictionary root, ResourceDictionary layer, UiProfile profile, string theme)
    {
        layer.Clear();
        var values = new Dictionary<string, string>(profile.Tokens);
        if (profile.Themes.TryGetValue(theme, out var themed))
            foreach (var (key, text) in themed) values[key] = text;
        foreach (var (key, text) in values)
            if (root[key] is { } baseValue && Convert(baseValue, text) is { } value)
                layer[key] = value;
    }

    private static SolidColorBrush Frozen(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
