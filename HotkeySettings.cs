using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace OrbitWeave;

public sealed class HotkeySettings
{
    public int Version { get; set; } = 2;
    public bool Control { get; set; } = true;
    public bool Alt { get; set; } = true;
    public bool Shift { get; set; }
    public bool Win { get; set; }
    public string Key { get; set; } = "Space";
    public string Theme { get; set; } = "Graphite";
    public string Animation { get; set; } = "Normal";
    public bool HoldSelect { get; set; } = true;
    public string Idle { get; set; } = "Visible";
    public string Position { get; set; } = "Centre";
    public string Language { get; set; } = "Comme Windows";
    public string SettingsTheme { get; set; } = "Clair";
    public string OpenBehavior { get; set; } = "Au clic";
    public string SectionBehavior { get; set; } = "Au survol";
    public string InterfaceProfile { get; set; } = Ui.UiProfileStore.OriginName;
    // Curseurs de la petite fenêtre. Absents d'un ancien fichier : déduits des trois choix (Animation, Idle),
    // que la grande fenêtre écrit encore ; un curseur y remet le choix le plus proche.
    public double? AnimationScale { get; set; }
    public double? IdleOpacity { get; set; }
    // Opacité des fonds de verre (sections, cartes, étiquettes) : 1 = le thème tel quel.
    public double GlassOpacity { get; set; } = 1;
    // Délai avant qu'une branche ouverte se replie quand la souris a quitté la roue.
    public double BranchCloseSeconds { get; set; } = DefaultBranchCloseSeconds;

    public const double MinAnimationScale = 0.4, MaxAnimationScale = 2.5;
    public const double MinIdleOpacity = 0.1, MinGlassOpacity = 0.4, MaxGlassOpacity = 1.4;
    public const double MinBranchCloseSeconds = 0.5, MaxBranchCloseSeconds = 20, DefaultBranchCloseSeconds = 4;

    public static double PresetAnimationScale(string animation) => animation switch { "Rapide" => 0.6, "Lent" => 1.55, _ => 1 };
    public static double PresetIdleOpacity(string idle) => idle switch { "SemiTransparent" => 0.5, "PresqueInvisible" => 0.15, _ => 1 };

    public void SetAnimationScale(double scale)
    {
        AnimationScale = Math.Clamp(scale, MinAnimationScale, MaxAnimationScale);
        Animation = Nearest(AnimationScale.Value, ["Rapide", "Normal", "Lent"], PresetAnimationScale);
    }

    public void SetIdleOpacity(double opacity)
    {
        IdleOpacity = Math.Clamp(opacity, MinIdleOpacity, 1);
        Idle = Nearest(IdleOpacity.Value, ["Visible", "SemiTransparent", "PresqueInvisible"], PresetIdleOpacity);
    }

    public void SetGlassOpacity(double opacity) => GlassOpacity = Math.Clamp(opacity, MinGlassOpacity, MaxGlassOpacity);
    public void SetBranchCloseSeconds(double seconds) => BranchCloseSeconds = Math.Clamp(Math.Round(seconds * 2) / 2, MinBranchCloseSeconds, MaxBranchCloseSeconds);

    private static string Nearest(double value, string[] names, Func<string, double> preset) =>
        names.OrderBy(name => Math.Abs(preset(name) - value)).First();

    public HotkeySettings Copy() => JsonSerializer.Deserialize<HotkeySettings>(JsonSerializer.Serialize(this))!;

    public static string FilePath => Path.Combine(ActionStore.DirectoryPath, "settings.json");
    public static HotkeySettings Defaults() => new() { Theme = "Verre liquide", SettingsTheme = "Comme Windows" };

    public void Normalize()
    {
        if (Theme is "Graphite" or "Bleu" or "Ambre") Theme = "Verre";
        if (Idle == "Semi-transparente") Idle = "SemiTransparent";
        if (Idle == "Presque invisible") Idle = "PresqueInvisible";
        if (Animation is not ("Lent" or "Normal" or "Rapide")) Animation = "Normal";
        if (OpenBehavior is not ("Au clic" or "Au survol")) OpenBehavior = "Au clic";
        if (SectionBehavior is not ("Au clic" or "Au survol")) SectionBehavior = "Au survol";
        if (SettingsTheme is not ("Clair" or "Sombre" or "Comme Windows")) SettingsTheme = "Comme Windows";
        AnimationScale = Math.Clamp(AnimationScale is { } scale && double.IsFinite(scale) ? scale : PresetAnimationScale(Animation), MinAnimationScale, MaxAnimationScale);
        IdleOpacity = Math.Clamp(IdleOpacity is { } idle && double.IsFinite(idle) ? idle : PresetIdleOpacity(Idle), MinIdleOpacity, 1);
        GlassOpacity = Math.Clamp(double.IsFinite(GlassOpacity) ? GlassOpacity : 1, MinGlassOpacity, MaxGlassOpacity);
        BranchCloseSeconds = Math.Clamp(double.IsFinite(BranchCloseSeconds) ? BranchCloseSeconds : DefaultBranchCloseSeconds, MinBranchCloseSeconds, MaxBranchCloseSeconds);
        Version = 2;
    }

