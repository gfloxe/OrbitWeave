using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using OrbitWeave.Orbit;
using OrbitWeave.Ui;
using Size = System.Windows.Size;

namespace OrbitWeave;

public partial class MainWindow
{
    private bool _viewportReady;
    private bool _adaptingViewport;
    private Point _canvasScreenOrigin;
    private Rect _screenCanvasBounds;
    private Rect _currentViewport;
    private double _viewportScale = 1;
    private double _originalViewportScale = 1;
    private Point _originalCanvasScreenOrigin;
    private System.Drawing.Rectangle _viewportWorkPixels;
    private static readonly HashSet<string> GeometryProperties = ["OffsetX", "OffsetY", "Width", "Height", "Rotation", "Scale"];
    private bool HasCustomViewport => !UiRuntime.Profile.Layout.IsEmpty ||
        UiRuntime.Profile.CustomElements.Values.Any(item => item.Parent == "wheel") ||
        UiRuntime.Profile.Elements.Any(pair => pair.Key.StartsWith("wheel") && pair.Value.Keys.Any(GeometryProperties.Contains)) ||
        UiRuntime.Profile.Tokens.Keys.Any(key => key.Contains("Radius") || key.Contains("Diameter"));

    private void PrepareAdaptiveViewport()
    {
        UpdateLayout();
        _viewportScale = (OrbitCanvas.RenderTransform as ScaleTransform)?.ScaleX ?? 1;
        _canvasScreenOrigin = OrbitCanvas.PointToScreen(new Point());
        _originalCanvasScreenOrigin = _canvasScreenOrigin;
        _originalViewportScale = _viewportScale;
        _viewportWorkPixels = System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle).WorkingArea;
        _currentViewport = new Rect(OrbitLayout.ViewportOffsetX, OrbitLayout.ViewportOffsetY,
            OrbitLayout.ViewportWidth, OrbitLayout.ViewportHeight);
        RefreshScreenCanvasBounds();
        _viewportReady = true;
        if (!HasCustomViewport) return;
        var content = FinalContentBounds();
        var dpi = VisualTreeHelper.GetDpi(this);
        var work = _viewportWorkPixels;
        var scale = AdaptiveViewport.ScaleToFit(content, new Rect(0, 0, work.Width / dpi.DpiScaleX,
            work.Height / dpi.DpiScaleY), _viewportScale, true);
        if (Math.Abs(scale - _viewportScale) > 0.0001)
        {
            // Le centre écran du contenu reste stable quand une disposition sauvegardée exige une réduction.
            var anchor = new Point(_canvasScreenOrigin.X + content.X * _viewportScale * dpi.DpiScaleX,
                _canvasScreenOrigin.Y + content.Y * _viewportScale * dpi.DpiScaleY);
            _viewportScale = scale;
            _canvasScreenOrigin = new Point(anchor.X - content.X * scale * dpi.DpiScaleX,
                anchor.Y - content.Y * scale * dpi.DpiScaleY);
            OrbitCanvas.RenderTransform = new ScaleTransform(scale, scale);
            EditOverlay.RenderTransform = OrbitCanvas.RenderTransform;
            RefreshScreenCanvasBounds();
        }
        var contentPixels = new Rect(_canvasScreenOrigin.X + content.X * scale * dpi.DpiScaleX,
            _canvasScreenOrigin.Y + content.Y * scale * dpi.DpiScaleY,
            content.Width * scale * dpi.DpiScaleX, content.Height * scale * dpi.DpiScaleY);
        var constrained = AdaptiveViewport.KeepInside(contentPixels, new Rect(work.Left, work.Top, work.Width, work.Height));
        _canvasScreenOrigin += constrained.TopLeft - contentPixels.TopLeft;
        RefreshScreenCanvasBounds();
        UpdateAdaptiveViewport(true);
    }

    private void RefreshScreenCanvasBounds()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var work = _viewportWorkPixels;
        _screenCanvasBounds = new Rect((work.Left - _canvasScreenOrigin.X) / (_viewportScale * dpi.DpiScaleX),
            (work.Top - _canvasScreenOrigin.Y) / (_viewportScale * dpi.DpiScaleY),
            work.Width / (_viewportScale * dpi.DpiScaleX), work.Height / (_viewportScale * dpi.DpiScaleY));
    }

    private Rect FinalContentBounds()
    {
        var result = Rect.Empty;
        foreach (var element in OrbitCanvas.Children.OfType<FrameworkElement>())
        {
            if (element.Visibility != Visibility.Visible || element is Line) continue;
            var bounds = FinalElementBounds(element);
            if (!bounds.IsEmpty) result.Union(bounds);
        }
        return result;
    }

    private Rect FinalElementBounds(FrameworkElement element)
    {
        var x = double.IsNaN(Canvas.GetLeft(element)) ? 0 : Canvas.GetLeft(element);
        var y = double.IsNaN(Canvas.GetTop(element)) ? 0 : Canvas.GetTop(element);
        if (element is System.Windows.Shapes.Path)
        {
            var pathBounds = VisualTreeHelper.GetDescendantBounds(element);
            if (!pathBounds.IsEmpty) pathBounds.Offset(x, y);
            return pathBounds;
        }
        if (!element.IsMeasureValid) element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return new Rect(x, y, element.DesiredSize.Width, element.DesiredSize.Height);
    }

    private void UpdateAdaptiveViewport(bool shrink = false)
    {
        if (!_viewportReady || _adaptingViewport) return;
        if (!HasCustomViewport)
        {
            var unchanged = _currentViewport == new Rect(30, 120, 1060, 880) && _viewportScale == _originalViewportScale;
            _viewportScale = _originalViewportScale;
            _canvasScreenOrigin = _originalCanvasScreenOrigin;
            if (unchanged) return;
            OrbitCanvas.RenderTransform = new ScaleTransform(_viewportScale, _viewportScale);
            EditOverlay.RenderTransform = OrbitCanvas.RenderTransform;
            RefreshScreenCanvasBounds();
            ApplyViewportRect(new Rect(30, 120, 1060, 880));
            return;
        }
        var content = FinalContentBounds();
        Stage("vp-contenu");
        var dpi = VisualTreeHelper.GetDpi(this);
        var desiredScale = AdaptiveViewport.ScaleToFit(content, new Rect(0, 0,
            _viewportWorkPixels.Width / dpi.DpiScaleX, _viewportWorkPixels.Height / dpi.DpiScaleY), _viewportScale, true);
        var rescaled = desiredScale < _viewportScale - 0.0001;
        if (rescaled)
        {
            var center = new Point(content.X + content.Width / 2, content.Y + content.Height / 2);
            _canvasScreenOrigin += new Vector(center.X * (_viewportScale - desiredScale) * dpi.DpiScaleX,
                center.Y * (_viewportScale - desiredScale) * dpi.DpiScaleY);
            _viewportScale = desiredScale;
            OrbitCanvas.RenderTransform = new ScaleTransform(desiredScale, desiredScale);
            EditOverlay.RenderTransform = OrbitCanvas.RenderTransform;
            var pixels = new Rect(_canvasScreenOrigin.X + content.X * desiredScale * dpi.DpiScaleX,
                _canvasScreenOrigin.Y + content.Y * desiredScale * dpi.DpiScaleY,
                content.Width * desiredScale * dpi.DpiScaleX, content.Height * desiredScale * dpi.DpiScaleY);
            var work = _viewportWorkPixels;
            var inside = AdaptiveViewport.KeepInside(pixels, new Rect(work.Left, work.Top, work.Width, work.Height));
            _canvasScreenOrigin += inside.TopLeft - pixels.TopLeft;
            RefreshScreenCanvasBounds();
        }
        // La fenêtre réserve dès l'ouverture la place de toutes les branches : la redimensionner à
        // chaque section survolée se faisait en quatre étapes et la roue sautait pendant 1 à 3 images.
        content.Union(PlannedBranchesBounds());
        Stage("vp-prévu");
        var target = AdaptiveViewport.Fit(content, _screenCanvasBounds, _currentViewport, true);
        if (target.IsEmpty) return;
        if (!shrink) target.Union(_currentViewport);
        target.Intersect(_screenCanvasBounds);
        // Pixels entiers : un arrondi de carte ne doit pas redimensionner la fenêtre d'1 px.
        target = new Rect(new Point(Math.Floor(target.Left), Math.Floor(target.Top)),
            new Point(Math.Ceiling(target.Right), Math.Ceiling(target.Bottom)));
        if (target == _currentViewport && !rescaled) return;
        ApplyViewportRect(target);
        Stage("vp-appliquer");
    }

    private Rect _plannedBranches = Rect.Empty;
    private Rect _plannedBranchesScreen = Rect.Empty;
    private bool _plannedBranchesEditing;

    private void ForgetPlannedBranches() => _plannedBranches = Rect.Empty;

    // Cartes de toutes les sections, à la place où ExpandGroup les posera.
    // Mode Modifier : plus la place des « + » (colonne à côté, bouton sous la pile). Sans elle, ouvrir une branche
    // agrandissait la grande fenêtre transparente, puis le fondu de l'ancienne la resserrait : 55 à 65 ms à chaque fois.
    private Rect PlannedBranchesBounds()
    {
        if (!_plannedBranches.IsEmpty && _plannedBranchesScreen == _screenCanvasBounds && _plannedBranchesEditing == _wheelEditMode)
            return _plannedBranches;
        var result = Rect.Empty;
        foreach (var (info, _) in _groupInfos.Values)
        {
            var (node, left, firstY, side, _, count, cardWidth, cardHeight, pitch) = PlanBranch(info);
            var branch = Rect.Empty;
            if (count > 0) branch.Union(new Rect(left, firstY - cardHeight / 2, cardWidth, (count - 1) * pitch + cardHeight));
            branch.Union(ExtraGroupsBounds(info, node));
            if (_wheelEditMode && !branch.IsEmpty)
            {
                var room = cardWidth + 24;
                branch = new Rect(side < 0 ? branch.Left - room : branch.Left, branch.Top - 40,
                    branch.Width + room, branch.Height + 80);
            }
            result.Union(branch);
        }
        _plannedBranches = result;
        _plannedBranchesScreen = _screenCanvasBounds;
        _plannedBranchesEditing = _wheelEditMode;
        return result;
    }

    private void ApplyViewportRect(Rect rectangle)
    {
        _adaptingViewport = true;
        try
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var screenPoint = new Point(_canvasScreenOrigin.X + rectangle.X * _viewportScale * dpi.DpiScaleX,
                _canvasScreenOrigin.Y + rectangle.Y * _viewportScale * dpi.DpiScaleY);
            var local = PointFromScreen(screenPoint);
            Left += local.X;
            Top += local.Y;
            Width = rectangle.Width * _viewportScale;
            Height = rectangle.Height * _viewportScale;
            OrbitCanvas.Margin = EditOverlay.Margin = new Thickness(-rectangle.Left * _viewportScale,
                -rectangle.Top * _viewportScale, 0, 0);
            _currentViewport = rectangle;
            if (DiagnosticFlags.Enabled)
                File.AppendAllText(System.IO.Path.Combine(DiagnosticFlags.DirectoryPath, "viewport.log"),
                    $"rect={rectangle.ToString(CultureInfo.InvariantCulture)}; scale={_viewportScale:F3}{Environment.NewLine}");
        }
        finally { _adaptingViewport = false; }
    }

    public Vector LimitWheelTranslation(string id, Vector delta)
    {
        if (!_viewportReady) return delta;
        if (id.StartsWith("editor.group:", StringComparison.Ordinal))
        {
            var parts = id.Split(':', 3);
            var member = UiRuntime.Profile.CustomElements.Values.FirstOrDefault(item => item.Parent == parts[1] && item.Group == parts[2]);
            if (member is not null) return LimitWheelTranslation(member.Id, delta);
        }
        var bounds = Rect.Empty;
        IEnumerable<FrameworkElement> elements = CardStack(id) is not null ? CardDragElements(id) : WheelElements(id);
        if (id == "wheel.hub")
            elements = elements.Concat(WheelElements("wheel.halo")).Concat(WheelElements("wheel.search"));
        if (id.StartsWith("wheel.section:", StringComparison.Ordinal))
        {
            var name = id["wheel.section:".Length..];
            elements = elements.Concat(OrbitCanvas.Children.OfType<FrameworkElement>().Where(item =>
                UiId.Get(item) == $"wheel.label:{name}" || UiId.Get(item) == $"wheel.badge:{name}"));
            var attachedIds = UiRuntime.Profile.CustomElements.Values.Where(item => item.Parent == "wheel" && item.AttachTo == id).Select(item => item.Id).ToHashSet();
            elements = elements.Concat(OrbitCanvas.Children.OfType<FrameworkElement>().Where(item => attachedIds.Contains(UiId.Get(item) ?? "")));
        }
        if (id == "wheel.hub" && (System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift) || DiagnosticFlags.LayoutDragWholeWheel))
            elements = OrbitCanvas.Children.OfType<FrameworkElement>().Where(item => item is not Line);
        if (UiRuntime.Profile.CustomElements.TryGetValue(id, out var custom) && custom.Group.Length > 0)
        {
            var ids = UiRuntime.Profile.CustomElements.Values.Where(item => item.Parent == custom.Parent && item.Group == custom.Group).Select(item => item.Id).ToHashSet();
            elements = OrbitCanvas.Children.OfType<FrameworkElement>().Where(item => ids.Contains(UiId.Get(item) ?? ""));
        }
        foreach (var element in elements)
        {
            if (!element.IsVisible) continue;
            if (ReferenceEquals(VisualTreeHelper.GetParent(element), OrbitCanvas)) bounds.Union(FinalElementBounds(element));
            else if (CanvasBounds(element) is { } rectangle) bounds.Union(rectangle);
        }
        var limited = bounds.IsEmpty ? delta : AdaptiveViewport.LimitTranslation(bounds, _screenCanvasBounds, delta);
        EditOverlay.Children.OfType<FrameworkElement>().Where(item => item.Tag as string == "screen-edge").ToList().ForEach(EditOverlay.Children.Remove);
        if (_wheelEditMode && limited != delta)
        {
            var guide = new System.Windows.Shapes.Rectangle { Width = _screenCanvasBounds.Width - 2, Height = _screenCanvasBounds.Height - 2,
                Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0x3E, 0xA5)), StrokeThickness = 2,
                Tag = "screen-edge", IsHitTestVisible = false };
            Canvas.SetLeft(guide, _screenCanvasBounds.Left + 1); Canvas.SetTop(guide, _screenCanvasBounds.Top + 1);
            EditOverlay.Children.Add(guide);
        }
        return limited;
    }

    private bool IsWheelHit(Point local)
    {
        for (var node = InputHitTest(local) as DependencyObject; node is not null;
            node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
        {
            if (_wheelEditMode && ReferenceEquals(node, EditOverlay)) return true;
            if (node is FrameworkElement { Tag: CutRequest }) return true;
            if (node is FrameworkElement { Tag: BranchPreview }) return true;
            if (node is FrameworkElement { Tag: AddRequest or RingAddRequest or ColumnRequest or GroupAddRequest }) return true;
            if (node is FrameworkElement { Tag: DisabledPlus }) return true;
            if (node is FrameworkElement element && UiId.Get(element)?.StartsWith("wheel", StringComparison.Ordinal) == true) return true;
        }
        return false;
    }
}
