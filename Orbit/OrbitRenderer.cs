using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using OrbitWeave.Orbit;
using OrbitWeave.Ui;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using Panel = System.Windows.Controls.Panel;
using Size = System.Windows.Size;

namespace OrbitWeave;

public partial class MainWindow
{
    // Construction de la roue, isolée du cycle de vie de la fenêtre.
    private sealed class OrbitRenderer
    {
    public void Render(MainWindow owner)
    {
        owner._renderCount++;
        owner._orbitView.Clear();
        owner._pendingAnimations.Clear();
        owner._baseElements.Clear();
        owner._expandedElements.Clear();
        owner._openCards.Clear();
        owner._groupButtons.Clear();
        owner._groupInfos.Clear();
        owner._groupLabels.Clear();
        owner._spokes.Clear();
        owner._liveLabels.Clear();
        owner._appliedLabelAttachments.Clear();
        foreach (var (id, point) in UiRuntime.Profile.Layout.Attachments)
            owner._appliedLabelAttachments[id] = point;
        owner._activeGroup = null;
        var visible = owner._actions.Where(a => a.GroupVisible).GroupBy(a => a.Group.Trim(), StringComparer.OrdinalIgnoreCase)
            .Take(9).ToList();
        var groups = visible
            .Select((g, i) => new GroupInfo(g.Key, g.Select(a => a.GroupIcon).FirstOrDefault(i => !string.IsNullOrWhiteSpace(i)) ?? "✦", Actions.ActionTree.Roots(g, g.Key).ToList(), OrbitLayout.RadiusFor(i, visible.Count), g.Max(a => a.BadgeCount), g.First().GroupType))
            .ToList();
        var count = groups.Count;
        owner._wheelGeometry = owner.CalculateWheelGeometry(groups);
        var geometry = owner._wheelGeometry;
        var center = MainWindow.ToPoint(geometry.HubCenter);
        var hubMasked = UiRuntime.Profile.Elements.TryGetValue("wheel.hub", out var hubValues) &&
            hubValues.GetValueOrDefault("Visible") == "false";

        var hub = owner._orbitView.Node("", true).With("wheel.hub", isProtected: true);
        if (hub.Content is FrameworkElement hubDot) hubDot.With("wheel.hub.dot");
        owner._hub = hub;
        hub.Opacity = 1;
        hub.ToolTip = owner._folded ? "Afficher la roue · clic droit pour les options" : "Replier la roue · clic droit pour les options";
        hub.Click += (_, _) => { if (owner._folded) { owner._folded = false; owner.RenderOrbit(); } else owner.FoldWheel(); };
        hub.MouseEnter += (_, _) => { if (!owner._wheelEditMode && owner._folded && owner._settings.OpenBehavior == "Au survol") { owner._folded = false; owner.RenderOrbit(); } };
        var menu = new System.Windows.Controls.ContextMenu { Background = new SolidColorBrush(owner.SurfaceColor), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(owner.AccentColor) }.With("wheel.menu", isProtected: true);
        var editEntry = new System.Windows.Controls.MenuItem { Header = "✎  Modifier la roue", Foreground = Brushes.White }.With("wheel.menu.edit", isProtected: true);
        editEntry.Click += (_, _) => owner.StartWheelEditing();
        var settingsEntry = new System.Windows.Controls.MenuItem { Header = "⚙  Thèmes et réglages…", Foreground = Brushes.White }.With("wheel.menu.settings", isProtected: true);
        settingsEntry.Click += (_, _) => owner.ShowQuickPanel();
        var treeEntry = new System.Windows.Controls.MenuItem { Foreground = Brushes.White }.With("wheel.menu.tree", isProtected: true);
        treeEntry.Click += (_, _) => owner.ToggleTree();
        menu.Opened += (_, _) => treeEntry.Header = owner.TreeOpen ? "⌂  Refermer l'arbre" : "❖  Ouvrir tout l'arbre";
        var quitEntry = new System.Windows.Controls.MenuItem { Header = "✕  Quitter OrbitWeave", Foreground = Brushes.White }.With("wheel.menu.quit");
        quitEntry.Click += (_, _) => System.Windows.Application.Current.Shutdown();
        menu.Items.Add(editEntry);
        menu.Items.Add(settingsEntry);
        menu.Items.Add(treeEntry);
        menu.Items.Add(new Separator());
        menu.Items.Add(quitEntry);
        hub.ContextMenu = menu;
        Canvas.SetLeft(hub, geometry.HubTopLeft.X);
        Canvas.SetTop(hub, geometry.HubTopLeft.Y);
        MainWindow.Customize(hub);
        if (hubMasked) hub.Opacity = 0.01;
        MainWindow.Customize(menu);
        Border? hubHit = null;
        owner._hubHit = null;
        if (hubMasked)
        {
            hub.Visibility = Visibility.Hidden;
            hubHit = new Border { Width = OrbitLayout.HubDiameter, Height = OrbitLayout.HubDiameter,
                Background = Brushes.Transparent, ContextMenu = menu };
            Canvas.SetLeft(hubHit, Canvas.GetLeft(hub));
            Canvas.SetTop(hubHit, Canvas.GetTop(hub));
            Panel.SetZIndex(hubHit, 100);
            owner._hubHit = hubHit;
            hubHit.With("wheel.hub.hit", isProtected: true);
            hubHit.MouseLeftButtonUp += (_, _) =>
            {
                if (owner._wheelEditMode) return;
                if (owner._folded) { owner._folded = false; owner.RenderOrbit(); }
                else owner.FoldWheel();
            };
            hubHit.MouseEnter += (_, _) =>
            {
                if (!owner._wheelEditMode && owner._folded && owner._settings.OpenBehavior == "Au survol")
                { owner._folded = false; owner.RenderOrbit(); }
            };
        }
        if (owner._folded)
        {
            owner._orbitView.Add(hub);
            if (hubHit is not null) owner._orbitView.Add(hubHit);
            owner._baseElements.AddRange(owner.OrbitCanvas.Children.Cast<UIElement>());
            return;
        }

        var hubDiskSize = MainWindow.Token("HubDiskDiameter");
        var hubDisk = new Ellipse { Width = hubDiskSize, Height = hubDiskSize,
            Fill = MainWindow.BrushToken("HubDiskBrush"), IsHitTestVisible = false }.With("wheel.hub.disk");
        Canvas.SetLeft(hubDisk, center.X - hubDiskSize / 2);
        Canvas.SetTop(hubDisk, center.Y - hubDiskSize / 2);
        MainWindow.Customize(hubDisk);
        if (hubMasked) hubDisk.Opacity = 0;
        Panel.SetZIndex(hubDisk, -2);
        owner._orbitView.Add(hubDisk);

        var halo = owner._orbitView.Halo().With("wheel.halo");
        Canvas.SetLeft(halo, geometry.HaloTopLeft.X);
        Canvas.SetTop(halo, geometry.HaloTopLeft.Y);
        MainWindow.Customize(halo);
        if (hubMasked) halo.Opacity = 0;
        owner._orbitView.Add(halo);

        for (var i = 0; i < count; i++)
        {
            var index = i;
            var section = geometry.Sections[i];
            var point = MainWindow.ToPoint(section.Center);
            var inner = owner._orbitView.Spoke(center, point).With($"wheel.spoke:{groups[i].Name}");
            MainWindow.Customize(inner);
            Panel.SetZIndex(inner, -1);
            owner._orbitView.Add(inner);
            owner._spokes[groups[i].Name] = inner;
            void StartSpoke()
            {
                var spokeDelay = owner.Motion(index * MainWindow.Token("MotionStagger"));
                var spokeDuration = owner.Motion(MainWindow.Token("MotionOpen"));
                inner.BeginAnimation(Line.X2Property, null);
                inner.BeginAnimation(Line.Y2Property, null);
                inner.X2 = center.X;
                inner.Y2 = center.Y;
                var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
                inner.BeginAnimation(Line.X2Property, new DoubleAnimation(center.X, point.X, spokeDuration)
                { BeginTime = spokeDelay, EasingFunction = easing });
                inner.BeginAnimation(Line.Y2Property, new DoubleAnimation(center.Y, point.Y, spokeDuration)
                { BeginTime = spokeDelay, EasingFunction = easing });
            }
            owner._pendingAnimations.Add(StartSpoke);
            if (!owner._deferAnimations) StartSpoke();
        }

        owner._orbitView.Add(hub);
        if (hubHit is not null) owner._orbitView.Add(hubHit);

        for (var i = 0; i < count; i++)
        {
            var index = i;
            var section = geometry.Sections[i];
            var group = groups[i] with { Position = section.Center, Radius = section.Radius };
            var angle = section.Angle;
            var point = MainWindow.ToPoint(section.Center);
            var x = point.X;
            var y = point.Y;
            var node = owner._orbitView.Node(group.Icon, false).With($"wheel.section:{group.Name}");
            if (node.Content is FrameworkElement nodeIcon) nodeIcon.With($"wheel.section.icon:{group.Name}");
            node.Click += (_, _) => owner.ExpandGroup(group, angle);
            node.MouseEnter += (_, _) => { if (!owner._wheelEditMode && owner._settings.SectionBehavior == "Au survol") owner.ExpandGroup(group, angle); };
            node.PreviewMouseLeftButtonDown += (_, e) =>
            {
                if (!owner._settings.HoldSelect) return;
                owner.ExpandGroup(group, angle);
                owner._quickSelecting = owner.CaptureMouse();
                e.Handled = true;
            };
            Canvas.SetLeft(node, section.NodeTopLeft.X);
            Canvas.SetTop(node, section.NodeTopLeft.Y);
            MainWindow.Customize(node);
            owner._orbitView.Add(node);
            owner._groupButtons[group.Name] = node;
            owner._groupInfos[group.Name] = (group, angle);
            var label = owner._orbitView.Label(group.Name, group.Actions.Count);
            if (group.Type is "Système" or "Fichiers" &&
                label.Child is StackPanel { Children.Count: > 1 } labelText && labelText.Children[1] is TextBlock subtitle)
            {
                owner._liveLabels.Add((group.Type, label, subtitle, section.LabelAnchor.X));
                // Le texte vivant peut changer de largeur après la première mesure WPF.
                label.SizeChanged += (_, _) =>
                {
                    var anchor = owner._wheelGeometry.Section(group.Name).LabelAnchor;
                    Canvas.SetLeft(label, Math.Round(anchor.X - label.ActualWidth / 2));
                };
            }
            // Les textes et tailles surchargés comptent dans la mesure ; le décalage s'applique une fois placé.
            MainWindow.Customize(label);
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, Math.Round(section.LabelAnchor.X - label.DesiredSize.Width / 2));
            Canvas.SetTop(label, Math.Round(section.LabelAnchor.Y));
            UiOverrides.Apply(label, UiRuntime.Profile);
            owner._orbitView.Add(label);
            owner._groupLabels[group.Name] = label;
            void StartLabel()
            {
                label.BeginAnimation(UIElement.OpacityProperty, null);
                label.Opacity = 0;
                label.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, MainWindow.TargetOpacity(label), owner.Motion(MainWindow.Token("MotionLabel")))
                { BeginTime = owner.Motion(MainWindow.Token("MotionLabelDelay") + index * MainWindow.Token("MotionStagger")) });
            }
            owner._pendingAnimations.Add(StartLabel);
            if (!owner._deferAnimations) StartLabel();
            var scale = new ScaleTransform(0.6, 0.6, OrbitLayout.SectionDiameter / 2, OrbitLayout.SectionDiameter / 2);
            var translate = new TranslateTransform(center.X - point.X, center.Y - point.Y);
            var transforms = new TransformGroup();
            transforms.Children.Add(scale);
            transforms.Children.Add(translate);
            node.RenderTransform = transforms;
            void StartNode()
            {
                var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
                var delay = owner.Motion(index * MainWindow.Token("MotionStagger"));
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                translate.BeginAnimation(TranslateTransform.XProperty, null);
                translate.BeginAnimation(TranslateTransform.YProperty, null);
                node.BeginAnimation(UIElement.OpacityProperty, null);
                scale.ScaleX = scale.ScaleY = 0.6;
                translate.X = center.X - point.X;
                translate.Y = center.Y - point.Y;
                node.Opacity = 0;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.6, 1, owner.Motion(MainWindow.Token("MotionOpen")))
                { BeginTime = delay, EasingFunction = easing });
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.6, 1, owner.Motion(MainWindow.Token("MotionOpen")))
                { BeginTime = delay, EasingFunction = easing });
                translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(center.X - point.X, 0, owner.Motion(MainWindow.Token("MotionOpen")))
                { BeginTime = delay, EasingFunction = easing });
                translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(center.Y - point.Y, 0, owner.Motion(MainWindow.Token("MotionOpen")))
                { BeginTime = delay, EasingFunction = easing });
                node.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, MainWindow.TargetOpacity(node), owner.Motion(MainWindow.Token("MotionFade"))) { BeginTime = delay });
            }
            owner._pendingAnimations.Add(StartNode);
            if (!owner._deferAnimations) StartNode();
            if (group.BadgeCount > 0)
            {
                var badgeText = group.BadgeCount > 99 ? "99+" : group.BadgeCount.ToString();
                var badge = new Border
                {
                    Height = 16, MinWidth = 16, Padding = new Thickness(3, 0, 3, 0),
                    CornerRadius = new CornerRadius(8), Background = MainWindow.BrushToken("BadgeBrush"),
                    Child = new TextBlock { Text = badgeText, FontSize = 9, FontWeight = FontWeights.SemiBold,
                        Foreground = MainWindow.BrushToken("BadgeTextBrush"), TextAlignment = TextAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center },
                    Opacity = 0, IsHitTestVisible = false
                }.With($"wheel.badge:{group.Name}");
                badge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(badge, Math.Round(section.BadgeAnchor.X - badge.DesiredSize.Width / 2));
                Canvas.SetTop(badge, Math.Round(section.BadgeAnchor.Y));
                MainWindow.Customize(badge);
                owner._orbitView.Add(badge);
                void StartBadge()
                {
                    badge.BeginAnimation(UIElement.OpacityProperty, null);
                    badge.Opacity = 0;
                    badge.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, MainWindow.TargetOpacity(badge), owner.Motion(MainWindow.Token("MotionFade")))
                    { BeginTime = owner.Motion(MainWindow.Token("MotionLabelDelay") + index * MainWindow.Token("MotionStagger")) });
                }
                owner._pendingAnimations.Add(StartBadge);
                if (!owner._deferAnimations) StartBadge();
            }
        }
        var search = owner.SearchButton().With("wheel.search");
        var searchRadius = MainWindow.Token("SearchDiameter") / 2;
        var searchOffset = MainWindow.Token("SearchOffset");
        Canvas.SetLeft(search, geometry.SearchTopLeft.X);
        Canvas.SetTop(search, geometry.SearchTopLeft.Y);
        MainWindow.Customize(search);
        owner._orbitView.Add(search);
        var searchScale = new ScaleTransform(0.6, 0.6, searchRadius, searchRadius);
        var searchTranslate = new TranslateTransform(0, -searchOffset);
        var searchTransform = new TransformGroup();
        searchTransform.Children.Add(searchScale);
        searchTransform.Children.Add(searchTranslate);
        search.RenderTransform = searchTransform;
        void StartSearch()
        {
            search.BeginAnimation(UIElement.OpacityProperty, null);
            searchScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            searchScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            searchTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            search.Opacity = 0;
            searchScale.ScaleX = searchScale.ScaleY = 0.6;
            searchTranslate.Y = -searchOffset;
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            var searchDelay = owner.Motion(MainWindow.Token("MotionSearchDelay"));
            var searchDuration = owner.Motion(MainWindow.Token("MotionOpen"));
            search.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, MainWindow.TargetOpacity(search), owner.Motion(MainWindow.Token("MotionFade"))) { BeginTime = searchDelay });
            searchScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.6, 1, searchDuration) { BeginTime = searchDelay, EasingFunction = easing });
            searchScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.6, 1, searchDuration) { BeginTime = searchDelay, EasingFunction = easing });
            searchTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-searchOffset, 0, searchDuration) { BeginTime = searchDelay, EasingFunction = easing });
        }
        owner._pendingAnimations.Add(StartSearch);
        if (!owner._deferAnimations) StartSearch();
        UiCustomElements.AddTo(owner.OrbitCanvas, UiRuntime.Profile, "wheel", owner._wheelGeometry);
        owner._baseElements.AddRange(owner.OrbitCanvas.Children.Cast<UIElement>());
    }

    }
}

