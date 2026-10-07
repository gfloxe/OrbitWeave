using Microsoft.Win32;

namespace OrbitWeave.Settings;

// « Lancer au démarrage de Windows » : la valeur OrbitWeave de HKCU\…\Run, comme la case de l'installateur.
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "OrbitWeave";

    public static string Command(string exe) => $"\"{exe}\"";

    // Activé seulement si la valeur lance bien cette copie (pas une ancienne copie déplacée ou supprimée).
    public static bool Launches(string? value, string exe) =>
        value is not null && value.Trim().Trim('"').Equals(exe, StringComparison.OrdinalIgnoreCase);

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return Environment.ProcessPath is { } exe && Launches(key?.GetValue(ValueName) as string, exe);
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled && Environment.ProcessPath is { } exe) key.SetValue(ValueName, Command(exe));
        else key.DeleteValue(ValueName, false);
    }
}
