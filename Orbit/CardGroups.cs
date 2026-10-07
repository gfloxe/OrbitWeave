namespace OrbitWeave.Orbit;

// Groupe de cartes détaché de la pile principale d'une section : X, Y = décalage de la première carte
// (bord gauche, centre vertical) depuis le centre du nœud de la section, comme WheelStackPlacement.
public sealed class WheelCardGroup
{
    public string Section { get; set; } = "";
    public List<Guid> Cards { get; set; } = [];
    public double X { get; set; }
    public double Y { get; set; }
    public WheelCardGroup Clone() => new() { Section = Section ?? "", Cards = [.. Cards ?? []], X = X, Y = Y };
}

// Une carte libre est un groupe d'une seule carte ; les cartes sans groupe forment la pile principale.
public static class CardGroups
{
    public static (List<Guid> Main, List<(string Id, WheelCardGroup Group, List<Guid> Cards)> Extra) Partition(
        WheelLayoutData layout, string section, IReadOnlyList<Guid> roots)
    {
        var known = roots.ToHashSet();
        var extra = new List<(string, WheelCardGroup, List<Guid>)>();
        var grouped = new HashSet<Guid>();
        foreach (var (id, group) in OfSection(layout, section))
        {
            var cards = group.Cards.Where(card => known.Contains(card) && grouped.Add(card)).ToList();
            if (cards.Count > 0) extra.Add((id, group, cards));
        }
        return (roots.Where(card => !grouped.Contains(card)).ToList(), extra);
    }

    public static string Detach(WheelLayoutData layout, string section, Guid card, double x, double y)
    {
        Remove(layout, section, card);
        return Add(layout, section, [card], x, y);
    }

    // Coupe le groupe (ou la pile principale) qui contient la carte, juste après elle ; null si elle est la dernière.
    public static string? CutAfter(WheelLayoutData layout, string section, IReadOnlyList<Guid> roots, Guid card, double x, double y)
    {
        var (main, extra) = Partition(layout, section, roots);
        var owner = extra.FirstOrDefault(item => item.Cards.Contains(card));
        var cards = owner.Group is null ? main : owner.Cards;
        var index = cards.IndexOf(card);
        if (index < 0 || index == cards.Count - 1) return null;
        var moved = cards.Skip(index + 1).ToList();
        if (owner.Group is not null) owner.Group.Cards.RemoveAll(moved.Contains);
        return Add(layout, section, moved, x, y);
    }

    // Nouvelle colonne : bord gauche juste après la colonne la plus à l'extérieur (côté de la branche),
    // un écart de « gap » entre les deux. Côté 0 (pile verticale) : à droite.
    public static double NextColumnLeft(IReadOnlyList<System.Windows.Rect> columns, int side, double width, double gap)
    {
        if (columns.Count == 0) return 0;
        return side < 0 ? columns.Min(c => c.Left) - gap - width : columns.Max(c => c.Right) + gap;
    }

    // Recolle un groupe : contre un autre groupe (au début ou à la fin), ou dans la pile principale (target null).
    public static void Merge(WheelLayoutData layout, string sourceId, string? targetId, bool append)
    {
        if (!layout.CardGroups.Remove(sourceId, out var source)) return;
        if (targetId is null || !layout.CardGroups.TryGetValue(targetId, out var target)) return;
        target.Cards = append ? [.. target.Cards, .. source.Cards] : [.. source.Cards, .. target.Cards];
    }

    private static IEnumerable<(string Id, WheelCardGroup Group)> OfSection(WheelLayoutData layout, string section) =>
        layout.CardGroups.Where(pair => pair.Value.Section.Equals(section, StringComparison.OrdinalIgnoreCase))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => (pair.Key, pair.Value));

    private static void Remove(WheelLayoutData layout, string section, Guid card)
    {
        foreach (var (id, group) in OfSection(layout, section).ToList())
            if (group.Cards.Remove(card) && group.Cards.Count == 0) layout.CardGroups.Remove(id);
    }

    private static string Add(WheelLayoutData layout, string section, List<Guid> cards, double x, double y)
    {
        var id = $"g{layout.CardGroups.Count + 1:D3}";
        for (var i = layout.CardGroups.Count + 2; layout.CardGroups.ContainsKey(id); i++) id = $"g{i:D3}";
        layout.CardGroups[id] = new WheelCardGroup { Section = section, Cards = cards, X = x, Y = y };
        return id;
    }
}
