using System.Text.Json;

namespace OrbitWeave.Actions;

// Modifications d'actions pour l'historique : toujours sur une copie, jamais sur la liste affichée.
public static class ActionEdits
{
    public static List<ActionItem> Clone(IEnumerable<ActionItem> actions) =>
        JsonSerializer.Deserialize<List<ActionItem>>(JsonSerializer.Serialize(actions.ToList(), ActionJson.Options), ActionJson.Options) ?? [];

    public static List<ActionItem> Rename(IEnumerable<ActionItem> actions, Guid id, string name) => Edit(actions, id, a => a.Name = name);
    public static List<ActionItem> SetIcon(IEnumerable<ActionItem> actions, Guid id, string icon) =>
        Edit(actions, id, a => { a.Icon = icon; a.UseSymbol = true; a.IconFile = null; });
    // « Auto » : retour à l'icône réelle ; le symbole reste en réserve si aucune icône n'est lisible.
    public static List<ActionItem> UseAutomaticIcon(IEnumerable<ActionItem> actions, Guid id) =>
        Edit(actions, id, a => { a.UseSymbol = false; a.IconFile = null; });
    // Image ou icône d'un fichier choisie par l'utilisateur ; elle passe devant l'icône automatique.
    public static List<ActionItem> SetIconFile(IEnumerable<ActionItem> actions, Guid id, string file) =>
        Edit(actions, id, a => { a.UseSymbol = false; a.IconFile = file; });
    // Un bouton encore sans vrai nom (« Nouveau bouton », ou le nom tiré de son ancienne cible) prend celui de ce qu'il ouvre.
    public static List<ActionItem> AddStep(IEnumerable<ActionItem> actions, Guid id, ActionStep step, string? name = null) => Edit(actions, id, a =>
    {
        var automatic = HasAutomaticName(a);
        a.Steps.Add(step);
        if (automatic) NameAfterTarget(a, name);
    });

    public static bool HasAutomaticName(ActionItem action) =>
        string.IsNullOrWhiteSpace(action.Name) || action.Name is NewButtonName or "Nouvelle action" ||
        action.Steps.OfType<OpenStep>().Any(step => action.Name == StepText.Describe(step));

    private static void NameAfterTarget(ActionItem action, string? name)
    {
        if (!string.IsNullOrWhiteSpace(name)) { action.Name = name.Trim(); return; }
        if (action.Steps.OfType<OpenStep>().FirstOrDefault() is not { } open) return;
        var described = StepText.Describe(open);
        // Cible encore en cours de saisie (« https:// ») : on attend qu'elle soit écrite.
        if (described is "(vide)" || described.Length == 0 || described.EndsWith(':') || described.Contains("//")) return;
        action.Name = described;
    }

    public static List<ActionItem> MoveStep(IEnumerable<ActionItem> actions, Guid id, int from, int to) => Edit(actions, id, a =>
    {
        if (!InRange(a, from) || !InRange(a, to) || from == to) return;
        var step = a.Steps[from];
        a.Steps.RemoveAt(from);
        a.Steps.Insert(to, step);
    });

    public static List<ActionItem> SetStepEnabled(IEnumerable<ActionItem> actions, Guid id, int index, bool enabled) =>
        Edit(actions, id, a => { if (InRange(a, index)) a.Steps[index].Enabled = enabled; });

    public static List<ActionItem> ReplaceStep(IEnumerable<ActionItem> actions, Guid id, int index, ActionStep step) =>
        Edit(actions, id, a =>
        {
            if (!InRange(a, index)) return;
            var automatic = HasAutomaticName(a);
            a.Steps[index] = step;
            if (automatic) NameAfterTarget(a, null);
        });

    public static List<ActionItem> RemoveStep(IEnumerable<ActionItem> actions, Guid id, int index) =>
        Edit(actions, id, a => { if (InRange(a, index)) a.Steps.RemoveAt(index); });

    public const string NewButtonName = "Nouveau bouton";

    // Bouton vide à la fin d'une section (parentId null) ou d'une carte (sous-carte). Pas de limite par branche.
    public static (List<ActionItem> Actions, Guid? Id, string? Error) NewButton(IEnumerable<ActionItem> actions, string section, Guid? parentId = null)
    {
        var copy = Clone(actions);
        var members = Members(copy, section);
        if (members.Count == 0) return (copy, null, null);
        ActionItem? parent = null;
        if (parentId is { } pid && (parent = copy.FirstOrDefault(a => a.Id == pid)) is null) return (copy, null, null);
        var item = Blank(parent ?? members[0]);
        item.ParentId = parent?.Id;
        var after = parent is null ? members[^1] : ActionTree.Descendants(copy, parent.Id).LastOrDefault() ?? parent;
        copy.Insert(copy.IndexOf(after) + 1, item);
        return (copy, item.Id, null);
    }

