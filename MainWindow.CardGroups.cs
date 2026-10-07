using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using OrbitWeave.Actions;
using OrbitWeave.Orbit;
using OrbitWeave.Ui;
using Button = System.Windows.Controls.Button;
using Panel = System.Windows.Controls.Panel;

namespace OrbitWeave;

// Groupes de cartes d'une section : coupe (✂), cartes libres et recollage par aimant, en mode édition.
public partial class MainWindow
{
    private sealed record CutRequest(string Section, Guid Card, Guid Next);

    // Place de chaque carte affichée : groupe (null = pile principale) et rang dans ce groupe.
    private readonly Dictionary<Guid, (string? Group, int Index)> _cardSlots = [];
    // Instantané pris au début d'un glisser de carte : la carte, sa place et sa position d'alors.
    private (Guid Card, string? Group, int Index, Vector Offset, string? Moving)? _cardDrag;
    private const double MagnetDistance = 48;

    public event Action<WheelLayoutData>? WheelLayoutProposed;

    private (List<ActionItem> Main, List<(string Id, WheelCardGroup Group, List<ActionItem> Cards)> Extra) SplitBranch(GroupInfo group)
    {
        var byId = group.Actions.GroupBy(action => action.Id).ToDictionary(item => item.Key, item => item.First());
        var (main, extra) = CardGroups.Partition(UiRuntime.Profile.Layout, group.Name, group.Actions.Select(action => action.Id).ToList());
        return (main.Select(id => byId[id]).ToList(),
            extra.Select(item => (item.Id, item.Group, item.Cards.Select(id => byId[id]).ToList())).ToList());
    }

    // Position d'un groupe sur la toile : bord gauche et centre de la première carte, gardés à l'écran.
    private (double Left, double FirstY) GroupPlacement(Point node, WheelCardGroup group, int count)
    {
        var left = Math.Round(node.X + group.X);
        var firstY = node.Y + group.Y;
        if (HasCustomViewport && _viewportReady && count > 0)
        {
            var height = Token("ActionCardHeight");
            var column = AdaptiveViewport.KeepInside(new Rect(left, firstY - height / 2, Token("ActionCardWidth"),
                (count - 1) * Token("ActionCardPitch") + height), _screenCanvasBounds);
            left = column.Left;
            firstY = column.Top + height / 2;
        }
        return (left, firstY);
    }

    private void AddCardGroup(string section, Point node, string id, WheelCardGroup group, List<ActionItem> cards)
    {
        var count = cards.Count;
        var width = Token("ActionCardWidth");
        var height = Token("ActionCardHeight");
        var pitch = Token("ActionCardPitch");
        var (left, firstY) = GroupPlacement(node, group, count);
        var side = left + width / 2 < node.X ? -1 : 1;
        var stagger = Token("MotionCardStagger");
        for (var i = 0; i < count; i++)
        {
            var action = cards[i];
            var top = Math.Round(Math.Round(firstY + i * pitch) - height / 2);
            var (start, p1, p2, end, linked) = GroupLink(node, left, firstY, count, i);
            var path = new System.Windows.Shapes.Path
            {
                Data = new PathGeometry([new PathFigure(start, [new BezierSegment(p1, p2, end, true)], false)]),
                Stroke = BrushToken("ConnectorBrush"), StrokeThickness = Token("ConnectorThickness"), IsHitTestVisible = false
            }.With($"wheel.link:{action.Id}");
            Customize(path);
            Panel.SetZIndex(path, -1);
            var card = CreateBranchCard(action);
            Canvas.SetLeft(card, left);
            Canvas.SetTop(card, top);
            Customize(card);
            if (linked) AddExpanded(path, i * stagger, side);
            AddExpanded(card, i * stagger, side);
            _cardSlots[action.Id] = (id, i);
            AddChildButtonIfEditing(action, left, top, side, null);
        }
        AddCutButtonsIfEditing(section, cards.Take(count).ToList(), left, firstY);
        AddGroupPlusIfEditing(section, id, left, firstY, count);
    }

    // + sous la dernière carte d'une colonne ajoutée : un nouveau bouton en bas de cette colonne.
    private sealed record GroupAddRequest(string Section, string Group);

