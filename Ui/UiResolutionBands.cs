namespace OrbitWeave.Ui;

// Largeur physique du moniteur qui porte la roue, en pixels.
public static class UiResolutionBands
{
    public const string Small = "Small";
    public const string Medium = "Medium";
    public const string Large = "Large";
    public const string ExtraLarge = "ExtraLarge";

    public static string ForWidth(int width) => width switch
    {
        < 1366 => Small,
        < 1920 => Medium,
        < 2560 => Large,
        _ => ExtraLarge
    };
}