    // Copie du bouton (sans ses sous-cartes), juste après lui, au même niveau.
    public static (List<ActionItem> Actions, Guid? Id, string? Error) Duplicate(IEnumerable<ActionItem> actions, Guid id)
    {
        var copy = Clone(actions);
        if (copy.FirstOrDefault(a => a.Id == id) is not { } original) return (copy, null, null);
        var parent = original.ParentId is { } pid ? copy.FirstOrDefault(a => a.Id == pid) : null;
        var item = Clone([original])[0];
        item.Id = Guid.NewGuid();
        foreach (var step in item.Steps) step.Id = Guid.NewGuid();
        item.Name = $"{original.Name} (copie)";
        var last = ActionTree.Descendants(copy, original.Id).LastOrDefault() ?? original;
        copy.Insert(copy.IndexOf(last) + 1, item);
        return (copy, item.Id, null);
    }

    // Supprime le bouton et ses sous-cartes. Une section ne disparaît pas ainsi : son dernier bouton laisse place à un bouton vide.
    public static List<ActionItem> Remove(IEnumerable<ActionItem> actions, Guid id)
    {
        var copy = Clone(actions);
        if (copy.FirstOrDefault(a => a.Id == id) is not { } item) return copy;
        var index = copy.IndexOf(item);
        foreach (var gone in ActionTree.Descendants(copy, id).Append(item).ToList()) copy.Remove(gone);
        KeepSection(copy, item, index);
        return copy;
    }

    // Déplace le bouton (avec ses sous-cartes) à la fin d'une autre section, au premier niveau.
    public static (List<ActionItem> Actions, string? Error) MoveToSection(IEnumerable<ActionItem> actions, Guid id, string section)
    {
        var copy = Clone(actions);
        var target = Members(copy, section);
        if (copy.FirstOrDefault(a => a.Id == id) is not { } item || target.Count == 0) return (copy, null);
        if (item.ParentId is null && SameSection(item.Group, section)) return (copy, null);
        var moving = new[] { item }.Concat(ActionTree.Descendants(copy, id)).ToList();
        var index = copy.IndexOf(item);
        var origin = Clone([item])[0];
        foreach (var card in moving)
        {
            copy.Remove(card);
            card.Group = target[0].Group; card.GroupIcon = target[0].GroupIcon;
            card.GroupType = target[0].GroupType; card.GroupVisible = target[0].GroupVisible;
        }
        item.ParentId = null;
        KeepSection(copy, origin, index);
        var last = Members(copy, section)[^1];
        copy.InsertRange(copy.IndexOf(last) + 1, moving);
        return (copy, null);
    }

    public static (List<ActionItem> Actions, string? Error) RenameSection(IEnumerable<ActionItem> actions, string section, string name)
    {
        var copy = Clone(actions);
        name = name.Trim();
        if (name.Length == 0) return (copy, "Le nom de la section ne peut pas être vide.");
        if (!SameSection(section, name) && Members(copy, name) is [var existing, ..]) return (copy, $"Une section « {existing.Group.Trim()} » existe déjà.");
        foreach (var item in Members(copy, section)) item.Group = name;
        return (copy, null);
    }

    public static List<ActionItem> RemoveSection(IEnumerable<ActionItem> actions, string section) =>
        Clone(actions).Where(a => !SameSection(a.Group, section)).ToList();

    public static List<ActionItem> SetSectionIcon(IEnumerable<ActionItem> actions, string section, string icon)
    {
        var copy = Clone(actions);
        foreach (var item in Members(copy, section)) item.GroupIcon = icon;
        return copy;
    }

    public const string NewSectionName = "Nouvelle section";

    // Section neuve (« Nouvelle section », « Nouvelle section 2 »…) avec un bouton vide, juste après la section donnée.
    public static (List<ActionItem> Actions, string Name) NewSection(IEnumerable<ActionItem> actions, string? after = null)
    {
        var copy = Clone(actions);
        var name = NewSectionName;
        for (var n = 2; Members(copy, name).Count > 0; n++) name = $"{NewSectionName} {n}";
        var item = Blank(new ActionItem { Group = name, GroupIcon = "✦" });
        var last = after is null ? null : Members(copy, after).LastOrDefault();
        copy.Insert(last is null ? copy.Count : copy.IndexOf(last) + 1, item);
        return (copy, name);
    }

    private static bool SameSection(string a, string b) => a.Trim().Equals(b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static List<ActionItem> Members(List<ActionItem> actions, string section) =>
        actions.Where(a => SameSection(a.Group, section)).ToList();

    private static ActionItem Blank(ActionItem template) => new()
    {
        Group = template.Group, GroupIcon = template.GroupIcon, GroupType = template.GroupType, GroupVisible = template.GroupVisible,
        Name = NewButtonName, Icon = "✦"
    };

    // Section devenue vide : un bouton vide la garde à sa place.
    private static void KeepSection(List<ActionItem> actions, ActionItem removed, int index)
    {
        if (Members(actions, removed.Group).Count > 0) return;
        actions.Insert(Math.Min(index, actions.Count), Blank(removed));
    }

    private static bool InRange(ActionItem action, int index) => index >= 0 && index < action.Steps.Count;

    private static List<ActionItem> Edit(IEnumerable<ActionItem> actions, Guid id, Action<ActionItem> change)
    {
        var copy = Clone(actions);
        if (copy.FirstOrDefault(a => a.Id == id) is { } action) change(action);
        return copy;
    }
}