    private void AddGroupPlusIfEditing(string section, string groupId, double left, double firstY, int count)
    {
        if (!_wheelEditMode || count == 0) return;
        var plus = PlusBadge(new GroupAddRequest(section, groupId), true, "Ajouter un bouton");
        Canvas.SetLeft(plus, Math.Round(left + Token("ActionCardWidth") / 2 - 12));
        Canvas.SetTop(plus, Math.Round(firstY + (count - 1) * Token("ActionCardPitch") + Token("ActionCardHeight") / 2 + 8));
        Panel.SetZIndex(plus, int.MaxValue);
        OrbitCanvas.Children.Add(plus);
        _expandedElements.Add(plus);
    }

    private void AddToGroup(GroupAddRequest request)
    {
        if (_wheelEditor is not { } editor) return;
        var layout = UiRuntime.Profile.Layout.Clone();
        if (!layout.CardGroups.TryGetValue(request.Group, out var group)) return;
        var (list, id, error) = ActionEdits.NewButton(_actions, request.Section);
        if (error is not null) { Notifications.Show("Impossible d'ajouter", error); return; }
        if (id is not { } added) return;
        PinMainStack(layout, request.Section);
        group.Cards.Add(added);
        editor.ApplyActionsAndLayoutFromWheel(list, layout);
        Dispatcher.BeginInvoke(() => SelectCard(request.Section, added, null), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // Lien d'une carte d'un groupe détaché vers sa section. Groupe au-dessus ou au-dessous du nœud :
    // un seul lien vertical, vers la carte la plus proche (linked = false pour les autres).
    private (Point Start, Point P1, Point P2, Point End, bool Linked) GroupLink(Point node, double left, double firstY, int count, int i)
    {
        var width = Token("ActionCardWidth");
        var height = Token("ActionCardHeight");
        var y = Math.Round(firstY + i * Token("ActionCardPitch"));
        var top = Math.Round(y - height / 2);
        var side = left + width / 2 < node.X ? -1 : 1;
        var radius = OrbitLayout.SectionDiameter / 2;
        var vertical = left < node.X + radius && left + width > node.X - radius;
        var below = firstY > node.Y;
        if (vertical)
        {
            var direction = below ? 1 : -1;
            // Sous le nœud, le lien part sous l'étiquette de la section.
            var start = new Point(node.X, below ? node.Y + radius + 4 + Token("SectionLabelHeight") : node.Y - radius);
            var end = new Point(Math.Round(left + width / 2), below ? top : top + height);
            var bend = Math.Min(40, Math.Abs(end.Y - start.Y) / 2);
            return (start, new Point(start.X, start.Y + direction * bend), new Point(end.X, end.Y - direction * bend), end,
                i == (below ? 0 : count - 1));
        }
        var from = new Point(node.X + side * radius, node.Y);
        var to = new Point(side < 0 ? left + width : left, y);
        var curve = Math.Min(40, Math.Max(0, side * (to.X - from.X)) / 2);
        return (from, new Point(from.X + side * curve, from.Y), new Point(to.X - side * curve, to.Y), to, true);
    }

    // Ciseaux entre deux cartes voisines : un clic coupe la pile juste là.
    private void AddCutButtonsIfEditing(string section, IReadOnlyList<ActionItem> cards, double left, double firstY)
    {
        if (!_wheelEditMode || cards.Count < 2) return;
        var width = Token("ActionCardWidth");
        var pitch = Token("ActionCardPitch");
        for (var i = 0; i < cards.Count - 1; i++)
        {
            var cut = new Border
            {
                Width = 18, Height = 18, CornerRadius = new CornerRadius(9),
                Background = BrushToken("CardGlassBrush"), BorderBrush = BrushToken("ConnectorBrush"), BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand, ToolTip = "Couper la pile ici",
                Tag = new CutRequest(section, cards[i].Id, cards[i + 1].Id),
                Child = new TextBlock { Text = "✂", FontSize = 11, Foreground = BrushToken("WidgetPrimaryTextBrush"),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false }
            };
            Canvas.SetLeft(cut, Math.Round(left + width / 2 - 9));
            Canvas.SetTop(cut, Math.Round(firstY + i * pitch + pitch / 2 - 9));
            Panel.SetZIndex(cut, int.MaxValue);
            OrbitCanvas.Children.Add(cut);
            _expandedElements.Add(cut);
        }
    }

    private bool TryCutAt(DependencyObject? hit)
    {
        for (; hit is not null && !ReferenceEquals(hit, OrbitCanvas); hit = VisualTreeHelper.GetParent(hit))
            if (hit is FrameworkElement { Tag: CutRequest cut }) { CutStack(cut); return true; }
        return false;
    }

    private void CutStack(CutRequest cut)
    {
        if (!_groupInfos.TryGetValue(cut.Section, out var group) || OpenCardButton(cut.Next) is not { } next) return;
        var layout = UiRuntime.Profile.Layout.Clone();
        PinMainStack(layout, cut.Section);
        var center = new Point(Canvas.GetLeft(next) + Token("ActionCardWidth") / 2, Canvas.GetTop(next) + Token("ActionCardHeight") / 2);
        var away = center.X < _branchNode.X ? -1 : 1;
        // La partie coupée s'écarte un peu, pour qu'on voie tout de suite qu'elle est libre.
        var x = Canvas.GetLeft(next) - _branchNode.X + away * 28;
        var y = center.Y - _branchNode.Y + 20;
        if (CardGroups.CutAfter(layout, cut.Section, group.Info.Actions.Select(action => action.Id).ToList(), cut.Card, x, y) is null) return;
        WheelLayoutProposed?.Invoke(layout);
    }

    // Une pile qui perd ou gagne des cartes ne doit pas se replacer toute seule : on fige sa place actuelle.
    private void PinMainStack(WheelLayoutData layout, string section)
    {
        if (!layout.Stacks.ContainsKey(section) && string.Equals(_activeGroup, section, StringComparison.OrdinalIgnoreCase))
            layout.Stacks[section] = new WheelStackPlacement(_branchLeft - _branchNode.X, _branchFirstY - _branchNode.Y, _branchSide);
    }

    private bool TryCardSlot(string id, out Guid card, out (string? Group, int Index) slot)
    {
        card = default; slot = default;
        return id.StartsWith("wheel.card:", StringComparison.Ordinal) && Guid.TryParse(id["wheel.card:".Length..], out card) &&
            _cardSlots.TryGetValue(card, out slot);
    }

    // Tête de la pile principale : elle déplace toute la pile (comportement d'origine, géré par CardStack).
    private bool IsMainStackHead(string id) =>
        !TryCardSlot(id, out _, out var slot) || slot is { Group: null, Index: 0 };

    // Glisser une carte : la tête d'un groupe déplace son groupe, toute autre carte se détache.
    // null = pas un glisser de groupe (pile principale, sous-carte) : l'éditeur garde son traitement habituel.
    public WheelLayoutData? CardDragLayout(string id, Vector delta, bool final, WheelLayoutData start)
    {
        if (_activeGroup is not { } section) return null;
        if (_cardDrag is null)
        {
            if (!TryCardSlot(id, out var card, out var slot) || slot is { Group: null, Index: 0 }) return null;
            if (OpenCardButton(card) is not { } button) return null;
            var offset = new Vector(Canvas.GetLeft(button) - _branchNode.X, Canvas.GetTop(button) + Token("ActionCardHeight") / 2 - _branchNode.Y);
            _cardDrag = (card, slot.Group, slot.Index, offset, null);
        }
        var drag = _cardDrag.Value;
        var layout = start.Clone();
        PinMainStack(layout, section);
        string moving;
        if (drag.Index == 0 && drag.Group is { } groupId && layout.CardGroups.TryGetValue(groupId, out var group))
        {
            group.X = drag.Offset.X + delta.X;
            group.Y = drag.Offset.Y + delta.Y;
            moving = groupId;
        }
        else moving = CardGroups.Detach(layout, section, drag.Card, drag.Offset.X + delta.X, drag.Offset.Y + delta.Y);
        _cardDrag = drag with { Moving = moving };
        var magnet = FindMagnet(layout, section, moving);
        ShowMagnet(final ? null : magnet?.Guide);
        if (final)
        {
            if (magnet is { } target) Stick(layout, moving, target.Target, target.Append);
            _cardDrag = null;
        }
        return layout;
    }

    // Recolle : à la fin d'un groupe, au début (le groupe remonte pour que ses cartes ne bougent pas), ou dans la pile principale.
    private static void Stick(WheelLayoutData layout, string moving, string? target, bool append)
    {
        if (target is not null && !append && layout.CardGroups.TryGetValue(moving, out var source) && layout.CardGroups.TryGetValue(target, out var below))
        {
            below.X = source.X;
            below.Y = source.Y;
        }
        CardGroups.Merge(layout, moving, target, append);
    }

    private (string? Target, bool Append, Rect Guide)? FindMagnet(WheelLayoutData layout, string section, string moving)
    {
        if (!_groupInfos.TryGetValue(section, out var info) || !layout.CardGroups.TryGetValue(moving, out var source)) return null;
        var width = Token("ActionCardWidth");
        var height = Token("ActionCardHeight");
        var pitch = Token("ActionCardPitch");
        var node = _branchNode;
        var roots = info.Info.Actions.Select(action => action.Id).ToList();
        var (main, extra) = CardGroups.Partition(layout, section, roots);
        var size = extra.FirstOrDefault(item => item.Id == moving).Cards?.Count ?? 0;
        if (size == 0) return null;
        var (left, firstY) = GroupPlacement(node, source, size);
        var targets = extra.Where(item => item.Id != moving).Select(item => ((string?)item.Id, GroupPlacement(node, item.Group, item.Cards.Count), item.Cards.Count)).ToList();
        if (layout.Stacks.TryGetValue(section, out var pinned))
            targets.Add((null, (Math.Round(node.X + pinned.X), node.Y + pinned.Y), main.Count));
        (string? Target, bool Append, Rect Guide)? best = null;
        var bestScore = double.MaxValue;
        foreach (var (target, (targetLeft, targetFirstY), count) in targets)
        {
            if (Math.Abs(left - targetLeft) > MagnetDistance) continue;
            // Une pile principale vide reçoit le groupe à sa place ; sinon on s'aimante en dessous ou au-dessus.
            var options = count == 0
                ? new[] { (Append: true, Y: targetFirstY) }
                : [(true, targetFirstY + count * pitch), (target is not null, targetFirstY - size * pitch)];
            foreach (var (append, y) in options)
            {
                if (!append && target is null) continue;
                var score = Math.Abs(firstY - y) + Math.Abs(left - targetLeft) / 2;
                if (Math.Abs(firstY - y) > pitch * 0.6 || score >= bestScore) continue;
                bestScore = score;
                var guideTop = append ? targetFirstY - height / 2 : y - height / 2;
                var guideCount = count + size;
                best = (target, append, new Rect(targetLeft - 4, guideTop - 4, width + 8, (guideCount - 1) * pitch + height + 8));
            }
        }
        return best;
    }

    // Contour rose de la pile obtenue si l'on relâche maintenant.
    private void ShowMagnet(Rect? guide)
    {
        EditOverlay.Children.OfType<FrameworkElement>().Where(item => item.Tag as string == "card-magnet").ToList().ForEach(EditOverlay.Children.Remove);
        if (guide is not { } rect) return;
        var outline = new System.Windows.Shapes.Rectangle
        {
            Width = rect.Width, Height = rect.Height, RadiusX = 10, RadiusY = 10, Tag = "card-magnet", IsHitTestVisible = false,
            Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0x3E, 0xA5)), StrokeThickness = 1.5, StrokeDashArray = [4, 3]
        };
        Canvas.SetLeft(outline, rect.Left);
        Canvas.SetTop(outline, rect.Top);
        EditOverlay.Children.Add(outline);
    }

