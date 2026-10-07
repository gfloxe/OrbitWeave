using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using OrbitWeave.Orbit;
using OrbitWeave.Ui;
using Button = System.Windows.Controls.Button;
using Panel = System.Windows.Controls.Panel;

namespace OrbitWeave;

public partial class MainWindow
{
    private double _branchLeft;
    private double _branchFirstY;
    private int _branchSide;
    private Point _branchNode;
    internal Border? _hubHit;
    internal readonly Dictionary<string, WheelPoint?> _appliedLabelAttachments = new(StringComparer.OrdinalIgnoreCase);
    public bool WheelGestureActive => _dragId is not null || _transformId is not null;
    private int _dragSelectionRedraws;
    public IReadOnlyList<string> WheelSectionNames => _wheelGeometry.Sections.Select(item => item.Name).ToArray();

    public (string Section, WheelStackPlacement Placement)? CardStack(string id)
    {
        if (_activeGroup is not { } name || !(id == "wheel.card" ||
            _expandedElements.OfType<Button>().Any(item => UiId.Get(item) == id))) return null;
        return (name, new WheelStackPlacement(_branchLeft - _branchNode.X, _branchFirstY - _branchNode.Y, _branchSide));
    }

    public WheelPoint? WheelPosition(string id)
    {
        if (id == "wheel.hub") return _wheelGeometry.HubCenter;
        if (!id.StartsWith("wheel.section:", StringComparison.Ordinal)) return null;
        var name = id["wheel.section:".Length..];
        var section = _wheelGeometry.Sections.FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return section.Name is null ? null : section.Center;
    }

    public WheelPoint LimitSectionPosition(WheelPoint point) => _viewportReady
        ? WheelLayout.LimitSection(point, _wheelGeometry.HubCenter, OrbitLayout.SectionDiameter, OrbitLayout.HubDiameter,
            _screenCanvasBounds.Left, _screenCanvasBounds.Top, _screenCanvasBounds.Right,
            _screenCanvasBounds.Bottom - Token("SectionLabelHeight") - 4)
        : WheelLayout.LimitSection(point, _wheelGeometry.HubCenter, OrbitLayout.SectionDiameter, OrbitLayout.HubDiameter);

