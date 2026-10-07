using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Orientation = System.Windows.Controls.Orientation;

namespace OrbitWeave;

// Fenêtre des réglages, au style de la roue (son verre, ses couleurs) ; se ferme en cliquant ailleurs ou par Échap.
// Petite près de l'icône : thèmes, curseurs, données. « Plus de réglages… » l'agrandit contre le bord droit de l'écran,
// sur toute sa hauteur, avec en plus le comportement et le raccourci ; le contenu défile si besoin.
public sealed partial class QuickPanel : Window
{
    private static readonly string[] Themes = ["Clair", "Sombre", "Verre", "Verre liquide"];

    private readonly Func<HotkeySettings> _settings;
    private readonly Action<Action<HotkeySettings>> _change;
    private readonly UniformGrid _themes = new() { Columns = 4, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Border _card = new() { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Padding = new Thickness(16, 14, 16, 12) };
    private DateTime _hiddenAt;
    // Vitesse affichée = 1 / durée relative des animations (à droite = plus rapide).
    private readonly Slider _speed = NewSlider(1 / HotkeySettings.MaxAnimationScale, 1 / HotkeySettings.MinAnimationScale);
    private readonly Slider _idle = NewSlider(HotkeySettings.MinIdleOpacity, 1);
    private readonly Slider _glass = NewSlider(HotkeySettings.MinGlassOpacity, HotkeySettings.MaxGlassOpacity);
    private readonly System.Windows.Threading.DispatcherTimer _sliderTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private bool _refreshing;
    private bool _expanded;
    private bool _capturing;
    private readonly StackPanel _more = new() { Visibility = Visibility.Collapsed };
    private readonly List<Action<HotkeySettings>> _refreshers = [];
    private readonly Button _hotkey = new() { Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _hotkeyHint = Text("", 11, FontWeights.Normal, new Thickness(0, 6, 0, 0), secondary: true);
    private readonly TextBlock _moreLink = new() { Text = "Plus de réglages…", FontSize = 12, Margin = new Thickness(0, 8, -6, -4), Padding = new Thickness(10, 6, 6, 4),
        Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand,
        HorizontalAlignment = HorizontalAlignment.Right, TextDecorations = TextDecorations.Underline };

    // settings : réglages actuels ; change : modifie, enregistre et applique.
    // data : boutons de la section Données (texte, action) ; la fenêtre se ferme avant, les confirmations passent devant.
    public QuickPanel(Func<HotkeySettings> settings, Action<Action<HotkeySettings>> change,
        IReadOnlyList<(string Label, Func<Task> Run)> data, IconLibrary? icons = null)
    {
        _settings = settings; _change = change; _icons = icons;
        Style = new Style(typeof(Window));
        Width = 360;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Title = "OrbitWeave";
        Left = Top = -10000;

        var body = new StackPanel();
        body.Children.Add(Text("OrbitWeave", 15, FontWeights.SemiBold));
        body.Children.Add(Text("Thème", 12, FontWeights.SemiBold, new Thickness(0, 12, 0, 0), secondary: true));
        body.Children.Add(_themes);
        body.Children.Add(SliderRow("Vitesse d'animation", _speed, "Lente", "Rapide"));
        body.Children.Add(SliderRow("Roue au repos", _idle, "Discrète", "Visible"));
        body.Children.Add(SliderRow("Transparence", _glass, "Plus de verre", "Plus opaque"));
        // Pendant le glisser, un seul enregistrement toutes les 80 ms ; la roue suit en direct.
        foreach (var slider in new[] { _speed, _idle, _glass })
            slider.ValueChanged += (_, _) => { if (!_refreshing) { _sliderTimer.Stop(); _sliderTimer.Start(); } };
        _sliderTimer.Tick += (_, _) =>
        {
            _sliderTimer.Stop();
            var (speed, idle, glass) = (_speed.Value, _idle.Value, _glass.Value);
            _change(settings =>
            {
                settings.SetAnimationScale(1 / speed);
                settings.SetIdleOpacity(idle);
                settings.SetGlassOpacity(glass);
            });
        };
        BuildMore();
        body.Children.Add(_more);
        body.Children.Add(Text("Données", 12, FontWeights.SemiBold, new Thickness(0, 14, 0, 0), secondary: true));
        var dataGrid = new UniformGrid { Columns = 2, Margin = new Thickness(-2, 6, -2, 0) };
        foreach (var (label, run) in data) dataGrid.Children.Add(DataButton(label, run));
        body.Children.Add(dataGrid);
        // Fond transparent et marge intérieure : tout le rectangle du lien répond au clic, pas seulement les lettres.
        _moreLink.SetResourceReference(TextBlock.ForegroundProperty, "WidgetSecondaryTextBrush");
        _moreLink.MouseLeftButtonUp += (_, _) => SetExpanded(!_expanded);
        body.Children.Add(_moreLink);
        _card.Child = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        _card.SetResourceReference(Border.BorderBrushProperty, "WidgetGlassBorderBrush");
        _card.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Opacity = 0.4 };
        Content = new Border { Child = _card, Padding = new Thickness(12) };

        Deactivated += (_, _) => { if (!_inDialog) HidePanel(); };
        PreviewKeyDown += (_, e) =>
        {
            if (_capturing) { CaptureKey(e); return; }
            if (e.Key == Key.Escape) { HidePanel(); e.Handled = true; }
        };
        Closing += (_, e) => { e.Cancel = !_closing; if (!_closing) HidePanel(); };
    }

    private bool _closing;
    public void CloseForGood() { _closing = true; Close(); }

    // Un clic sur l'icône juste après une fermeture par clic ailleurs (ce même clic) ne doit pas la rouvrir.
    public bool JustHidden => DateTime.Now - _hiddenAt < TimeSpan.FromMilliseconds(350);

    // Ouvre la fenêtre contre le coin de la zone de travail le plus proche du point donné (pixels d'écran).
    public void ShowNear(System.Drawing.Point anchor)
    {
        if (_expanded) SetExpanded(false, move: false);
        Refresh();
        if (!IsVisible) { Left = Top = -10000; Show(); }
        UpdateLayout();
        var fromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var area = System.Windows.Forms.Screen.FromPoint(anchor).WorkingArea;
        var topLeft = fromDevice.Transform(new Point(area.Left, area.Top));
        var bottomRight = fromDevice.Transform(new Point(area.Right, area.Bottom));
        var point = fromDevice.Transform(new Point(anchor.X, anchor.Y));
        Left = point.X > (topLeft.X + bottomRight.X) / 2 ? bottomRight.X - ActualWidth : topLeft.X;
        Top = point.Y > (topLeft.Y + bottomRight.Y) / 2 ? bottomRight.Y - ActualHeight : topLeft.Y;
        Activate();
    }

    public void HidePanel()
    {
        StopCapture();
        if (!IsVisible) return;
        _hiddenAt = DateTime.Now;
        Hide();
    }

    // Couleurs du thème actuel, et vignette du thème choisi.
    public void Refresh()
    {
        var base_ = (System.Windows.Application.Current.TryFindResource("GlassBaseBrush") as SolidColorBrush)?.Color ?? Color.FromRgb(40, 40, 40);
        _card.Background = new SolidColorBrush(Color.FromArgb(0xF0, base_.R, base_.G, base_.B));
        _themes.Children.Clear();
        var settings = _settings();
        foreach (var theme in Themes) _themes.Children.Add(ThemeTile(theme, theme == settings.Theme));
        _refreshing = true;
        try
        {
            _speed.Value = 1 / (settings.AnimationScale ?? HotkeySettings.PresetAnimationScale(settings.Animation));
            _idle.Value = settings.IdleOpacity ?? HotkeySettings.PresetIdleOpacity(settings.Idle);
            _glass.Value = settings.GlassOpacity;
        }
        finally { _refreshing = false; }
        foreach (var refresh in _refreshers) refresh(settings);
    }

    // Agrandie : contre le bord droit de l'écran où elle est, sur toute la hauteur de la zone de travail.
    private void SetExpanded(bool expanded, bool move = true)
    {
        _expanded = expanded;
        StopCapture();
        _more.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        _moreLink.Text = expanded ? "Moins de réglages" : "Plus de réglages…";
        if (!expanded) ShowUpdate();
        if (!move) { SizeToContent = SizeToContent.Height; return; }
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var area = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
        var fromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = fromDevice.Transform(new Point(area.Left, area.Top));
        var bottomRight = fromDevice.Transform(new Point(area.Right, area.Bottom));
        if (expanded)
        {
            SizeToContent = SizeToContent.Manual;
            Height = bottomRight.Y - topLeft.Y;
            Left = bottomRight.X - ActualWidth;
            Top = topLeft.Y;
        }
        else
        {
            SizeToContent = SizeToContent.Height;
            UpdateLayout();
            Left = bottomRight.X - ActualWidth;
            Top = bottomRight.Y - ActualHeight;
        }
    }

    // Comportement et raccourci : visibles seulement dans la fenêtre agrandie.
    private void BuildMore()
    {
        _more.Children.Add(Text("Comportement", 12, FontWeights.SemiBold, new Thickness(0, 14, 0, 0), secondary: true));
        _more.Children.Add(Choice("Ouvrir la roue", ["Au clic", "Au survol"], s => s.OpenBehavior, (s, value) => s.OpenBehavior = value));
        _more.Children.Add(Choice("Ouvrir une section", ["Au clic", "Au survol"], s => s.SectionBehavior, (s, value) => s.SectionBehavior = value));
        _more.Children.Add(Choice("Lancement rapide", ["Activé", "Désactivé"], s => s.HoldSelect ? "Activé" : "Désactivé", (s, value) => s.HoldSelect = value == "Activé"));
        _more.Children.Add(BranchCloseRow());
        var quickHint = Text("Maintenir le clic sur une section, glisser sur un bouton, relâcher.", 11, FontWeights.Normal, new Thickness(0, 4, 0, 0), secondary: true);
        quickHint.TextWrapping = TextWrapping.Wrap;
        quickHint.TextTrimming = TextTrimming.None;
        _more.Children.Add(quickHint);
        _more.Children.Add(Text("Raccourci", 12, FontWeights.SemiBold, new Thickness(0, 14, 0, 0), secondary: true));
        _hotkey.Template = TileTemplate();
        _hotkey.Background = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
        _hotkey.BorderThickness = new Thickness(1);
        _hotkey.BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        _hotkey.Cursor = System.Windows.Input.Cursors.Hand;
        _hotkey.ToolTip = "Cliquer, puis appuyer sur la nouvelle combinaison";
        _hotkey.Click += (_, _) =>
        {
            _capturing = true;
            _hotkey.Content = Centered("Appuie sur la combinaison…");
            _hotkeyHint.Text = "N'importe quelle touche ou combinaison (Ctrl, Alt, Maj, Win). Échap pour garder l'actuel.";
            _hotkeyHint.Visibility = Visibility.Visible;
            _hotkey.Focus();
        };
        _more.Children.Add(_hotkey);
        _hotkeyHint.TextWrapping = TextWrapping.Wrap;
        _hotkeyHint.TextTrimming = TextTrimming.None;
        _hotkeyHint.Visibility = Visibility.Collapsed;
        _more.Children.Add(_hotkeyHint);
        _refreshers.Add(s => { if (!_capturing) _hotkey.Content = Centered(Settings.HotkeyCaptureRules.Display(s)); });
        BuildIcons();
        BuildUpdates();
    }

    // Repli d'une branche : délai affiché en secondes ; enregistré au relâchement (et toutes les 80 ms pendant le glisser).
    private FrameworkElement BranchCloseRow()
    {
        var slider = NewSlider(HotkeySettings.MinBranchCloseSeconds, HotkeySettings.MaxBranchCloseSeconds);
        slider.SmallChange = 0.5;
        slider.LargeChange = 2;
        slider.IsSnapToTickEnabled = true;
        slider.TickFrequency = 0.5;
        var title = Text("", 12, FontWeights.Normal);
        void ShowValue() => title.Text = $"Repli d'une branche · {slider.Value.ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"))} s";
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        timer.Tick += (_, _) => { timer.Stop(); var seconds = slider.Value; _change(settings => settings.SetBranchCloseSeconds(seconds)); };
        slider.ValueChanged += (_, _) => { ShowValue(); if (!_refreshing) { timer.Stop(); timer.Start(); } };
        _refreshers.Add(settings => { var was = _refreshing; _refreshing = true; slider.Value = settings.BranchCloseSeconds; _refreshing = was; ShowValue(); });
        var row = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        row.Children.Add(title);
        row.Children.Add(slider);
        var ends = new DockPanel();
        var end = Text("Lent", 11, FontWeights.Normal, secondary: true);
        DockPanel.SetDock(end, Dock.Right);
        ends.Children.Add(end);
        ends.Children.Add(Text("Rapide", 11, FontWeights.Normal, secondary: true));
        row.Children.Add(ends);
        return row;
    }

    private void StopCapture()
    {
        if (!_capturing) return;
        _capturing = false;
        _hotkeyHint.Visibility = Visibility.Collapsed;
        _hotkey.Content = Centered(Settings.HotkeyCaptureRules.Display(_settings()));
    }

    // Toute touche, seule ou avec Ctrl / Alt / Maj / Win. Échap seul annule ; une touche d'écriture seule est acceptée avec un avertissement.
    private void CaptureKey(KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key switch { Key.System => e.SystemKey, Key.ImeProcessed => e.ImeProcessedKey, _ => e.Key };
        var modifiers = Keyboard.Modifiers;
        var (ctrl, alt, shift, win) = (modifiers.HasFlag(ModifierKeys.Control), modifiers.HasFlag(ModifierKeys.Alt),
            modifiers.HasFlag(ModifierKeys.Shift), modifiers.HasFlag(ModifierKeys.Windows));
        var name = key.ToString();
        if (key == Key.Escape && !ctrl && !alt && !shift && !win) { StopCapture(); return; }
        if (!Settings.HotkeyCaptureRules.IsUsable(ctrl, alt, shift, win, name)) return;
        _capturing = false;
        _change(settings => { settings.Control = ctrl; settings.Alt = alt; settings.Shift = shift; settings.Win = win; settings.Key = name; });
        Refresh();
        if (Settings.HotkeyCaptureRules.BlocksTyping(ctrl, alt, shift, win, name))
        {
            _hotkeyHint.Text = "Attention : seule, cette touche ne s'écrira plus ailleurs, elle ouvrira la roue.";
            _hotkeyHint.Visibility = Visibility.Visible;
        }
        else _hotkeyHint.Visibility = Visibility.Collapsed;
    }

    // Deux choix côte à côte ; celui en vigueur est entouré de rose.
    private FrameworkElement Choice(string title, string[] options, Func<HotkeySettings, string> get, Action<HotkeySettings, string> set)
    {
        var row = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        row.Children.Add(Text(title, 12, FontWeights.Normal));
        var grid = new UniformGrid { Columns = options.Length, Margin = new Thickness(-2, 4, -2, 0) };
        var buttons = new List<(string Option, Button Button)>();
        foreach (var option in options)
        {
            var button = new Button
            {
                Content = Centered(option), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(2), Cursor = System.Windows.Input.Cursors.Hand,
                Background = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)), HorizontalAlignment = HorizontalAlignment.Stretch, Template = TileTemplate()
            };
            button.Click += (_, _) => { _change(settings => set(settings, option)); Refresh(); };
            buttons.Add((option, button));
            grid.Children.Add(button);
        }
        _refreshers.Add(settings =>
        {
            var current = get(settings);
            foreach (var (option, button) in buttons)
            {
                var selected = option == current;
                button.BorderThickness = new Thickness(selected ? 2 : 1);
                button.BorderBrush = selected ? new SolidColorBrush(Color.FromRgb(0xFF, 0x3E, 0xA5)) : new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
            }
        });
        row.Children.Add(grid);
        return row;
    }

    private static TextBlock Centered(string text)
    {
        var block = Text(text, 12, FontWeights.Normal);
        block.HorizontalAlignment = HorizontalAlignment.Center;
        return block;
    }

    private FrameworkElement ThemeTile(string theme, bool selected)
    {
        var (backdrop, node, text) = theme switch
        {
            "Clair" => (Color.FromRgb(0xF1, 0xF1, 0xF0), Color.FromRgb(0xDA, 0xDA, 0xDA), Colors.Black),
            "Sombre" => (Color.FromRgb(0x10, 0x10, 0x10), Color.FromRgb(0x23, 0x23, 0x23), Colors.White),
            "Verre" => (Color.FromRgb(0x3A, 0x48, 0x5C), Color.FromArgb(0xB3, 0x32, 0x32, 0x32), Colors.White),
            _ => (Color.FromRgb(0x2E, 0x4A, 0x6B), Color.FromArgb(0xB8, 0x2C, 0x2C, 0x2E), Colors.White)
        };
        var preview = new Canvas { Width = 64, Height = 40, Background = new SolidColorBrush(backdrop), ClipToBounds = true };
        void Node(double x, double y, double size)
        {
            var circle = new System.Windows.Shapes.Ellipse { Width = size, Height = size, Fill = new SolidColorBrush(node),
                Stroke = new SolidColorBrush(Color.FromArgb(0x60, text.R, text.G, text.B)), StrokeThickness = 1 };
            Canvas.SetLeft(circle, x - size / 2);
            Canvas.SetTop(circle, y - size / 2);
            preview.Children.Add(circle);
        }
        Node(32, 24, 16); Node(14, 12, 10); Node(50, 12, 10); Node(50, 34, 10);
        var tile = new StackPanel();
        tile.Children.Add(new Border { Child = preview, CornerRadius = new CornerRadius(6), ClipToBounds = true });
        tile.Children.Add(Text(theme, 11, selected ? FontWeights.SemiBold : FontWeights.Normal, new Thickness(0, 4, 0, 0)));
        var button = new Button
        {
            Content = tile, Padding = new Thickness(3), Margin = new Thickness(2), Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand,
            BorderThickness = new Thickness(selected ? 2 : 1), ToolTip = theme,
            BorderBrush = selected ? new SolidColorBrush(Color.FromRgb(0xFF, 0x3E, 0xA5)) : new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF))
        };
        button.Template = TileTemplate();
        button.Click += (_, _) =>
        {
            _change(settings => settings.Theme = theme);
            Refresh();
        };
        return button;
    }

    private Button DataButton(string label, Func<Task> run)
    {
        var text = Text(label, 12, FontWeights.Normal);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        var button = new Button
        {
            Content = text, Padding = new Thickness(8, 7, 8, 7), Margin = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Stretch, Cursor = System.Windows.Input.Cursors.Hand,
            Background = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), Template = TileTemplate()
        };
        button.Click += async (_, _) =>
        {
            HidePanel();
            try { await run(); }
            catch (Exception ex) { CrashLog.Write("QuickPanel." + label, ex); }
        };
        return button;
    }

    // Bouton sans le style de la bibliothèque d'interface : juste son cadre.
    private static ControlTemplate TileTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    private static Slider NewSlider(double min, double max) => new()
    {
        Minimum = min, Maximum = max, SmallChange = (max - min) / 50, LargeChange = (max - min) / 10,
        IsMoveToPointEnabled = true, Margin = new Thickness(0, 2, 0, 0)
    };

    private static FrameworkElement SliderRow(string title, Slider slider, string left, string right)
    {
        var row = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        row.Children.Add(Text(title, 12, FontWeights.SemiBold, secondary: true));
        row.Children.Add(slider);
        var ends = new DockPanel();
        var end = Text(right, 11, FontWeights.Normal, secondary: true);
        DockPanel.SetDock(end, Dock.Right);
        ends.Children.Add(end);
        ends.Children.Add(Text(left, 11, FontWeights.Normal, secondary: true));
        row.Children.Add(ends);
        return row;
    }

    private static TextBlock Text(string text, double size, FontWeight weight, Thickness margin = default, bool secondary = false)
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = weight, Margin = margin, TextTrimming = TextTrimming.CharacterEllipsis };
        block.SetResourceReference(TextBlock.ForegroundProperty, secondary ? "WidgetSecondaryTextBrush" : "WidgetPrimaryTextBrush");
        return block;
    }
}
