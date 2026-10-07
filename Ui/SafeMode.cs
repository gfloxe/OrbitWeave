namespace OrbitWeave.Ui;

// Démarrage avec l'interface d'origine : Maj maintenue, --safe-mode, ou 3 démarrages inachevés de suite.
public static class SafeMode
{
    private const string FileName = "pending-starts.txt";
    public static bool Active { get; set; }
    // Message à montrer une fois la roue affichée (mode de secours ou profil illisible).
    public static string? StartupMessage { get; set; }
    public static string Folder => Path.Combine(ActionStore.DirectoryPath, "ui");

    public static bool ShouldStart(IEnumerable<string> args, bool shiftDown, string folder) =>
        shiftDown || args.Contains("--safe-mode", StringComparer.OrdinalIgnoreCase) || PendingStarts(folder) >= 3;

    // Le compteur n'est qu'une aide : un fichier verrouillé ou interdit ne doit jamais empêcher la roue de démarrer.
    public static void MarkStarting(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, FileName), (PendingStarts(folder) + 1).ToString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { CrashLog.Write("SafeMode.MarkStarting", ex); }
    }

    public static void MarkStarted(string folder)
    {
        try { File.Delete(Path.Combine(folder, FileName)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { CrashLog.Write("SafeMode.MarkStarted", ex); }
    }

    private static int PendingStarts(string folder)
    {
        var path = Path.Combine(folder, FileName);
        try { return File.Exists(path) && int.TryParse(File.ReadAllText(path), out var count) ? count : 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }
}
