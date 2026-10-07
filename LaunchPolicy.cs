namespace OrbitWeave;

public static class LaunchPolicy
{
    // Copie posée par l'installateur : son désinstalleur est à côté. C'est elle qui se met à jour toute seule.
    public static bool IsInstalledCopy(string? exe) =>
        !string.IsNullOrWhiteSpace(exe) && Path.GetFileName(exe).Equals("OrbitWeave.exe", StringComparison.OrdinalIgnoreCase) &&
        File.Exists(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exe))!, "unins000.exe"));

    public static bool IsNormalExecutable(string? exe)
    {
        if (string.IsNullOrWhiteSpace(exe) || !Path.GetFileName(exe).Equals("OrbitWeave.exe", StringComparison.OrdinalIgnoreCase)) return false;
        if (IsInstalledCopy(exe)) return true;
        var folder = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(exe))!);
        return folder.Name.Equals("net10.0-windows", StringComparison.OrdinalIgnoreCase) &&
            folder.Parent?.Name.Equals("Release", StringComparison.OrdinalIgnoreCase) == true &&
            folder.Parent.Parent?.Name.Equals("bin", StringComparison.OrdinalIgnoreCase) == true;
    }

    public static bool CanLaunch(string? exe, bool diagnostic, string? dataDirectory)
    {
        if (!diagnostic) return IsNormalExecutable(exe) && dataDirectory is null;
        if (string.IsNullOrWhiteSpace(dataDirectory)) return false;
        try
        {
            var target = Path.GetFullPath(Path.Combine(dataDirectory, "OrbitWeave")).TrimEnd(Path.DirectorySeparatorChar);
            var real = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OrbitWeave"));
            return !target.Equals(real, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return false; }
    }
}
