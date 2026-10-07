namespace OrbitWeave.Actions;

// Fichiers, applis ou adresses déposés sur la roue : ce qu'ils deviennent, et où.
public static class DropEdits
{
    public static (List<ActionStep> Steps, List<string> Missing) StepsFor(IEnumerable<string> items, Func<string, bool> exists)
    {
        var steps = new List<ActionStep>();
        var missing = new List<string>();
        foreach (var raw in items)
        {
            var item = raw.Trim();
            if (item.Length == 0) continue;
            if (Uri.TryCreate(item, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                steps.Add(new OpenStep { Target = item });
            else if (!exists(item))
                missing.Add(item);
            else if (Path.GetExtension(item).Equals(".ps1", StringComparison.OrdinalIgnoreCase))
                steps.Add(new ScriptStep { Source = ScriptSource.Fichier, Path = item });
            else
                steps.Add(new OpenStep { Target = item });
        }
        return (steps, missing);
    }

    public static string NameFor(ActionStep step) => step switch
    {
        ScriptStep script => Path.GetFileNameWithoutExtension(script.Path),
        _ => StepText.Describe(step)
    };

    // « Remplacer » : le bouton lance désormais ce qui a été déposé. Son nom suit s'il n'avait pas été choisi.
    public static List<ActionItem> Replace(IEnumerable<ActionItem> actions, Guid id, IReadOnlyList<ActionStep> steps)
    {
        var copy = ActionEdits.Clone(actions);
        if (steps.Count == 0 || copy.FirstOrDefault(a => a.Id == id) is not { } action) return copy;
        var automaticName = HasAutomaticName(action);
        action.Steps = Fresh(steps);
        action.UseSymbol = false;
        if (automaticName) action.Name = NameFor(steps[0]);
        return copy;
    }

    // « + Ajouter » : à la suite de ses étapes. Un bouton vide prend simplement ce qui est déposé.
    public static List<ActionItem> Add(IEnumerable<ActionItem> actions, Guid id, IReadOnlyList<ActionStep> steps)
    {
        var list = actions.ToList();
        if (list.FirstOrDefault(a => a.Id == id) is { Steps.Count: 0 }) return Replace(list, id, steps);
        var copy = ActionEdits.Clone(list);
        if (steps.Count == 0 || copy.FirstOrDefault(a => a.Id == id) is not { } action) return copy;
        var automaticName = HasAutomaticName(action);
        action.Steps.AddRange(Fresh(steps));
        // « Nouveau bouton » dont la seule étape est encore vide : il prend le nom de ce qui est déposé.
        if (automaticName && action.Steps.Select(NameFor).FirstOrDefault(Named) is { } name) action.Name = name;
        return copy;
    }

    // Nom pas encore choisi par l'utilisateur (« Nouveau bouton », ou tiré d'une cible, script compris).
    private static bool HasAutomaticName(ActionItem action) =>
        ActionEdits.HasAutomaticName(action) || action.Steps.FirstOrDefault() is { } first && action.Name == NameFor(first);

    private static bool Named(string name) => name.Length > 0 && name != "(vide)";

    // Dépôt dans le vide près d'une section ouverte : un nouveau bouton à la fin de sa pile.
    public static (List<ActionItem> Actions, Guid? Id, string? Error) Create(IEnumerable<ActionItem> actions, string section, IReadOnlyList<ActionStep> steps)
    {
        var copy = ActionEdits.Clone(actions);
        var members = copy.Where(a => a.Group.Trim().Equals(section.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (members.Count == 0 || steps.Count == 0) return (copy, null, null);
        var name = members[0].Group;
        var template = members[0];
        var item = new ActionItem
        {
            Group = name, GroupIcon = template.GroupIcon, GroupType = template.GroupType, GroupVisible = template.GroupVisible,
            Name = NameFor(steps[0]), Icon = "✦", Steps = Fresh(steps)
        };
        copy.Insert(copy.IndexOf(members[^1]) + 1, item);
        return (copy, item.Id, null);
    }

    // Copies neuves (nouveaux identifiants) : une même étape ne doit jamais être partagée entre deux listes.
    private static List<ActionStep> Fresh(IEnumerable<ActionStep> steps) =>
        steps.Select(step =>
        {
            var copy = System.Text.Json.JsonSerializer.Deserialize<ActionStep>(
                System.Text.Json.JsonSerializer.Serialize(step, ActionJson.Options), ActionJson.Options)!;
            copy.Id = Guid.NewGuid();
            return copy;
        }).ToList();
}
