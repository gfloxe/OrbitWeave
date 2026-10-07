namespace OrbitWeave.Actions;

public enum IconKind { None, Shell, Site, Image }

// Ce que la carte d'un bouton doit dessiner : un élément Shell (fichier, dossier, appli), un site, ou son symbole.
public sealed record IconTarget(IconKind Kind, string Value)
{
    public static readonly IconTarget Nothing = new(IconKind.None, "");

    public static IconTarget For(ActionItem action)
    {
        if (action.UseSymbol) return Nothing;
        if (!string.IsNullOrWhiteSpace(action.IconFile)) return Custom(action.IconFile, ActionStore.DirectoryPath, File.Exists);
        if (action.Steps.OfType<OpenStep>().FirstOrDefault() is not { } open) return Nothing;
        return Resolve(open.Target);
    }

    public static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".ico", ".bmp", ".gif"];

    public static bool IsImage(string path) => ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    // Icône choisie : une image (chemin relatif au dossier des données, ou absolu) ou un fichier dont on prend l'icône.
    public static IconTarget Custom(string file, string dataDirectory, Func<string, bool> fileExists)
    {
        var path = Path.IsPathFullyQualified(file) ? file : Path.Combine(dataDirectory, file);
        if (IsImage(path)) return fileExists(path) ? new IconTarget(IconKind.Image, path) : Nothing;
        return Resolve(path);
    }

    public static IconTarget Resolve(string target) =>
        Resolve(target, path => File.Exists(path) || Directory.Exists(path), FindProgram);

    public static IconTarget Resolve(string target, Func<string, bool> exists, Func<string, string?> findProgram)
    {
        target = Environment.ExpandEnvironmentVariables(target.Trim());
        if (target.Length == 0) return Nothing;
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            return new IconTarget(IconKind.Site, $"https://{uri.Host}/");
        if (target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return new IconTarget(IconKind.Shell, target);
        var hasSeparator = target.Contains('\\') || target.Contains('/');
        if (!hasSeparator && target.IndexOf(':') > 1) return Nothing; // ms-settings:, mailto:…
        if (Path.IsPathFullyQualified(target))
        {
            var path = target.Length > 3 ? target.TrimEnd('\\', '/') : target;
            return exists(path) ? new IconTarget(IconKind.Shell, path) : Nothing;
        }
        if (hasSeparator) return Nothing;
        var name = Path.HasExtension(target) ? target : target + ".exe";
        return findProgram(name) is { } found ? new IconTarget(IconKind.Shell, found) : Nothing;
    }

    // Comme le lancement par Windows : dossiers du PATH, puis la clé « App Paths ».
    private static string? FindProgram(string name)
    {
        var folders = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Prepend(Environment.GetFolderPath(Environment.SpecialFolder.Windows))
            .Prepend(Environment.SystemDirectory);
        foreach (var folder in folders)
        {
            try
            {
                var candidate = Path.Combine(Environment.ExpandEnvironmentVariables(folder.Trim().Trim('"')), name);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { }
        }
        foreach (var root in new[] { Microsoft.Win32.Registry.LocalMachine, Microsoft.Win32.Registry.CurrentUser })
        {
            try
            {
                using var key = root.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\App Paths\{name}");
                if (key?.GetValue(null) is string value)
                {
                    var path = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
                    if (File.Exists(path)) return path;
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        }
        return null;
    }
}
