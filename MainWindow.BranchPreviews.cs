using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using OrbitWeave.Actions;
using OrbitWeave.Orbit;
using OrbitWeave.Ui;
using Button = System.Windows.Controls.Button;
using Panel = System.Windows.Controls.Panel;

namespace OrbitWeave;

public partial class MainWindow
{
    // Carte d'aperçu : sa section et le bouton qu'elle montre.
    private sealed record BranchPreview(string Section, Guid Card);

    private readonly List<UIElement> _branchPreviews = [];

    // « Ouvrir tout l'arbre » (clic droit du rond du milieu) : toutes les branches visibles en même temps,
    // jusqu'au repli de la roue ou jusqu'à « Refermer l'arbre ».
    private bool _treeOpen;

    public bool TreeOpen => _treeOpen;

    // Ouverture animée comme une branche : les cartes sortent de leur section l'une après l'autre.
    private bool _treeEntering;

    public void ToggleTree()
    {
        _treeOpen = !_treeOpen;
        if (DiagnosticFlags.TraceFrames) MeasureTree();
        Trace($"arbre {(_treeOpen ? "ouvert" : "refermé")}, replié={_folded}");
        if (_treeOpen)
        {
            _treeEntering = true;
            try
            {
                if (_folded) { _folded = false; RenderOrbit(); }
                else { DrawBranchPreviews(); UpdateAdaptiveViewport(true); }
            }
            finally { _treeEntering = false; }
            Trace($"arbre : {_branchPreviews.Count(e => e.HasAnimatedProperties)}/{_branchPreviews.Count} éléments animés, {Motion(Token("MotionCard")).TotalMilliseconds:F0} ms");
            return;
        }
        // Fermeture : les cartes s'effacent, la fenêtre se resserre à la fin.
        var leaving = _branchPreviews.ToList();
        _branchPreviews.Clear();
        foreach (var element in leaving) FadeOutExpanded(element, ReferenceEquals(element, leaving[^1]));
        DrawBranchPreviews();
        if (leaving.Count == 0) UpdateAdaptiveViewport(true);
    }

    // --trace-frames : fluidité de l'animation de l'arbre, dans perf.log.
    private void MeasureTree()
    {
        var meter = new FrameMeter(true);
        meter.Begin(_treeOpen ? "arbre/ouverture" : "arbre/fermeture");
        var stop = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        stop.Tick += (_, _) => { stop.Stop(); meter.End(); };
        stop.Start();
    }

