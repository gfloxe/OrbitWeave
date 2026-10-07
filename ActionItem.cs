using System.Text.Json;
using System.Text.Json.Serialization;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using OrbitWeave.Actions;

namespace OrbitWeave;

public sealed class ActionItem : INotifyPropertyChanged
{
    private string _group = "Raccourcis";
    private string _groupIcon = "✦";
    private string _name = "Nouvelle action";
    private string _icon = "✦";
    private string _type = "Application";
    private int _badgeCount;
    private bool _groupVisible = true;
    private string _groupType = "Ordinaire";
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
    public string Group { get => _group; set => Set(ref _group, value); }
    public string GroupIcon { get => _groupIcon; set => Set(ref _groupIcon, value); }
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Icon { get => _icon; set => Set(ref _icon, value); }
    // Symbole choisi dans la bulle : il remplace l'icône réelle de ce que le bouton ouvre. Écrit seulement s'il vaut true.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool UseSymbol { get; set; }
    // Icône choisie : une image copiée dans les données (chemin relatif « icons\… ») ou un fichier dont on prend l'icône.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IconFile { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    // Carte parente : null = carte de premier niveau de la section.
    public Guid? ParentId { get; set; }
    public List<ActionStep> Steps { get; set; } = [];

    // Niveau dans l'arbre, calculé pour l'affichage (Réglages) ; jamais enregistré.
    [JsonIgnore]
    public int TreeDepth { get; set; }

    // Type d'origine (Application, Dossier, Site) : utilisé par l'éditeur actuel, plus enregistré.
    [JsonIgnore]
    public string Type { get => _type; set => Set(ref _type, value); }

    // Jusqu'au nouvel éditeur, les réglages modifient la cible de la première étape « Ouvrir ».
    [JsonIgnore]
    public string Target
    {
        get => Steps.OfType<OpenStep>().FirstOrDefault()?.Target ?? "";
        set
        {
            var step = Steps.OfType<OpenStep>().FirstOrDefault();
            if (step is null)
            {
                step = new OpenStep();
                Steps.Insert(0, step);
            }
            if (step.Target == value) return;
            step.Target = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Target)));
        }
    }
    public int BadgeCount { get => _badgeCount; set => Set(ref _badgeCount, Math.Max(0, value)); }
    public bool GroupVisible { get => _groupVisible; set => Set(ref _groupVisible, value); }
    public string GroupType { get => _groupType; set => Set(ref _groupType, value); }
}

public static class ActionStore
{
    public static string DirectoryPath => DiagnosticFlags.DataDirectory is { } data ? Path.Combine(data, "OrbitWeave") :
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OrbitWeave");
    public static string LegacyDirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "OrbitWeave", "UserData");
    public static string FilePath => Path.Combine(DirectoryPath, "actions.json");

    public static void EnsureMigrated()
    {
        Directory.CreateDirectory(DirectoryPath);
        if (DiagnosticFlags.DataDirectory is not null) return;
        foreach (var name in new[] { "actions.json", "settings.json" })
        {
            var source = Path.Combine(LegacyDirectoryPath, name);
            var destination = Path.Combine(DirectoryPath, name);
            if (File.Exists(source) && !File.Exists(destination)) File.Copy(source, destination);
        }
    }

    public static List<ActionItem> Load() => Load(FilePath);

    public static List<ActionItem> Load(string path)
    {
        if (!File.Exists(path)) return Defaults();
        try
        {
            var (actions, changed) = ActionMigration.Read(File.ReadAllText(path));
            // Un parent disparu ou une boucle ne doit jamais faire disparaître une carte.
            changed |= ActionTree.Repair(actions);
            if (changed)
            {
                var backup = Path.Combine(Path.GetDirectoryName(path)!, "actions.backup.json");
                if (!File.Exists(backup)) File.Copy(path, backup);
                Save(actions, path);
            }
            return actions;
        }
        catch (Exception)
        {
            // Un fichier illisible est mis de côté, jamais écrasé : on repart des actions par défaut.
            try
            {
                File.Copy(path, Path.Combine(Path.GetDirectoryName(path)!,
                    $"actions.broken-{DateTime.Now:yyyyMMdd-HHmmss}.json"), true);
            }
            catch (IOException) { }
            return Defaults();
        }
    }

    public static void Save(IEnumerable<ActionItem> actions) => Save(actions, FilePath);

    public static void Save(IEnumerable<ActionItem> actions, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(actions, ActionJson.Options));
        File.Move(temp, path, true);
    }

    public static List<ActionItem> Defaults()
    {
        var defaults = DefaultItems();
        foreach (var action in defaults) action.GroupType = ActionMigration.InferGroupType(action.Group);
        return defaults;
    }

    private static List<ActionItem> DefaultItems() => new()
    {
        new() { Group = "Fichiers", GroupIcon = "▣", Name = "Explorateur", Icon = "▣", Type = "Application", Target = "explorer.exe" },
        new() { Group = "Fichiers", GroupIcon = "▣", Name = "Documents", Icon = "◇", Type = "Dossier", Target = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) },
        new() { Group = "Web", GroupIcon = "◎", Name = "Navigateur", Icon = "◎", Type = "Site", Target = "https://www.google.com" },
        new() { Group = "Web", GroupIcon = "◎", Name = "YouTube", Icon = "▶", Type = "Site", Target = "https://www.youtube.com" },
        new() { Group = "Outils", GroupIcon = "⌗", Name = "Calculatrice", Icon = "⌗", Type = "Application", Target = "calc.exe" },
        new() { Group = "Outils", GroupIcon = "⌗", Name = "Bloc-notes", Icon = "≡", Type = "Application", Target = "notepad.exe" },
        new() { Group = "Windows", GroupIcon = "⚙", Name = "Paramètres", Icon = "⚙", Type = "Site", Target = "ms-settings:" },
        new() { Group = "Windows", GroupIcon = "⚙", Name = "Gestionnaire", Icon = "▤", Type = "Application", Target = "taskmgr.exe" },
        new() { Group = "Accès", GroupIcon = "↓", Name = "Téléchargements", Icon = "↓", Type = "Dossier", Target = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads" },
        new() { Group = "Accès", GroupIcon = "↓", Name = "Bureau", Icon = "□", Type = "Dossier", Target = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) },
        new() { Group = "Création", GroupIcon = "✎", Name = "VS Code", Icon = "⌘", Type = "Application", Target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Visual Studio Code.lnk") },
        new() { Group = "Création", GroupIcon = "✎", Name = "CapCut", Icon = "▧", Type = "Application", Target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "CapCut.lnk") },
        new() { Group = "Média", GroupIcon = "♫", Name = "Spotify", Icon = "♫", Type = "Application", Target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Spotify.lnk") },
        new() { Group = "Média", GroupIcon = "♫", Name = "Musique", Icon = "♪", Type = "Dossier", Target = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic) },
        new() { Group = "Social", GroupIcon = "◌", Name = "Discord", Icon = "◌", Type = "Application", Target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Discord.lnk") },
        new() { Group = "Social", GroupIcon = "◌", Name = "Courriel", Icon = "✉", Type = "Site", Target = "https://mail.google.com" }
    };
}
