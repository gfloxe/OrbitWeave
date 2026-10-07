using System.Windows;

namespace OrbitWeave.Ui;

// Couches de ressources de l'application : Tokens.xaml < thème < profil d'interface.
public static class UiRuntime
{
    public static ResourceDictionary ThemeLayer { get; } = new();
    public static ResourceDictionary TokenLayer { get; } = new();
    public static UiProfile Profile { get; private set; } = new();
    public static string Theme { get; private set; } = "";
    public static int ActiveScreenWidth { get; private set; } = 1920;
    public static event Action? Changed;

    public static void Install(ResourceDictionary resources)
    {
        if (!resources.MergedDictionaries.Contains(ThemeLayer)) resources.MergedDictionaries.Add(ThemeLayer);
        if (!resources.MergedDictionaries.Contains(TokenLayer)) resources.MergedDictionaries.Add(TokenLayer);
    }

    public static void SetTheme(string theme)
    {
        Theme = theme;
        Reapply();
    }

    public static void SetProfile(UiProfile profile, bool notify = true)
    {
        Profile = profile;
        if (notify) Reapply();
        if (notify) Changed?.Invoke();
    }

    public static void SetScreenWidth(int width)
    {
        if (width <= 0 || ActiveScreenWidth == width) return;
        ActiveScreenWidth = width;
        Changed?.Invoke();
    }

    private static void Reapply()
    {
        if (System.Windows.Application.Current is { } app) UiTokens.Apply(app.Resources, TokenLayer, Profile, Theme);
    }
}