    // Éléments déplacés par un glisser de carte : son groupe entier pour une tête, la carte seule sinon.
    private IEnumerable<FrameworkElement> CardDragElements(string id)
    {
        if (!TryCardSlot(id, out var card, out var slot)) return _expandedElements.OfType<Button>();
        if (slot.Index > 0) return _expandedElements.OfType<Button>().Where(item => item.Tag is ActionItem action && action.Id == card);
        return _expandedElements.OfType<Button>().Where(item => item.Tag is ActionItem action &&
            _cardSlots.TryGetValue(action.Id, out var other) && other.Group == slot.Group);
    }

    private bool ActiveSectionHasGroups() => _activeGroup is { } name &&
        UiRuntime.Profile.Layout.CardGroups.Values.Any(group => group.Section.Equals(name, StringComparison.OrdinalIgnoreCase));

    // Reconstruction immédiate de la branche ouverte, sans faire rétrécir la fenêtre pendant un geste.
    private void RebuildActiveBranch()
    {
        if (_activeGroup is not { } name || !_groupInfos.TryGetValue(name, out var group)) return;
        var instant = _instantMotion;
        _instantMotion = true;
        try
        {
            RestoreCoveredElements();
            foreach (var element in _expandedElements) OrbitCanvas.Children.Remove(element);
            _expandedElements.Clear();
            _openCards.Clear();
            _activeGroup = null;
            ExpandGroup(group.Info, group.Angle);
        }
        finally { _instantMotion = instant; }
    }