    private void AnimateTreeEntry(UIElement element, double delay, int side, int vertical)
    {
        if (!_treeEntering) return;
        var duration = Motion(Token("MotionCard"));
        var begin = Motion(delay);
        var easing = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
        var target = element.Opacity;
        element.Opacity = 0;
        element.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, target, duration) { BeginTime = begin, EasingFunction = easing });
        if (element is not Button card) return;
        var slide = side == 0 ? new TranslateTransform(0, -vertical * 16) : new TranslateTransform(-side * 24, 0);
        card.RenderTransform = slide;
        var property = side == 0 ? TranslateTransform.YProperty : TranslateTransform.XProperty;
        slide.BeginAnimation(property, new System.Windows.Media.Animation.DoubleAnimation(side == 0 ? -vertical * 16 : -side * 24, 0, duration)
            { BeginTime = begin, EasingFunction = easing });
    }

    // Les branches de l'arbre sont de vraies cartes : un clic lance (ou ouvre les sous-cartes) comme dans la branche ouverte.
    private bool TreeMode => _treeOpen && !_wheelEditMode;

    // Mode Modifier : les branches des autres sections restent visibles, en aperçu, à la place où elles s'ouvriront.
    // Elles n'ont pas d'identifiant d'interface : l'éditeur, le dépôt et les recherches par identifiant ne les voient pas.
    private void DrawBranchPreviews()
    {
        var timing = System.Diagnostics.Stopwatch.StartNew();
        try { DrawBranchPreviewsCore(); }
        finally { if (_traceTimer is not null) Trace($"aperçus redessinés : {_branchPreviews.Count} éléments en {timing.Elapsed.TotalMilliseconds:F1} ms"); }
    }

    private void DrawBranchPreviewsCore()
    {
        foreach (var element in _branchPreviews) OrbitCanvas.Children.Remove(element);
        _branchPreviews.Clear();
        DrawRingPlus();
        if ((!_wheelEditMode && !_treeOpen) || _folded || _folding) return;
        foreach (var (info, _) in _groupInfos.Values)
        {
            if (string.Equals(info.Name, _activeGroup, StringComparison.OrdinalIgnoreCase)) continue;
            var (node, left, firstY, side, _, count, cardWidth, cardHeight, pitch) = PlanBranch(info);
            left = Math.Round(left);
            var (cards, extra) = SplitBranch(info);
            var stagger = Token("MotionCardStagger");
            var outward = Math.Sign(node.Y - WheelCenter.Y);
            for (var i = 0; i < count; i++)
            {
                var y = Math.Round(firstY + i * pitch);
                var delay = (side == 0 && outward < 0 ? count - 1 - i : i) * stagger;
                AddPreviewCard(info.Name, cards[i], left, Math.Round(y - cardHeight / 2), delay, side, outward);
                if (side == 0) continue;
                var start = new Point(node.X + side * OrbitLayout.SectionDiameter / 2, node.Y);
                var end = new Point(side < 0 ? left + cardWidth : left, y);
                var bend = Math.Min(40, Math.Max(0, side * (end.X - start.X)) / 2);
                AddPreviewLink(start, new Point(start.X + side * bend, start.Y), new Point(end.X - side * bend, end.Y), end, delay);
            }
            // Groupes de cartes détachés de la branche (ex. « Démarrage »), à leur place.
            foreach (var (_, group, groupCards) in extra)
            {
                var groupCount = groupCards.Count;
                var (groupLeft, groupFirstY) = GroupPlacement(node, group, groupCount);
                var groupSide = groupLeft + cardWidth / 2 < node.X ? -1 : 1;
                for (var i = 0; i < groupCount; i++)
                {
                    AddPreviewCard(info.Name, groupCards[i], groupLeft, Math.Round(Math.Round(groupFirstY + i * pitch) - cardHeight / 2),
                        i * stagger, groupSide, 0);
                    var (start, p1, p2, end, linked) = GroupLink(node, groupLeft, groupFirstY, groupCount, i);
                    if (linked) AddPreviewLink(start, p1, p2, end, i * stagger);
                }
            }
        }
    }

    private const double PreviewOpacity = 0.45;

    private void AddPreviewCard(string section, ActionItem action, double left, double top, double delay = 0, int side = 0, int vertical = 0)
    {
        var card = OrbitCardFactory.Create(action, ActionTree.HasChildren(_actions, action.Id));
        Customize(card);
        ForgetIds(card);
        card.Tag = new BranchPreview(section, action.Id);
        card.Cursor = System.Windows.Input.Cursors.Hand;
        card.Opacity = TreeMode ? 1 : PreviewOpacity; // la fabrique crée les cartes invisibles, pour leur animation d'entrée
        if (TreeMode)
        {
            card.Click += (_, _) =>
            {
                if (_groupInfos.ContainsKey(section) && ActivatePreview(new BranchPreview(section, action.Id)) is Button real)
                    real.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, real));
            };
        }
        else
        {
            card.ToolTip = "Clic : modifier cette branche";
        }
        Canvas.SetLeft(card, left);
        Canvas.SetTop(card, top);
        Panel.SetZIndex(card, -3);
        AddPreview(card);
        AnimateTreeEntry(card, delay, side, vertical);
    }

    private void AddPreviewLink(Point start, Point p1, Point p2, Point end, double delay = 0)
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = new PathGeometry([new PathFigure(start, [new BezierSegment(p1, p2, end, true)], false)]),
            Stroke = BrushToken("ConnectorBrush"), StrokeThickness = Token("ConnectorThickness"),
            IsHitTestVisible = false, Opacity = TreeMode ? 1 : PreviewOpacity
        };
        Panel.SetZIndex(path, -4);
        AddPreview(path);
        AnimateTreeEntry(path, delay, 0, 0);
    }

    private void AddPreview(UIElement element)
    {
        OrbitCanvas.Children.Add(element);
        _branchPreviews.Add(element);
    }

    private static void ForgetIds(DependencyObject node)
    {
        node.ClearValue(UiId.IdProperty);
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) ForgetIds(child);
    }

    private static BranchPreview? PreviewAt(DependencyObject? node)
    {
        for (; node is not null; node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (node is FrameworkElement { Tag: BranchPreview preview }) return preview;
        return null;
    }

    // Clic sur un aperçu : sa branche devient la branche active ; renvoie la vraie carte, à sélectionner.
    private FrameworkElement? ActivatePreview(BranchPreview preview)
    {
        if (!_groupInfos.TryGetValue(preview.Section, out var group)) return null;
        var instant = _instantMotion;
        _instantMotion = true;
        try { ExpandGroup(group.Info, group.Angle); }
        finally { _instantMotion = instant; }
        return _expandedElements.OfType<Button>().FirstOrDefault(item => item.Tag is ActionItem action && action.Id == preview.Card);
    }
}
