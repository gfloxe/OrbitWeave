namespace OrbitWeave.Orbit;

// Géométrie indépendante de WPF : tous les objets reliés à une section partagent ce résultat.
public readonly record struct WheelPoint(double X, double Y)
{
    public static WheelPoint operator +(WheelPoint a, WheelPoint b) => new(a.X + b.X, a.Y + b.Y);
    public static WheelPoint operator -(WheelPoint a, WheelPoint b) => new(a.X - b.X, a.Y - b.Y);
}

public sealed class WheelLayoutData
{
    public WheelPoint? Hub { get; set; }
    public Dictionary<string, WheelPoint> Sections { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, WheelPoint> Attachments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, WheelStackPlacement> Stacks { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    // Groupes de cartes détachés de la pile principale (voir CardGroups).
    public Dictionary<string, WheelCardGroup> CardGroups { get; set; } = new(StringComparer.Ordinal);
    public WheelLayoutData Clone() => new()
    {
        Hub = Hub,
        Sections = new(Sections, StringComparer.OrdinalIgnoreCase),
        Attachments = new(Attachments, StringComparer.OrdinalIgnoreCase),
        Stacks = new(Stacks, StringComparer.OrdinalIgnoreCase),
        CardGroups = (CardGroups ?? []).Where(pair => pair.Value is not null && double.IsFinite(pair.Value.X) && double.IsFinite(pair.Value.Y))
            .ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal)
    };
    public bool IsEmpty => Hub is null && Sections.Count == 0 && Attachments.Count == 0 && Stacks.Count == 0 && (CardGroups?.Count ?? 0) == 0;
}

// Décalage de la pile depuis le centre du nœud, et côté conservé lors du déplacement.
public readonly record struct WheelStackPlacement(double X, double Y, int Side);

public readonly record struct WheelMetrics(
    double InnerRadius, double OuterRadius, double SectionDiameter,
    double HubDiameter, double HaloDiameter, double SearchOffset, double SearchDiameter);

public readonly record struct WheelSectionInput(string Name, bool HasBadge);

public readonly record struct WheelSectionGeometry(
    string Name, int Index, WheelPoint Center, WheelPoint NodeTopLeft,
    WheelPoint SpokeStart, WheelPoint SpokeEnd, WheelPoint LabelAnchor,
    WheelPoint BadgeAnchor, double Angle, double Radius, bool HasBadge);

public sealed class WheelGeometry(WheelPoint hubCenter, WheelPoint hubTopLeft,
    WheelPoint haloTopLeft, WheelPoint searchTopLeft, IReadOnlyList<WheelSectionGeometry> sections)
{
    public WheelPoint HubCenter { get; } = hubCenter;
    public WheelPoint HubTopLeft { get; } = hubTopLeft;
    public WheelPoint HaloTopLeft { get; } = haloTopLeft;
    public WheelPoint SearchTopLeft { get; } = searchTopLeft;
    public IReadOnlyList<WheelSectionGeometry> Sections { get; } = sections;
    public WheelSectionGeometry Section(string name) =>
        Sections.First(section => section.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}

public static class WheelLayout
{
    public static readonly WheelPoint DefaultCenter = new(560, 560);

    // Une translation commune garde les écarts relatifs, y compris aux limites de la fenêtre.
    public static WheelLayoutData MoveHub(WheelLayoutData original, WheelPoint hub,
        IReadOnlyDictionary<string, WheelPoint> sections, WheelPoint delta, bool whole,
        double left = 74, double top = 164, double right = 1046, double bottom = 956)
    {
        var anchors = whole ? sections.Values.Append(hub).ToArray() : [hub];
        var dx = ClampTranslation(delta.X, left - anchors.Min(point => point.X), right - anchors.Max(point => point.X));
        var dy = ClampTranslation(delta.Y, top - anchors.Min(point => point.Y), bottom - anchors.Max(point => point.Y));
        var shift = new WheelPoint(dx, dy);
        var layout = original.Clone();
        layout.Hub = hub + shift;
        if (whole) foreach (var (name, point) in sections) layout.Sections[name] = point + shift;
        return layout;
    }

    private static double ClampTranslation(double value, double min, double max) => min <= max ? Math.Clamp(value, min, max) : 0;

    public static WheelPoint ProjectDrag(WheelPoint proposed, WheelPoint origin, WheelPoint hub,
        bool keepRadius, bool keepAngle, double grid)
    {
        if (grid > 0) proposed = new WheelPoint(origin.X + Math.Round((proposed.X - origin.X) / grid) * grid,
            origin.Y + Math.Round((proposed.Y - origin.Y) / grid) * grid);
        var dx = proposed.X - hub.X;
        var dy = proposed.Y - hub.Y;
        var radius = keepRadius ? Math.Sqrt(Math.Pow(origin.X - hub.X, 2) + Math.Pow(origin.Y - hub.Y, 2))
            : Math.Sqrt(dx * dx + dy * dy);
        var angle = keepAngle ? Math.Atan2(origin.Y - hub.Y, origin.X - hub.X) : Math.Atan2(dy, dx);
        return new WheelPoint(hub.X + Math.Cos(angle) * radius, hub.Y + Math.Sin(angle) * radius);
    }

    public static WheelPoint LimitSection(WheelPoint point, WheelPoint hub, double diameter, double hubDiameter,
        double left = 30, double top = 120, double right = 1090, double bottom = 1000)
    {
        var margin = diameter / 2 + 4;
        var x = Math.Clamp(point.X, left + margin, right - margin);
        var y = Math.Clamp(point.Y, top + margin, bottom - margin);
        var dx = x - hub.X;
        var dy = y - hub.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        var minimum = (diameter + hubDiameter) / 2 + 8;
        if (distance < minimum)
        {
            if (distance < 0.001) { dx = 0; dy = -1; distance = 1; }
            x = hub.X + dx / distance * minimum;
            y = hub.Y + dy / distance * minimum;
        }
        return new WheelPoint(Math.Clamp(x, left + margin, right - margin), Math.Clamp(y, top + margin, bottom - margin));
    }

    public static WheelGeometry Compute(IReadOnlyList<WheelSectionInput> inputs, WheelMetrics metrics,
        WheelLayoutData? layout = null, IReadOnlySet<string>? addedSections = null)
    {
        var hub = layout?.Hub ?? DefaultCenter;
        var sections = new List<WheelSectionGeometry>(inputs.Count);
        for (var i = 0; i < inputs.Count; i++)
        {
            var autoAngle = -Math.PI / 2 + i * Math.PI * 2 / Math.Max(1, inputs.Count);
            var outer = i % 2 == 1 || (inputs.Count > 1 && inputs.Count % 2 == 1 && i == inputs.Count - 1);
            var autoRadius = outer ? metrics.OuterRadius : metrics.InnerRadius;
            var autoPoint = new WheelPoint(DefaultCenter.X + Math.Cos(autoAngle) * autoRadius,
                DefaultCenter.Y + Math.Sin(autoAngle) * autoRadius);
            var input = inputs[i];
            var saved = default(WheelPoint);
            var positioned = layout is not null && layout.Sections.TryGetValue(input.Name, out saved);
            var node = positioned ? saved : autoPoint;
            var adjusted = false;
            if (!positioned && addedSections?.Contains(input.Name) == true && layout?.Sections.Count > 0)
            {
                var occupied = layout.Sections.Where(pair => inputs.Any(section => section.Name.Equals(pair.Key, StringComparison.OrdinalIgnoreCase))).Select(pair => pair.Value).ToArray();
                for (var step = 0; step < 72 && occupied.Any(point => Distance(point, node) < metrics.SectionDiameter + 12); step++)
                {
                    var candidate = autoAngle + (step + 1) * Math.PI / 36;
                    node = new WheelPoint(DefaultCenter.X + Math.Cos(candidate) * autoRadius, DefaultCenter.Y + Math.Sin(candidate) * autoRadius);
                    adjusted = true;
                }
            }
            var dx = node.X - hub.X;
            var dy = node.Y - hub.Y;
            var angle = positioned || adjusted || layout?.Hub is not null ? Math.Atan2(dy, dx) : autoAngle;
            var radius = positioned || adjusted || layout?.Hub is not null ? Math.Sqrt(dx * dx + dy * dy) : autoRadius;
            sections.Add(new WheelSectionGeometry(input.Name, i, node,
                new WheelPoint(Math.Round(node.X - metrics.SectionDiameter / 2), Math.Round(node.Y - metrics.SectionDiameter / 2)),
                hub, node, new WheelPoint(node.X, node.Y + metrics.SectionDiameter / 2 + 4) +
                    (layout?.Attachments.GetValueOrDefault($"wheel.label:{input.Name}") ?? default),
                new WheelPoint(node.X + 22, node.Y - 30) +
                    (layout?.Attachments.GetValueOrDefault($"wheel.badge:{input.Name}") ?? default), angle, radius, input.HasBadge));
        }
        return new WheelGeometry(hub,
            new WheelPoint(Math.Round(hub.X - metrics.HubDiameter / 2), Math.Round(hub.Y - metrics.HubDiameter / 2)),
            new WheelPoint(hub.X - metrics.HaloDiameter / 2, hub.Y - metrics.HaloDiameter / 2),
            new WheelPoint(Math.Round(hub.X - metrics.SearchDiameter / 2),
                Math.Round(hub.Y + metrics.SearchOffset - metrics.SearchDiameter / 2)) +
                    (layout?.Attachments.GetValueOrDefault("wheel.search") ?? default),
            sections);
    }

    private static double Distance(WheelPoint a, WheelPoint b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
}