    // ---------- Ajouter une colonne ----------

    private sealed record ColumnRequest(string Section);

    // Place de la prochaine colonne de la branche ouverte : à côté de la plus extérieure, alignée en haut sur la pile principale.
    private (double Left, double FirstY)? NextColumnSpot(GroupInfo info)
    {
        if (!string.Equals(_activeGroup, info.Name, StringComparison.OrdinalIgnoreCase)) return null;
        var width = Token("ActionCardWidth");
        var height = Token("ActionCardHeight");
        var pitch = Token("ActionCardPitch");
        var (main, extra) = SplitBranch(info);
        var columns = new List<Rect>();
        if (main.Count > 0) columns.Add(new Rect(_branchLeft, _branchFirstY - height / 2, width, (main.Count - 1) * pitch + height));
        foreach (var (_, group, cards) in extra)
        {
            var (left, firstY) = GroupPlacement(_branchNode, group, cards.Count);
            columns.Add(new Rect(left, firstY - height / 2, width, (cards.Count - 1) * pitch + height));
        }
        if (columns.Count == 0) return null;
        return (CardGroups.NextColumnLeft(columns, _branchSide, width, 24), _branchFirstY);
    }

    // + en pointillé là où la prochaine colonne se posera.
    private void AddColumnPlusIfEditing(GroupInfo info)
    {
        if (!_wheelEditMode || NextColumnSpot(info) is not { } spot) return;
        var plus = PlusBadge(new ColumnRequest(info.Name), true, "Ajouter une colonne");
        plus.BorderThickness = new Thickness(1.5);
        Canvas.SetLeft(plus, Math.Round(spot.Left + Token("ActionCardWidth") / 2 - 12));
        Canvas.SetTop(plus, Math.Round(spot.FirstY - 12));
        Panel.SetZIndex(plus, int.MaxValue);
        OrbitCanvas.Children.Add(plus);
        _expandedElements.Add(plus);
    }