    // Le glisser ne reconstruit pas la toile : seuls positions et traits existants sont mis à jour.
    public void ApplyWheelLayout()
    {
        var started = Stopwatch.GetTimestamp();
        var previous = _wheelGeometry;
        var groups = _groupInfos.Values.Select(value => value.Info).ToArray();
        _wheelGeometry = CalculateWheelGeometry(groups);
        var geometry = _wheelGeometry;
        Place("wheel.hub", geometry.HubTopLeft);
        if (_hubHit is not null)
        {
            Canvas.SetLeft(_hubHit, geometry.HubTopLeft.X);
            Canvas.SetTop(_hubHit, geometry.HubTopLeft.Y);
        }
        Place("wheel.hub.disk", new WheelPoint(geometry.HubCenter.X - Token("HubDiskDiameter") / 2,
            geometry.HubCenter.Y - Token("HubDiskDiameter") / 2));
        Place("wheel.halo", geometry.HaloTopLeft);
        Place("wheel.search", geometry.SearchTopLeft);
        foreach (var label in _groupLabels.Values)
            if (label.Child is StackPanel text)
                foreach (var child in text.Children.OfType<FrameworkElement>())
                {
                    if (UiId.Get(child) is not { } id) continue;
                    WheelPoint? value = UiRuntime.Profile.Layout.Attachments.TryGetValue(id, out var attachment) ? attachment : null;
                    if (_appliedLabelAttachments.GetValueOrDefault(id) == value) continue;
                    UiOverrides.Apply(child, UiRuntime.Profile);
                    _appliedLabelAttachments[id] = value;
                }
        foreach (var item in UiRuntime.Profile.CustomElements.Values.Where(item => item.Parent == "wheel" && item.AttachTo.Length > 0))
            Place(item.Id, UiCustomElements.Anchor(item.AttachTo, geometry) + new WheelPoint(item.X, item.Y) +
                UiRuntime.Profile.Layout.Attachments.GetValueOrDefault(item.Id));
        var movingSelection = _dragId is not null && (_dragId.StartsWith("wheel.section:", StringComparison.Ordinal) || _dragId == "wheel.hub") && _editSelection == _dragId;
        var selectionDelta = _dragId == "wheel.hub" ? new Vector(geometry.HubTopLeft.X - previous.HubTopLeft.X, geometry.HubTopLeft.Y - previous.HubTopLeft.Y) : new Vector();
        foreach (var section in geometry.Sections)
        {
            var name = section.Name;
            var before = previous.Sections.FirstOrDefault(item => item.Name == name);
            if (before == section) continue;
            if (movingSelection && _dragId == $"wheel.section:{name}")
                selectionDelta = new Vector(section.NodeTopLeft.X - before.NodeTopLeft.X, section.NodeTopLeft.Y - before.NodeTopLeft.Y);
            if (_groupButtons.TryGetValue(name, out var button))
            {
                Canvas.SetLeft(button, section.NodeTopLeft.X);
                Canvas.SetTop(button, section.NodeTopLeft.Y);
            }
            if (_groupLabels.TryGetValue(name, out var label))
            {
                if (_wheelEditMode && label.CacheMode is null) label.CacheMode = new BitmapCache { RenderAtScale = 1 };
                // Même mesure que le rendu initial et le sous-titre vivant, marges comprises.
                var width = label.DesiredSize.Width;
                Canvas.SetLeft(label, Math.Round(section.LabelAnchor.X - width / 2));
                Canvas.SetTop(label, Math.Round(section.LabelAnchor.Y));
            }
            if (_spokes.TryGetValue(name, out var spoke))
            {
                if (spoke.HasAnimatedProperties)
                {
                    spoke.BeginAnimation(Line.X2Property, null);
                    spoke.BeginAnimation(Line.Y2Property, null);
                }
                spoke.X1 = section.SpokeStart.X;
                spoke.Y1 = section.SpokeStart.Y;
                spoke.X2 = section.SpokeEnd.X;
                spoke.Y2 = section.SpokeEnd.Y;
            }
            var badge = OrbitCanvas.Children.OfType<FrameworkElement>().FirstOrDefault(item => UiId.Get(item) == $"wheel.badge:{name}");
            if (badge is not null)
            {
                Canvas.SetLeft(badge, Math.Round(section.BadgeAnchor.X - badge.DesiredSize.Width / 2));
                Canvas.SetTop(badge, Math.Round(section.BadgeAnchor.Y));
            }
            if (_groupInfos.TryGetValue(name, out var old))
                _groupInfos[name] = (old.Info with { Position = section.Center, Radius = section.Radius }, section.Angle);
            for (var i = 0; i < _liveLabels.Count; i++)
                if (ReferenceEquals(_liveLabels[i].Label, label))
                {
                    var live = _liveLabels[i];
                    _liveLabels[i] = (live.Type, live.Label, live.Subtitle, section.LabelAnchor.X);
                }
        }
        FollowActiveBranch();
        DrawRingPlus();
        UpdateAdaptiveViewport(!WheelGestureActive);
        UpdateEditingBanner();
        if (movingSelection)
        {
            foreach (var item in EditOverlay.Children.OfType<FrameworkElement>().Where(item => item is not Line && item.Tag as string is not ("layout-guide" or "screen-edge")))
            {
                Canvas.SetLeft(item, Canvas.GetLeft(item) + selectionDelta.X);
                Canvas.SetTop(item, Canvas.GetTop(item) + selectionDelta.Y);
            }
        }
        else DrawEditSelection();
        if (_dragMeter is not null) _layoutUpdateTimes.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        if (_dragId is null) CompleteWheelLayoutMove();
    }

    private void Place(string id, WheelPoint point)
    {
        var element = OrbitCanvas.Children.OfType<FrameworkElement>().FirstOrDefault(item => UiId.Get(item) == id);
        if (element is null) return;
        if (_wheelEditMode && element.CacheMode is null) element.CacheMode = new BitmapCache { RenderAtScale = 1 };
        Canvas.SetLeft(element, point.X);
        Canvas.SetTop(element, point.Y);
    }

