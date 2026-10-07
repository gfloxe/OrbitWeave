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
    // Chemin ouvert : l'indice est le niveau de la carte parente (0 = carte de premier niveau).
    private readonly List<(ActionItem Parent, List<UIElement> Elements)> _openCards = [];
    private Guid? _quickHover;

    private Button CreateBranchCard(ActionItem action)
    {
        var card = OrbitCardFactory.Create(action, ActionTree.HasChildren(_actions, action.Id));
        card.Tag = action;
        card.Click += (_, _) => CardClicked(action, card);
        card.MouseEnter += (_, _) => CardHovered(action, card);
        return card;
    }

    private void CardClicked(ActionItem action, Button card)
    {
        if (ActionTree.HasChildren(_actions, action.Id) &&
            (_settings.SectionBehavior == "Au clic" || ActionTree.IsFolderOnly(action)))
        {
            ExpandCard(action, card);
            return;
        }
        Execute(action);
    }

    private void CardHovered(ActionItem action, Button card)
    {
        if (_wheelEditMode || _settings.SectionBehavior != "Au survol") return;
        if (ActionTree.HasChildren(_actions, action.Id)) ExpandCard(action, card);
        else CloseCardBranches(ActionTree.Depth(_actions, action));
    }

    private void ExpandCard(ActionItem parent, Button card)
    {
        var level = ActionTree.Depth(_actions, parent);
        if (level < _openCards.Count && _openCards[level].Parent.Id == parent.Id) return;
        CloseCardBranches(level);
        var children = ActionTree.Children(_actions, parent.Id).ToList();
        if (children.Count == 0) return;
        var width = Token("ActionCardWidth");
        var height = Token("ActionCardHeight");
        var pitch = Token("ActionCardPitch");
        var box = new BranchBox(Canvas.GetLeft(card), Canvas.GetTop(card),
            card.ActualWidth > 0 ? card.ActualWidth : width, card.ActualHeight > 0 ? card.ActualHeight : height);
        var screen = HasCustomViewport && _viewportReady ? _screenCanvasBounds : new Rect(30, 120, 1060, 880);
        var bounds = new BranchBox(screen.Left + 4, screen.Top + 4, screen.Width - 8, screen.Height - 8);
        var occupied = _expandedElements.OfType<Button>().Where(item => item.Tag is ActionItem && !ReferenceEquals(item, card))
            .Select(item => new BranchBox(Canvas.GetLeft(item), Canvas.GetTop(item), width, height)).ToList();
        // Côté opposé au nœud de la section, d'après les positions affichées (un profil peut décaler l'un ou l'autre) ;
        // une carte à peu près au-dessus ou au-dessous du nœud part à droite.
        var nodeX = _activeGroup is { } active && _groupButtons.TryGetValue(active, out var node) ? ShownCenterX(node) : _branchNode.X;
        var preferred = ShownCenterX(card) - nodeX < -box.Width / 2 ? -1 : 1;
        var plan = SubBranchLayout.Place(box, children.Count, width, height, pitch, Token("CardGap"), preferred, bounds, occupied);        var elements = new List<UIElement>();
        var stagger = Token("MotionCardStagger");
        var start = new Point(plan.Side > 0 ? box.Right : box.Left, box.CenterY);
        for (var i = 0; i < children.Count; i++)
        {
            var y = Math.Round(plan.FirstY + i * pitch);
            var end = new Point(plan.Side > 0 ? plan.Left : plan.Left + width, y);
            var bend = Math.Min(40, Math.Abs(end.X - start.X) / 2);
            var path = new System.Windows.Shapes.Path
            {
                Data = new PathGeometry([new PathFigure(start, [new BezierSegment(
                    new Point(start.X + plan.Side * bend, start.Y), new Point(end.X - plan.Side * bend, end.Y), end, true)], false)]),
                Stroke = BrushToken("ConnectorBrush"), StrokeThickness = Token("ConnectorThickness"), IsHitTestVisible = false
            }.With($"wheel.link:{children[i].Id}");
            Customize(path);
            Panel.SetZIndex(path, -1);
            var child = CreateBranchCard(children[i]);
            var left = Math.Round(plan.Left);
            var top = Math.Round(y - height / 2);
            Canvas.SetLeft(child, left);
            Canvas.SetTop(child, top);
            Customize(child);
            AddExpanded(path, i * stagger, plan.Side);
            AddExpanded(child, i * stagger, plan.Side);
            elements.Add(path);
            elements.Add(child);
            AddChildButtonIfEditing(children[i], left, top, plan.Side, elements);
        }
        _openCards.Add((parent, elements));
        // Agrandir seulement : refermer une sous-branche ne doit pas faire sauter la fenêtre.
        UpdateAdaptiveViewport(false);
        UpdateEditingBanner();
    }

    private void CloseCardBranches(int level)
    {
        for (var i = _openCards.Count - 1; i >= level; i--)
        {
            foreach (var element in _openCards[i].Elements)
            {
                _expandedElements.Remove(element);
                FadeOutExpanded(element, shrink: false);
            }
            _openCards.RemoveAt(i);
        }
    }

    private double ShownCenterX(FrameworkElement element)
    {
        var shown = element.TransformToAncestor(OrbitCanvas).TransformBounds(new Rect(element.RenderSize));
        return shown.Left + shown.Width / 2;
    }

    private Button? OpenCardButton(Guid id) =>
        _expandedElements.OfType<Button>().FirstOrDefault(item => item.Tag is ActionItem action && action.Id == id);

    // Lancement rapide : la capture empêche MouseEnter, on suit donc le pointeur ici.
    private void QuickSelectHover(Point point)
    {
        var hit = VisualTreeHelper.HitTest(OrbitCanvas, point)?.VisualHit as DependencyObject;
        while (hit is not null && hit is not Button { Tag: ActionItem }) hit = VisualTreeHelper.GetParent(hit);
        if (hit is not Button { Tag: ActionItem action } card || action.Id == _quickHover) return;
        _quickHover = action.Id;
        if (ActionTree.HasChildren(_actions, action.Id)) ExpandCard(action, card);
        else CloseCardBranches(ActionTree.Depth(_actions, action));
    }

    private void AddChildButtonIfEditing(ActionItem action, double left, double top, int side, List<UIElement>? elements) { }
}