    // Une colonne neuve à côté des autres, avec un nouveau bouton dedans, sélectionné pour être rempli.
    private void AddColumn(string section)
    {
        if (_wheelEditor is not { } editor || !_groupInfos.TryGetValue(section, out var group)) return;
        if (!string.Equals(_activeGroup, group.Info.Name, StringComparison.OrdinalIgnoreCase)) ExpandGroup(group.Info, group.Angle);
        if (NextColumnSpot(group.Info) is not { } spot) return;
        var (list, id, error) = ActionEdits.NewButton(_actions, section);
        if (error is not null) { Notifications.Show("Impossible d'ajouter", error); return; }
        if (id is not { } added) return;
        var layout = UiRuntime.Profile.Layout.Clone();
        PinMainStack(layout, group.Info.Name);
        CardGroups.Detach(layout, group.Info.Name, added, spot.Left - _branchNode.X, spot.FirstY - _branchNode.Y);
        editor.ApplyActionsAndLayoutFromWheel(list, layout);
        Dispatcher.BeginInvoke(() => SelectCard(section, added, null), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private Rect ExtraGroupsBounds(GroupInfo info, Point node)
    {
        var result = Rect.Empty;
        var height = Token("ActionCardHeight");
        var pitch = Token("ActionCardPitch");
        foreach (var (_, group, cards) in SplitBranch(info).Extra)
        {
            var count = cards.Count;
            var (left, firstY) = GroupPlacement(node, group, count);
            result.Union(new Rect(left, firstY - height / 2, Token("ActionCardWidth"), (count - 1) * pitch + height));
        }
        return result;
    }
}