    public void CompleteWheelLayoutMove()
    {
        ForgetPlannedBranches();
        DrawRingPlus();
        UpdateAdaptiveViewport(true);
        if (_activeGroup is not { } name || !_groupInfos.TryGetValue(name, out var group)) return;
        var instant = _instantMotion;
        _instantMotion = true;
        try { CollapseGroup(); ExpandGroup(group.Info, group.Angle); }
        finally { _instantMotion = instant; }
        DrawEditSelection();
    }

    private void FollowActiveBranch()
    {
        if (_activeGroup is not { } name || !_groupInfos.TryGetValue(name, out var group)) return;
        // Plusieurs groupes : chacun a sa propre place, on reconstruit la branche d'un coup.
        if (ActiveSectionHasGroups() || _cardDrag is not null) { RebuildActiveBranch(); return; }
        var node = ToPoint(group.Info.Position!.Value);
        var delta = node - _branchNode;
        var hasPinned = UiRuntime.Profile.Layout.Stacks.TryGetValue(name, out var pinned);
        if (delta.Length < 0.000001 && (!hasPinned ||
            Math.Abs(_branchLeft - node.X - pinned.X) < 0.000001 &&
            Math.Abs(_branchFirstY - node.Y - pinned.Y) < 0.000001 && _branchSide == pinned.Side)) return;
        if (hasPinned)
        {
            _branchLeft = node.X + pinned.X;
            _branchFirstY = node.Y + pinned.Y;
            _branchSide = pinned.Side;
        }
        else
        {
            _branchLeft += delta.X;
            _branchFirstY += delta.Y;
        }
        _branchNode = node;
        var width = Token("ActionCardWidth");
        var height = Token("ActionCardHeight");
        var pitch = Token("ActionCardPitch");
        // Les sous-branches ouvertes se referment : seule la pile principale suit la section.
        CloseCardBranches(0);
        var cards = _expandedElements.OfType<Button>().Where(item => item.Tag is ActionItem action && _cardSlots.ContainsKey(action.Id)).ToArray();
        if (HasCustomViewport && _viewportReady && cards.Length > 0)
        {
            var column = AdaptiveViewport.KeepInside(new Rect(_branchLeft, _branchFirstY - height / 2, width,
                (cards.Length - 1) * pitch + height), _screenCanvasBounds);
            _branchLeft = column.X; _branchFirstY = column.Y + height / 2;
        }
        for (var i = 0; i < cards.Length; i++)
        {
            var y = Math.Round(_branchFirstY + i * pitch);
            Canvas.SetLeft(cards[i], Math.Round(_branchLeft));
            Canvas.SetTop(cards[i], Math.Round(y - height / 2));
            if (cards[i].Tag is not ActionItem action) continue;
            var path = _expandedElements.OfType<System.Windows.Shapes.Path>().FirstOrDefault(item => UiId.Get(item) == $"wheel.link:{action.Id}");
            if (path is null) continue;
            var start = new Point(node.X + _branchSide * OrbitLayout.SectionDiameter / 2, node.Y);
            var end = new Point(_branchSide < 0 ? _branchLeft + width : _branchLeft, y);
            var bend = Math.Min(40, Math.Max(0, _branchSide * (end.X - start.X)) / 2);
            path.Data = new PathGeometry([new PathFigure(start,
                [new BezierSegment(new Point(start.X + _branchSide * bend, start.Y),
                    new Point(end.X - _branchSide * bend, end.Y), end, true)], false)]);
        }
        var cuts = _expandedElements.OfType<Border>().Where(item => item.Tag is CutRequest).ToArray();
        for (var i = 0; i < cuts.Length; i++)
        {
            Canvas.SetLeft(cuts[i], Math.Round(_branchLeft + width / 2 - 9));
            Canvas.SetTop(cuts[i], Math.Round(_branchFirstY + i * pitch + pitch / 2 - 9));
        }
        if (_branchSide == 0 && _expandedElements.OfType<Line>().FirstOrDefault() is { } link)
        {
            var outward = Math.Sign(node.Y - WheelCenter.Y);
            link.X1 = Math.Round(node.X) + 0.5;
            link.Y1 = outward > 0 ? node.Y + OrbitLayout.SectionDiameter / 2 + 4 + Token("SectionLabelHeight") : node.Y - OrbitLayout.SectionDiameter / 2;
            link.X2 = Math.Round(_branchLeft + width / 2) + 0.5;
            link.Y2 = outward > 0 ? _branchFirstY - height / 2 : _branchFirstY + (cards.Length - 1) * pitch + height / 2;
        }
    }

