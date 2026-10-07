using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using TextBox = System.Windows.Controls.TextBox;

namespace OrbitWeave.Ui.Editor;

// Bulle d'une section en mode Modifier : son nom et son symbole. Le nom s'applique à la validation
// (Entrée, clic ailleurs) : un nom à moitié tapé ne renomme rien, et le renommage compte pour une étape.
public sealed class SectionBubble : Window
{
    private readonly Func<IReadOnlyList<ActionItem>> _actions;
    private readonly Func<string, string, string?> _rename;
    private readonly Action<string, string> _setIcon;
    private readonly Action<KeyEventArgs> _keys;
    private readonly TextBox _name = new() { FontSize = 14, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Button _icon = new() { Width = 36, Height = 34, FontSize = 16, Padding = new Thickness(0), ToolTip = "Symbole de la section" };
    private readonly TextBlock _error = new() { FontSize = 12, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap,
        Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x8A)), Visibility = Visibility.Collapsed };
    private readonly TextBlock _count;
    private readonly Popup _symbolPopup = new() { StaysOpen = false, Placement = PlacementMode.Bottom, AllowsTransparency = true };
    private bool _closingForGood;

    public string? Section { get; private set; }

    // rename(ancien, nouveau) : null si fait, sinon le message à montrer. setIcon(section, symbole).
    public SectionBubble(Func<IReadOnlyList<ActionItem>> actions, Func<string, string, string?> rename,
        Action<string, string> setIcon, Action<KeyEventArgs> keys)
    {
        _actions = actions; _rename = rename; _setIcon = setIcon; _keys = keys;
        Style = new Style(typeof(Window));
        MinWidth = MinHeight = 0;
        Width = 300;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Title = "Section · OrbitWeave";
        Left = Top = -10000;

        // Échap est consommé avant d'arriver au champ (style de la bibliothèque d'interface) : écouté même déjà traité.
        _name.AddHandler(Keyboard.PreviewKeyDownEvent, new System.Windows.Input.KeyEventHandler((_, e) =>
        {
            if (e.Key == Key.Enter) { CommitName(); e.Handled = true; }
            else if (e.Key == Key.Escape)
            {
                _name.Text = Section ?? "";
                _error.Visibility = Visibility.Collapsed;
                _name.SelectAll();
                e.Handled = true;
            }
        }), handledEventsToo: true);
        _name.LostKeyboardFocus += (_, _) => CommitName();
        _icon.Click += (_, _) => _symbolPopup.IsOpen = true;
        _symbolPopup.PlacementTarget = _icon;
        _symbolPopup.Child = SymbolGrid();

        var header = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_icon, Dock.Left);
        header.Children.Add(_icon);
        _name.Margin = new Thickness(8, 0, 0, 0);
        header.Children.Add(_name);

        var body = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
        body.Children.Add(ActionBubble.Muted("Section", new Thickness(0, 0, 0, 6)));
        body.Children.Add(header);
        body.Children.Add(_error);
        body.Children.Add(_count = ActionBubble.Muted("", new Thickness(0, 10, 0, 0)));

        var card = new Border { Child = body, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, "SettingsCardBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "SettingsBorderBrush");
        card.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Opacity = 0.35 };
        Content = new Border { Child = card, Padding = new Thickness(10) };

        PreviewKeyDown += (_, e) => { if (Keyboard.FocusedElement is not TextBox) _keys(e); };
        // Affichée sans prendre la main : un clic dedans l'active, sinon le clavier reste à la roue (Échap y quitterait le mode Modifier).
        PreviewMouseDown += (_, _) => { if (!IsActive) Activate(); };
        Closing += (_, e) => { if (_closingForGood) return; e.Cancel = true; HideBubble(); };
    }

    public void ShowFor(string section, Rect targetScreenPixels, Rect? avoid = null)
    {
        if (!string.Equals(Section, section, StringComparison.OrdinalIgnoreCase))
        {
            CommitName();
            _symbolPopup.IsOpen = false;
            _error.Visibility = Visibility.Collapsed;
            Section = section;
            _name.Text = section;
        }
        Refresh();
        if (!IsVisible) { Left = Top = -10000; Show(); }
        BubblePlacement.MoveNextTo(this, targetScreenPixels, avoid);
    }

    // « Renommer » du clic droit : le nom est prêt à être remplacé.
    public void FocusName()
    {
        Activate();
        _name.Focus();
        _name.SelectAll();
    }

    public void HideBubble()
    {
        CommitName();
        _symbolPopup.IsOpen = false;
        if (IsVisible) Hide();
    }

    public void CloseForGood()
    {
        _closingForGood = true;
        Close();
    }

    public void Refresh()
    {
        if (Section is not { } section) return;
        var members = _actions().Where(a => a.Group.Trim().Equals(section.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (members.Count == 0) return;
        _icon.Content = string.IsNullOrWhiteSpace(members[0].GroupIcon) ? "✦" : members[0].GroupIcon;
        if (!_name.IsKeyboardFocusWithin && _error.Visibility != Visibility.Visible) _name.Text = members[0].Group.Trim();
        var roots = Actions.ActionTree.Roots(_actions().ToList(), section).Count;
        _count.Text = $"{roots} bouton{(roots > 1 ? "s" : "")} · clic droit sur la section pour en ajouter ou la supprimer";
    }

    private void CommitName()
    {
        if (Section is not { } section) return;
        var name = _name.Text.Trim();
        if (name == section.Trim()) { _error.Visibility = Visibility.Collapsed; return; }
        if (_rename(section, name) is { } error)
        {
            _error.Text = error;
            _error.Visibility = Visibility.Visible;
            return;
        }
        _error.Visibility = Visibility.Collapsed;
        Section = name;
    }

    private FrameworkElement SymbolGrid()
    {
        var grid = new UniformGrid { Columns = 8, Margin = new Thickness(8) };
        foreach (var symbol in ActionBubble.Symbols)
        {
            var button = new Button { Content = symbol, Width = 32, Height = 32, Padding = new Thickness(0), FontSize = 15, Margin = new Thickness(1) };
            button.Click += (_, _) =>
            {
                _symbolPopup.IsOpen = false;
                if (Section is { } section) _setIcon(section, symbol);
            };
            grid.Children.Add(button);
        }
        var border = new Border { Child = grid, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
        border.SetResourceReference(Border.BackgroundProperty, "SettingsCardBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "SettingsBorderBrush");
        return border;
    }
}
