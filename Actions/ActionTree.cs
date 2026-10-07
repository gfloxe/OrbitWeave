namespace OrbitWeave.Actions;

// Sous-cartes : la liste reste plate, chaque carte connaît son parent (ParentId).
public static class ActionTree
{

    public static IReadOnlyList<ActionItem> Roots(IEnumerable<ActionItem> actions, string group) =>
        actions.Where(a => a.ParentId is null && a.Group.Trim().Equals(group.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

    public static IReadOnlyList<ActionItem> Children(IEnumerable<ActionItem> actions, Guid parentId) =>
        actions.Where(a => a.ParentId == parentId).ToList();

    public static bool HasChildren(IEnumerable<ActionItem> actions, Guid id) => actions.Any(a => a.ParentId == id);

    public static int Depth(IEnumerable<ActionItem> actions, ActionItem item) => Ancestors(actions, item.Id).Count;

    // De la racine au parent direct ; une boucle s'arrête au premier déjà-vu.
    public static IReadOnlyList<ActionItem> Ancestors(IEnumerable<ActionItem> actions, Guid id)
    {
        var byId = actions.GroupBy(a => a.Id).ToDictionary(g => g.Key, g => g.First());
        var chain = new List<ActionItem>();
        var seen = new HashSet<Guid> { id };
        var current = byId.GetValueOrDefault(id)?.ParentId;
        while (current is { } parentId && byId.TryGetValue(parentId, out var parent) && seen.Add(parentId))
        {
            chain.Insert(0, parent);
            current = parent.ParentId;
        }
        return chain;
    }

    public static IReadOnlyList<ActionItem> Descendants(IEnumerable<ActionItem> actions, Guid id)
    {
        var list = actions.ToList();
        var result = new List<ActionItem>();
        var seen = new HashSet<Guid> { id };
        void Walk(Guid parent)
        {
            foreach (var child in list.Where(a => a.ParentId == parent).ToList())
            {
                if (!seen.Add(child.Id)) continue;
                result.Add(child);
                Walk(child.Id);
            }
        }
        Walk(id);
        return result;
    }

    public static IReadOnlyList<ActionItem> Flatten(IEnumerable<ActionItem> actions, string group)
    {
        var list = actions.ToList();
        var result = new List<ActionItem>();
        foreach (var root in Roots(list, group))
        {
            root.TreeDepth = 0;
            result.Add(root);
            foreach (var item in Descendants(list, root.Id))
            {
                item.TreeDepth = Depth(list, item);
                result.Add(item);
            }
        }
        return result;
    }

    // Nouvelle sous-carte vide, placée après la dernière carte de la descendance du parent.
    public static ActionItem? AddChild(IList<ActionItem> actions, ActionItem parent)
    {
        var child = new ActionItem
        {
            Group = parent.Group, GroupIcon = parent.GroupIcon, GroupVisible = parent.GroupVisible, GroupType = parent.GroupType,
            Name = "Nouvelle action", Target = "", ParentId = parent.Id
        };
        var last = Descendants(actions, parent.Id).LastOrDefault() ?? parent;
        actions.Insert(actions.IndexOf(last) + 1, child);
        return child;
    }

    public static int Remove(IList<ActionItem> actions, ActionItem item)
    {
        var doomed = Descendants(actions, item.Id).Append(item).ToList();
        foreach (var action in doomed) actions.Remove(action);
        return doomed.Count;
    }

    public static bool Repair(IList<ActionItem> actions)
    {
        var changed = false;
        var ids = actions.Select(a => a.Id).ToHashSet();
        foreach (var action in actions)
            if (action.ParentId is { } parent && (parent == action.Id || !ids.Contains(parent)))
            { action.ParentId = null; changed = true; }
        // Boucle : en remontant depuis une carte, revenir sur un déjà-vu coupe le lien de cette carte.
        var byId = actions.GroupBy(a => a.Id).ToDictionary(g => g.Key, g => g.First());
        foreach (var action in actions)
        {
            var seen = new HashSet<Guid> { action.Id };
            var current = action.ParentId;
            while (current is { } id && byId.TryGetValue(id, out var parent))
            {
                if (!seen.Add(id)) { action.ParentId = null; changed = true; break; }
                current = parent.ParentId;
            }
        }
        // Une sous-carte appartient toujours à la section de sa racine.
        foreach (var action in actions.Where(a => a.ParentId is not null))
        {
            var root = Ancestors(actions, action.Id).FirstOrDefault();
            if (root is null || (action.Group == root.Group && action.GroupIcon == root.GroupIcon &&
                action.GroupVisible == root.GroupVisible && action.GroupType == root.GroupType)) continue;
            action.Group = root.Group;
            action.GroupIcon = root.GroupIcon;
            action.GroupVisible = root.GroupVisible;
            action.GroupType = root.GroupType;
            changed = true;
        }
        return changed;
    }

    // Carte-dossier : rien à lancer, elle ne sert qu'à ouvrir ses sous-cartes.
    public static bool IsFolderOnly(ActionItem action) =>
        action.Steps.All(step => step is OpenStep open && string.IsNullOrWhiteSpace(open.Target));
}
