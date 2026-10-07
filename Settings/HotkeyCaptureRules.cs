namespace OrbitWeave.Settings;

// Raccourci de la roue : toute touche, seule ou avec Ctrl / Alt / Maj / Win. Seules les touches de modification
// seules et Échap seul (il annule la saisie) sont refusées ; Windows peut encore refuser une combinaison qu'il réserve.
public static class HotkeyCaptureRules
{
    private static readonly HashSet<string> ModifierKeys =
        ["LeftCtrl", "RightCtrl", "LeftAlt", "RightAlt", "LeftShift", "RightShift", "LWin", "RWin", "System", "None"];

    public static bool IsUsable(bool control, bool alt, bool shift, bool win, string key) =>
        !string.IsNullOrWhiteSpace(key) && !ModifierKeys.Contains(key) &&
        !(key == "Escape" && !control && !alt && !shift && !win);

    // Touche seule qui sert à écrire : elle ne s'écrira plus ailleurs, elle ouvrira la roue.
    public static bool BlocksTyping(bool control, bool alt, bool shift, bool win, string key) =>
        !control && !alt && !win && !IsFunctionKey(key) && key is not ("Pause" or "Scroll" or "Snapshot" or "Apps") &&
        !key.StartsWith("Media", StringComparison.Ordinal) && !key.StartsWith("Browser", StringComparison.Ordinal) &&
        !key.StartsWith("Volume", StringComparison.Ordinal) && !key.StartsWith("Launch", StringComparison.Ordinal);

    private static bool IsFunctionKey(string key) =>
        key.Length > 1 && key[0] == 'F' && int.TryParse(key.AsSpan(1), out var number) && number is >= 1 and <= 24;

    public static string Display(HotkeySettings settings) => string.Join(" + ",
        new[] { settings.Control ? "Ctrl" : null, settings.Alt ? "Alt" : null, settings.Shift ? "Maj" : null,
            settings.Win ? "Win" : null, KeyName(settings.Key) }
        .Where(value => value is not null));

    public static string KeyName(string key) => key switch
    {
        "Space" => "Espace",
        "Return" or "Enter" => "Entrée",
        "Back" => "Retour arrière",
        "Delete" => "Suppr",
        "Insert" => "Inser",
        "Escape" => "Échap",
        "Tab" => "Tab",
        "Left" => "←", "Right" => "→", "Up" => "↑", "Down" => "↓",
        "Prior" or "PageUp" => "Page préc.", "Next" or "PageDown" => "Page suiv.",
        "Home" => "Début", "End" => "Fin",
        "Snapshot" => "Impr. écran",
        "Capital" => "Verr. Maj",
        _ when key.Length == 2 && key[0] == 'D' && char.IsDigit(key[1]) => key[1..],
        _ when key.StartsWith("NumPad", StringComparison.Ordinal) => "Pavé " + key["NumPad".Length..],
        _ => key
    };
}
