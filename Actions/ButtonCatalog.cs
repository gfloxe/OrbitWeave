namespace OrbitWeave.Actions;

public sealed record CatalogEntry(string Name, string Icon, string Target);

// « Ajouter un bouton connu » (clic droit d'une section) : de quoi ravoir un bouton supprimé.
// Les boutons d'origine de la roue, les outils de Windows ; les applis installées viennent d'InstalledApps.
public static class ButtonCatalog
{
    public static IReadOnlyList<CatalogEntry> Originals() =>
        ActionStore.Defaults()
            .Select(a => new CatalogEntry(a.Name, a.Icon, a.Target))
            .Where(e => e.Target.Length > 0)
            .DistinctBy(e => e.Target, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static readonly IReadOnlyList<CatalogEntry> WindowsTools =
    [
        new("Gestionnaire des tâches", "▤", "taskmgr.exe"),
        new("Moniteur de ressources", "▥", "resmon.exe"),
        new("Paramètres", "⚙", "ms-settings:"),
        new("Windows Update", "↻", "ms-settings:windowsupdate"),
        new("Applications installées", "▦", "ms-settings:appsfeatures"),
        new("Affichage", "▭", "ms-settings:display"),
        new("Son", "♪", "ms-settings:sound"),
        new("Bluetooth", "✱", "ms-settings:bluetooth"),
        new("Réseau et Internet", "◎", "ms-settings:network"),
        new("Panneau de configuration", "▣", "control.exe"),
        new("Explorateur de fichiers", "▣", "explorer.exe"),
        new("Ce PC", "▢", "shell:MyComputerFolder"),
        new("Corbeille", "⌫", "shell:RecycleBinFolder"),
        new("Téléchargements", "↓", "shell:Downloads"),
        new("Invite de commandes", "›", "cmd.exe"),
        new("PowerShell", "»", "powershell.exe"),
        new("Bloc-notes", "≡", "notepad.exe"),
        new("Calculatrice", "⌗", "calc.exe"),
        new("Paint", "✎", "mspaint.exe"),
        new("Outil Capture d'écran", "✂", "snippingtool.exe"),
        new("Gestionnaire de périphériques", "⚙", "devmgmt.msc"),
        new("Gestion des disques", "◔", "diskmgmt.msc"),
        new("Services", "⚙", "services.msc"),
        new("Observateur d'événements", "☰", "eventvwr.msc"),
        new("Informations système", "ⓘ", "msinfo32.exe"),
        new("Éditeur du Registre", "▤", "regedit.exe"),
    ];

    // Initiale d'une appli pour le classement par lettre : sans accent, « # » pour un chiffre ou un symbole.
    public static string Initial(string name)
    {
        var first = name.Trim().FirstOrDefault();
        var plain = first.ToString().Normalize(System.Text.NormalizationForm.FormD).FirstOrDefault();
        return char.IsLetter(plain) ? char.ToUpperInvariant(plain).ToString() : "#";
    }

    // Déjà dans la section : même cible (sans tenir compte de la casse ni des espaces autour).
    public static bool IsIn(IEnumerable<ActionItem> actions, string section, string target) =>
        actions.Any(a => a.Group.Trim().Equals(section.Trim(), StringComparison.OrdinalIgnoreCase) &&
            a.Steps.OfType<OpenStep>().Any(s => s.Target.Trim().Equals(target.Trim(), StringComparison.OrdinalIgnoreCase)));

    // Nouveau bouton à la fin de la section, qui ouvre la cible ; l'icône réelle s'affiche si elle existe.
    public static (List<ActionItem> Actions, Guid? Id, string? Error) Add(IEnumerable<ActionItem> actions, string section, CatalogEntry entry)
    {
        var (list, id, error) = ActionEdits.NewButton(actions, section);
        if (id is not { } added || list.FirstOrDefault(a => a.Id == added) is not { } item) return (list, id, error);
        item.Name = entry.Name;
        item.Icon = entry.Icon;
        item.UseSymbol = false;
        item.Steps = [new OpenStep { Target = entry.Target }];
        return (list, id, error);
    }
}