    private Vector SnapSection(Vector delta)
    {
        var center = WheelCenter;
        var moved = _dragCenter + delta;
        var modifiers = Keyboard.Modifiers;
        moved = ToPoint(WheelLayout.ProjectDrag(new WheelPoint(moved.X, moved.Y),
            new WheelPoint(_dragCenter.X, _dragCenter.Y), _wheelGeometry.HubCenter,
            modifiers.HasFlag(ModifierKeys.Shift), !modifiers.HasFlag(ModifierKeys.Shift) && modifiers.HasFlag(ModifierKeys.Control), 0));
        var proposed = moved - center;
        var radius = proposed.Length;
        var angle = Math.Atan2(proposed.Y, proposed.X);
        var magnetic = !modifiers.HasFlag(ModifierKeys.Alt);
        if (magnetic)
        {
            if (!modifiers.HasFlag(ModifierKeys.Shift))
                foreach (var orbit in new[] { Token("InnerOrbitRadius"), Token("OuterOrbitRadius") })
                    if (Math.Abs(radius - orbit) < 6) radius = orbit;
            if (!modifiers.HasFlag(ModifierKeys.Control))
                foreach (var section in _wheelGeometry.Sections)
                {
                    var regular = OrbitLayout.AngleFor(section.Index, _wheelGeometry.Sections.Count);
                    var gap = Math.Atan2(Math.Sin(angle - regular), Math.Cos(angle - regular));
                    if (Math.Abs(gap * radius) < 6) { angle = regular; break; }
                }
        }
        moved = new Point(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius);
        if (magnetic && !modifiers.HasFlag(ModifierKeys.Shift) && !modifiers.HasFlag(ModifierKeys.Control))
            foreach (var anchor in _wheelGeometry.Sections.Where(section => $"wheel.section:{section.Name}" != _dragId)
                .Select(section => ToPoint(section.Center)).Prepend(center))
            {
                if (Math.Abs(moved.X - anchor.X) < 6) moved.X = anchor.X;
                if (Math.Abs(moved.Y - anchor.Y) < 6) moved.Y = anchor.Y;
            }
        moved = ToPoint(LimitSectionPosition(new WheelPoint(moved.X, moved.Y)));
        EditOverlay.Children.OfType<Line>().ToList().ForEach(EditOverlay.Children.Remove);
        EditOverlay.Children.OfType<Ellipse>().Where(item => item.Tag as string == "layout-guide").ToList().ForEach(EditOverlay.Children.Remove);
        if (magnetic)
        {
            var brush = new SolidColorBrush(Color.FromRgb(0xFF, 0x3E, 0xA5));
            EditOverlay.Children.Add(new Line { X1 = center.X, Y1 = center.Y, X2 = moved.X, Y2 = moved.Y,
                Stroke = brush, StrokeThickness = 1, StrokeDashArray = [3, 3], IsHitTestVisible = false });
            foreach (var orbit in new[] { Token("InnerOrbitRadius"), Token("OuterOrbitRadius") })
                if (Math.Abs((moved - center).Length - orbit) < 0.5)
                {
                    var guide = new Ellipse { Width = orbit * 2, Height = orbit * 2, Stroke = brush,
                        StrokeThickness = 1, StrokeDashArray = [3, 3], Tag = "layout-guide", IsHitTestVisible = false };
                    Canvas.SetLeft(guide, center.X - orbit); Canvas.SetTop(guide, center.Y - orbit);
                    EditOverlay.Children.Add(guide);
                }
        }
        return moved - _dragCenter;
    }
}