    public void ResetWheel(HotkeySettings defaults)
    {
        Theme = defaults.Theme; Animation = defaults.Animation; Idle = defaults.Idle;
        AnimationScale = PresetAnimationScale(Animation); IdleOpacity = PresetIdleOpacity(Idle); GlassOpacity = defaults.GlassOpacity;
        BranchCloseSeconds = defaults.BranchCloseSeconds;
        OpenBehavior = defaults.OpenBehavior; SectionBehavior = defaults.SectionBehavior;
        HoldSelect = defaults.HoldSelect;
    }

    public void ResetKeyboardAndWindow(HotkeySettings defaults)
    {
        Control = defaults.Control; Alt = defaults.Alt; Shift = defaults.Shift; Win = defaults.Win;
        Key = defaults.Key; SettingsTheme = defaults.SettingsTheme;
    }

    public static HotkeySettings Load() => Load(FilePath);

    public static HotkeySettings Load(string path)
    {
        HotkeySettings settings;
        try { settings = JsonSerializer.Deserialize<HotkeySettings>(File.ReadAllText(path)) ?? Defaults(); }
        catch (Exception ex)
        {
            settings = Defaults();
            // Un fichier illisible est mis de côté avant que la prochaine sauvegarde ne l'écrase.
            if (ex is not (FileNotFoundException or DirectoryNotFoundException))
                try { File.Copy(path, Path.Combine(Path.GetDirectoryName(path)!, $"settings.broken-{DateTime.Now:yyyyMMdd-HHmmss}.json"), true); }
                catch (Exception copyError) { CrashLog.Write("HotkeySettings.Load", copyError); }
        }
        settings.Normalize();
        settings.ApplyThemeBrush();
        return settings;
    }

    public void Save() => Save(FilePath);

    // Écriture dans un fichier temporaire puis remplacement : une coupure ne laisse jamais un fichier tronqué.
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, true);
        ApplyThemeBrush();
    }

    private void ApplyThemeBrush()
    {
        if (System.Windows.Application.Current == null) return;
        var color = Theme switch
        {
            "Clair" => Color.FromRgb(218, 218, 218),
            "Sombre" => Color.FromRgb(23, 23, 23),
            "Verre liquide" => Color.FromRgb(48, 48, 48),
            "Verre" => Color.FromRgb(40, 40, 40),
            "Bleu" => Color.FromRgb(14, 27, 45),
            "Ambre" => Color.FromRgb(35, 27, 20),
            _ => Color.FromRgb(40, 40, 40)
        };
        Ui.UiRuntime.ThemeLayer["GlassBaseBrush"] =
            new SolidColorBrush(Color.FromArgb(0xB3, color.R, color.G, color.B));
        Ui.UiRuntime.ThemeLayer["LabelGlassBrush"] = Theme switch
        {
            "Clair" => new SolidColorBrush(Color.FromArgb(0xD9, 230, 230, 230)),
            "Sombre" => new SolidColorBrush(Color.FromArgb(0xD9, 35, 35, 35)),
            "Verre liquide" => new SolidColorBrush(Color.FromArgb(0xB0, 52, 52, 54)),
            "Verre" => new SolidColorBrush(Color.FromArgb(0xB3, 63, 63, 63)),
            "Bleu" => new SolidColorBrush(Color.FromArgb(0xB3, 34, 52, 70)),
            "Ambre" => new SolidColorBrush(Color.FromArgb(0xB3, 67, 52, 38)),
            _ => new SolidColorBrush(Color.FromArgb(0xB3, 63, 63, 63))
        };
        Ui.UiRuntime.ThemeLayer["CardGlassBrush"] = Theme switch
        {
            "Clair" => new SolidColorBrush(Color.FromArgb(0xD9, 235, 235, 235)),
            "Sombre" => new SolidColorBrush(Color.FromArgb(0xD9, 35, 35, 35)),
            "Verre liquide" => new SolidColorBrush(Color.FromArgb(0xB8, 44, 44, 46)),
            "Verre" => new SolidColorBrush(Color.FromArgb(0xB3, 50, 50, 50)),
            "Bleu" => new SolidColorBrush(Color.FromArgb(0xB3, 40, 59, 78)),
            "Ambre" => new SolidColorBrush(Color.FromArgb(0xB3, 74, 59, 42)),
            _ => new SolidColorBrush(Color.FromArgb(0xB3, 50, 50, 50))
        };
        // Curseur Transparence : l'opacité des fonds de verre du thème, multipliée.
        foreach (var key in new[] { "GlassBaseBrush", "LabelGlassBrush", "CardGlassBrush" })
            if (Ui.UiRuntime.ThemeLayer[key] is SolidColorBrush glass)
                Ui.UiRuntime.ThemeLayer[key] = new SolidColorBrush(Color.FromArgb(
                    (byte)Math.Clamp(Math.Round(glass.Color.A * GlassOpacity), 0, 255), glass.Color.R, glass.Color.G, glass.Color.B));
        Ui.UiRuntime.ThemeLayer["WidgetPrimaryTextBrush"] =
            new SolidColorBrush(Theme == "Clair" ? Colors.Black : Colors.White);
        Ui.UiRuntime.ThemeLayer["WidgetSecondaryTextBrush"] =
            new SolidColorBrush(Theme == "Clair" ? Color.FromRgb(75, 75, 75) : Color.FromRgb(198, 198, 198));
        Ui.UiRuntime.SetTheme(Theme);
    }
}
