namespace OrbitWeave.Actions;

public sealed record InstalledApp(string Name, string Path);

// « + › Appli installée » : les raccourcis du menu Démarrer, lus une fois en arrière-plan.
public static class InstalledApps
{
    private static readonly string[] Extensions = [".lnk", ".url", ".appref-ms"];
    private static readonly string[] Excluded = ["uninstall", "désinstaller", "desinstaller"];
    private static Task<List<InstalledApp>>? _cached;

    public static string[] StartMenuFolders =>
    [
        Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
        Environment.GetFolderPath(Environment.SpecialFolder.StartMenu)
    ];

    public static Task<List<InstalledApp>> Cached => _cached ??= Task.Run(() => Merge(Scan(StartMenuFolders), StoreApps()));

    // Applis du Store (sans raccourci du menu Démarrer) : ouvertes par « shell:AppsFolder\<id>».
    public static List<InstalledApp> Merge(List<InstalledApp> shortcuts, IEnumerable<(string Name, string Id)> storeApps)
    {
        var names = new HashSet<string>(shortcuts.Select(a => a.Name), StringComparer.OrdinalIgnoreCase);
        var merged = new List<InstalledApp>(shortcuts);
        foreach (var (name, id) in storeApps)
            if (id.Contains('!') && !string.IsNullOrWhiteSpace(name) && names.Add(name))
                merged.Add(new InstalledApp(name, $@"shell:AppsFolder\{id}"));
        return merged.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Lecture du dossier Shell « Applications » sur un fil STA ; tout échec → aucune appli du Store.
    private static List<(string Name, string Id)> StoreApps()
    {
        var apps = new List<(string, string)>();
        var thread = new Thread(() =>
        {
            try
            {
                if (Type.GetTypeFromProgID("Shell.Application") is not { } type) return;
                dynamic shell = Activator.CreateInstance(type)!;
                dynamic folder = shell.NameSpace("shell:AppsFolder");
                foreach (var item in folder.Items())
                    apps.Add(((string)item.Name, (string)item.Path));
            }
            catch (Exception) { apps.Clear(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(10));
        return apps;
    }

    public static List<InstalledApp> Scan(IEnumerable<string> folders)
    {
        var apps = new List<InstalledApp>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder)) continue;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var file in files.OrderBy(f => f.Count(c => c == System.IO.Path.DirectorySeparatorChar)).ThenBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                if (!Extensions.Contains(System.IO.Path.GetExtension(file).ToLowerInvariant())) continue;
                var name = System.IO.Path.GetFileNameWithoutExtension(file);
                if (Excluded.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase))) continue;
                if (names.Add(name)) apps.Add(new InstalledApp(name, file));
            }
        }
        return apps.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
