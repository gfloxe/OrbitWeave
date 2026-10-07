using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using OrbitWeave.Actions;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ContextMenu = System.Windows.Controls.ContextMenu;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using ListBox = System.Windows.Controls.ListBox;
using MenuItem = System.Windows.Controls.MenuItem;
using Orientation = System.Windows.Controls.Orientation;
using Size = System.Windows.Size;
using TextBox = System.Windows.Controls.TextBox;

namespace OrbitWeave.Ui.Editor;

// Bulle d'un bouton en mode Modifier : nom, symbole, ce qu'il lance. Elle ne modifie rien elle-même :
// chaque changement part dans l'historique de l'éditeur (apply), qui redessine la roue puis la bulle.
public sealed class ActionBubble : Window
{
    internal static readonly string[] Symbols =
        ["✦", "▣", "◇", "◎", "▶", "⌗", "≡", "⚙", "▤", "↓", "□", "⌘", "▧", "♫", "♪", "◌", "✉", "★", "♥", "☀", "☾", "⚑", "✎", "⏻"];

    private readonly Func<IReadOnlyList<ActionItem>> _actions;
    private readonly Action<List<ActionItem>, string?> _apply;
    private readonly Func<ActionItem, Task> _test;
    private readonly Action<KeyEventArgs> _keys;
    private readonly Action _done;
    private readonly TextBox _name = new() { FontSize = 14, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Button _icon = new() { Width = 36, Height = 34, FontSize = 16, Padding = new Thickness(0), ToolTip = "Symbole" };
    private readonly StackPanel _steps = new();
    private readonly Popup _symbolPopup = new() { StaysOpen = false, Placement = PlacementMode.Bottom, AllowsTransparency = true };
    private readonly Popup _appsPopup = new() { StaysOpen = false, Placement = PlacementMode.Top, AllowsTransparency = true };
    private bool _refreshing, _closingForGood;
    private int _nameGesture, _editingIndex = -1;
    private int? _dragFrom;
    private double _dragStartY;

    public Guid? ActionId { get; private set; }

    // done : « OK » — les changements sont déjà appliqués ; la bulle se ferme et le bouton n'est plus sélectionné.
    public ActionBubble(Func<IReadOnlyList<ActionItem>> actions, Action<List<ActionItem>, string?> apply,
        Func<ActionItem, Task> test, Action<KeyEventArgs> keys, Action done)
    {
        _actions = actions; _apply = apply; _test = test; _keys = keys; _done = done;
        // Style vide : le style implicite de la bibliothèque d'interface impose une taille minimale.
        Style = new Style(typeof(Window));
        MinWidth = MinHeight = 0;
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Title = "Bouton · OrbitWeave";
        Left = Top = -10000;

        _name.GotKeyboardFocus += (_, _) => _nameGesture++;
        _name.TextChanged += (_, _) =>
        {
            if (_refreshing || ActionId is not { } id) return;
            _apply(ActionEdits.Rename(_actions(), id, _name.Text), $"bubble-name:{id}:{_nameGesture}");
        };
        _icon.Click += (_, _) => { FillLibrary(); _symbolPopup.IsOpen = true; };
        _symbolPopup.PlacementTarget = _icon;
        _symbolPopup.Child = SymbolGrid();

        var header = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_icon, Dock.Left);
        header.Children.Add(_icon);
        _name.Margin = new Thickness(8, 0, 0, 0);
        header.Children.Add(_name);

        var add = new Button { Content = "+  Ajouter", Padding = new Thickness(12, 5, 12, 5), ToolTip = "Ajouter une étape" };
        add.ContextMenu = AddMenu();
        add.Click += (_, _) => { add.ContextMenu.PlacementTarget = add; add.ContextMenu.IsOpen = true; };
        _appsPopup.PlacementTarget = add;
        var testButton = new Button { Content = "▶  Tester", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(8, 0, 0, 0) };
        testButton.Click += async (_, _) => { if (Current() is { } action) await _test(action); };
        var okButton = new Button { Content = "OK", Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(8, 0, 0, 0), ToolTip = "Fermer la bulle" };
        okButton.Click += (_, _) => { HideBubble(); _done(); };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        footer.Children.Add(add);
        footer.Children.Add(testButton);
        footer.Children.Add(okButton);

        var body = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
        body.Children.Add(header);
        body.Children.Add(Muted("Ce qu'il lance", new Thickness(0, 12, 0, 6)));
        body.Children.Add(_steps);
        body.Children.Add(footer);

        var card = new Border { Child = body, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, "SettingsCardBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "SettingsBorderBrush");
        card.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Opacity = 0.35 };
        Content = new Border { Child = card, Padding = new Thickness(10) };

        PreviewKeyDown += OnKey;
        // Affichée sans prendre la main : un clic dedans l'active, sinon le clavier reste à la roue.
        PreviewMouseDown += (_, _) => { if (!IsActive) Activate(); };
        Closing += (_, e) => { if (_closingForGood) return; e.Cancel = true; HideBubble(); };
    }

    public void ShowFor(Guid actionId, Rect targetScreenPixels, Rect? avoid = null)
    {
        var changed = ActionId != actionId;
        ActionId = actionId;
        if (changed) { _editingIndex = -1; ClosePopups(); }
        Refresh();
        if (!IsVisible) { Left = Top = -10000; Show(); }
        BubblePlacement.MoveNextTo(this, targetScreenPixels, avoid);
    }

    public void HideBubble()
    {
        ClosePopups();
        _editingIndex = -1;
        if (IsVisible) Hide();
    }

    public void CloseForGood()
    {
        _closingForGood = true;
        Close();
    }

    public void Refresh()
    {
        if (Current() is not { } action) return;
        _refreshing = true;
        try
        {
            if (!_name.IsKeyboardFocusWithin && _name.Text != action.Name) _name.Text = action.Name;
            ShowIcon(action);
            RebuildSteps(action);
        }
        finally { _refreshing = false; }
    }

    // Le bouton de symbole montre ce que montre la carte : l'icône réelle (automatique) ou le symbole.
    private async void ShowIcon(ActionItem action)
    {
        var symbol = string.IsNullOrWhiteSpace(action.Icon) ? "✦" : action.Icon;
        var target = IconTarget.For(action);
        _icon.ToolTip = target.Kind == IconKind.None ? "Symbole" : action.IconFile is not null ? "Icône choisie · clic pour changer" :
            "Icône automatique · clic pour choisir un symbole";
        if (Orbit.AppIcons.TryGetLoaded(target, out var loaded)) { _icon.Content = loaded is null ? symbol : Orbit.OrbitCardFactory.IconImage(loaded); return; }
        _icon.Content = symbol;
        var id = action.Id;
        var image = await Orbit.AppIcons.LoadAsync(target);
        if (image is not null && ActionId == id && Current() is { UseSymbol: false }) _icon.Content = Orbit.OrbitCardFactory.IconImage(image);
    }

    private ActionItem? Current() => ActionId is { } id ? _actions().FirstOrDefault(a => a.Id == id) : null;

    private void Apply(Func<IReadOnlyList<ActionItem>, Guid, List<ActionItem>> edit, string? gesture = null)
    {
        if (ActionId is not { } id) return;
        _apply(edit(_actions(), id), gesture);
    }

    private void RebuildSteps(ActionItem action)
    {
        _steps.Children.Clear();
        if (action.Steps.Count == 0)
        {
            _steps.Children.Add(Muted("Aucune étape · + pour en ajouter", new Thickness(0, 2, 0, 2)));
            return;
        }
        for (var i = 0; i < action.Steps.Count; i++)
        {
            _steps.Children.Add(StepRow(action.Steps[i], i));
            if (i == _editingIndex) _steps.Children.Add(StepEditor(action.Steps[i], i));
        }
    }

    private FrameworkElement StepRow(ActionStep step, int index)
    {
        var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1), Background = Brushes.Transparent, Tag = index };
        var handle = new TextBlock { Text = "⠿", FontSize = 14, Width = 18, Cursor = System.Windows.Input.Cursors.SizeNS,
            VerticalAlignment = VerticalAlignment.Center, ToolTip = "Glisser pour réordonner" };
        handle.SetResourceReference(TextBlock.ForegroundProperty, "SettingsMutedBrush");
        handle.MouseLeftButtonDown += (_, e) => { _dragFrom = index; _dragStartY = e.GetPosition(_steps).Y; handle.CaptureMouse(); e.Handled = true; };
        handle.MouseMove += (_, e) =>
        {
            if (_dragFrom is null || !handle.IsMouseCaptured) return;
            row.RenderTransform = new TranslateTransform(0, e.GetPosition(_steps).Y - _dragStartY);
            row.Opacity = 0.75;
        };
        handle.MouseLeftButtonUp += (_, e) =>
        {
            if (_dragFrom is not { } from) return;
            _dragFrom = null;
            handle.ReleaseMouseCapture();
            row.RenderTransform = null;
            row.Opacity = 1;
            var to = TargetIndex(e.GetPosition(_steps).Y);
            if (to != from) { _editingIndex = -1; Apply((list, id) => ActionEdits.MoveStep(list, id, from, to)); }
            e.Handled = true;
        };
        handle.LostMouseCapture += (_, _) => { if (_dragFrom is null) return; _dragFrom = null; row.RenderTransform = null; row.Opacity = 1; };

        var enabled = new CheckBox { IsChecked = step.Enabled, VerticalAlignment = VerticalAlignment.Center, MinWidth = 0,
            Margin = new Thickness(2, 0, 4, 0), ToolTip = step.Enabled ? "Désactiver cette étape" : "Activer cette étape" };
        enabled.Click += (_, _) => Apply((list, id) => ActionEdits.SetStepEnabled(list, id, index, enabled.IsChecked == true));

        var remove = new Button { Content = "✕", Width = 26, Height = 26, Padding = new Thickness(0), FontSize = 11,
            ToolTip = "Supprimer cette étape", Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        remove.Click += (_, _) => { _editingIndex = -1; Apply((list, id) => ActionEdits.RemoveStep(list, id, index)); };

        var text = new TextBlock { Text = StepText.Describe(step), VerticalAlignment = VerticalAlignment.Center, FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis, Cursor = System.Windows.Input.Cursors.Hand, Padding = new Thickness(2, 4, 2, 4),
            ToolTip = "Clic pour modifier" };
        text.SetResourceReference(TextBlock.ForegroundProperty, step.Enabled ? "SettingsTextBrush" : "SettingsMutedBrush");
        if (!step.Enabled) text.TextDecorations = TextDecorations.Strikethrough;
        text.MouseLeftButtonUp += (_, _) => { _editingIndex = _editingIndex == index ? -1 : index; Refresh(); };

        DockPanel.SetDock(handle, Dock.Left);
        DockPanel.SetDock(enabled, Dock.Left);
        DockPanel.SetDock(remove, Dock.Right);
        row.Children.Add(handle);
        row.Children.Add(enabled);
        row.Children.Add(remove);
        row.Children.Add(text);
        return row;
    }

    private int TargetIndex(double y)
    {
        var rows = _steps.Children.OfType<DockPanel>().ToList();
        if (rows.Count == 0) return 0;
        for (var i = 0; i < rows.Count; i++)
        {
            var top = rows[i].TranslatePoint(new Point(0, 0), _steps).Y;
            if (y < top + rows[i].ActualHeight) return Math.Max(0, (int)rows[i].Tag);
        }
        return (int)rows[^1].Tag;
    }

    private FrameworkElement StepEditor(ActionStep step, int index)
    {
        var (label, value, multiline) = step switch
        {
            WaitStep wait => ("Secondes", wait.Seconds.ToString("0.##", CultureInfo.GetCultureInfo("fr-FR")), false),
            ScriptStep { Source: ScriptSource.Commande } script => ("Commande", script.Code, true),
            ScriptStep script => ("Chemin du script", script.Path, false),
            OpenStep open => ("Ouvrir (appli, fichier, dossier ou adresse)", open.Target, false),
            _ => ("", "", false)
        };
        var box = new TextBox { Text = value, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = multiline ? 60 : 0, Margin = new Thickness(0, 2, 0, 0) };
        var committed = false;
        void Commit()
        {
            if (committed || _editingIndex != index) return;
            committed = true;
            _editingIndex = -1;
            if (WithValue(step, box.Text) is { } updated) Apply((list, id) => ActionEdits.ReplaceStep(list, id, index, updated));
            else Refresh();
        }
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && !multiline) { Commit(); e.Handled = true; }
            else if (e.Key == Key.Escape) { committed = true; _editingIndex = -1; Refresh(); e.Handled = true; }
        };
        box.LostKeyboardFocus += (_, _) => Commit();
        box.Loaded += (_, _) => { Activate(); box.Focus(); box.SelectAll(); };
        var panel = new StackPanel { Margin = new Thickness(18, 0, 0, 6) };
        panel.Children.Add(Muted(label, new Thickness(0)));
        panel.Children.Add(box);
        return panel;
    }

    // Copie de l'étape avec la nouvelle valeur ; null si la valeur ne change rien ou est invalide.
    private static ActionStep? WithValue(ActionStep step, string text)
    {
        var copy = System.Text.Json.JsonSerializer.Deserialize<ActionStep>(
            System.Text.Json.JsonSerializer.Serialize(step, ActionJson.Options), ActionJson.Options)!;
        switch (copy)
        {
            case WaitStep wait:
                var raw = text.Trim().Replace(" ", "").TrimEnd('s');
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.GetCultureInfo("fr-FR"), out var seconds) &&
                    !double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds)) return null;
                if (Math.Abs(wait.Seconds - Math.Clamp(seconds, 0.1, 3600)) < 0.0001) return null;
                wait.Seconds = seconds;
                return wait;
            case ScriptStep { Source: ScriptSource.Commande } script:
                if (script.Code == text) return null;
                script.Code = text;
                return script;
            case ScriptStep script:
                if (script.Path == text.Trim()) return null;
                script.Path = text.Trim();
                return script;
            case OpenStep open:
                if (open.Target == text.Trim()) return null;
                open.Target = text.Trim();
                return open;
            default:
                return null;
        }
    }

    private ContextMenu AddMenu()
    {
        var menu = new ContextMenu();
        MenuItem Item(string header, Action click)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => click();
            menu.Items.Add(item);
            return item;
        }
        Item("Appli installée…", OpenAppsPopup);
        Item("Fichier…", () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Fichier à ouvrir" };
            if (dialog.ShowDialog(this) == true) AddStep(new OpenStep { Target = dialog.FileName });
        });
        Item("Dossier…", () =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Dossier à ouvrir" };
            if (dialog.ShowDialog(this) == true) AddStep(new OpenStep { Target = dialog.FolderName });
        });
        Item("Site web…", () =>
        {
            if (Current() is not { } action) return;
            _editingIndex = action.Steps.Count;
            AddStep(new OpenStep { Target = "https://" });
        });
        Item("Attendre", () => AddStep(new WaitStep { Seconds = 1 }));
        Item("Script…", () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Script à lancer", Filter = "Scripts (*.ps1;*.bat;*.cmd)|*.ps1;*.bat;*.cmd" };
            if (dialog.ShowDialog(this) == true) AddStep(new ScriptStep { Source = ScriptSource.Fichier, Path = dialog.FileName });
        });
        return menu;
    }

    private void AddStep(ActionStep step) => Apply((list, id) => ActionEdits.AddStep(list, id, step));

    private async void OpenAppsPopup()
    {
        var search = new TextBox { Margin = new Thickness(0, 0, 0, 6) };
        var list = new ListBox { Height = 220, DisplayMemberPath = nameof(InstalledApp.Name) };
        var panel = new StackPanel { Margin = new Thickness(10) };
        panel.Children.Add(Muted("Rechercher une appli", new Thickness(0, 0, 0, 4)));
        panel.Children.Add(search);
        panel.Children.Add(list);
        var border = new Border { Child = panel, Width = 280, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
        border.SetResourceReference(Border.BackgroundProperty, "SettingsCardBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "SettingsBorderBrush");
        // Échap est consommé avant d'arriver au champ ; écouté ici même déjà traité.
        border.AddHandler(Keyboard.PreviewKeyDownEvent, new System.Windows.Input.KeyEventHandler((_, e) =>
        {
            if (e.Key != Key.Escape) return;
            _appsPopup.IsOpen = false;
            e.Handled = true;
        }), handledEventsToo: true);
        _appsPopup.Child = border;
        _appsPopup.IsOpen = true;
        Activate();
        search.Focus();
        list.ItemsSource = new[] { new InstalledApp("Chargement…", "") };
        var apps = await InstalledApps.Cached;
        void Filter() => list.ItemsSource = apps.Where(a => a.Name.Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase)).Take(40).ToList();
        Filter();
        search.TextChanged += (_, _) => Filter();
        void Choose()
        {
            // Entrée dans la recherche sans rien choisir : la première appli trouvée.
            var app = list.SelectedItem as InstalledApp ?? list.Items.OfType<InstalledApp>().FirstOrDefault();
            if (app is not { Path.Length: > 0 }) return;
            _appsPopup.IsOpen = false;
            Apply((list, id) => ActionEdits.AddStep(list, id, new OpenStep { Target = app.Path }, app.Name));
        }
        list.MouseDoubleClick += (_, _) => Choose();
        // PreviewKeyDown : le champ de recherche de la bibliothèque d'interface garde Échap pour lui.
        search.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Choose(); e.Handled = true; }
            else if (e.Key == Key.Down && list.Items.Count > 0) { list.SelectedIndex = 0; ((UIElement?)list.ItemContainerGenerator.ContainerFromIndex(0))?.Focus(); e.Handled = true; }
            else if (e.Key == Key.Escape) { _appsPopup.IsOpen = false; e.Handled = true; }
        };
        list.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Choose(); e.Handled = true; }
            else if (e.Key == Key.Escape) { _appsPopup.IsOpen = false; e.Handled = true; }
        };
    }

    private FrameworkElement SymbolGrid()
    {
        var grid = new UniformGrid { Columns = 8, Margin = new Thickness(8) };
        var automatic = new Button { Content = "Auto", Width = 32, Height = 32, Padding = new Thickness(0), FontSize = 10, Margin = new Thickness(1),
            ToolTip = "Icône réelle de ce que le bouton ouvre" };
        automatic.Click += (_, _) =>
        {
            _symbolPopup.IsOpen = false;
            Apply((list, id) => ActionEdits.UseAutomaticIcon(list, id));
        };
        grid.Children.Add(automatic);
        foreach (var symbol in Symbols)
        {
            var button = new Button { Content = symbol, Width = 32, Height = 32, Padding = new Thickness(0), FontSize = 15, Margin = new Thickness(1) };
            button.Click += (_, _) =>
            {
                _symbolPopup.IsOpen = false;
                Apply((list, id) => ActionEdits.SetIcon(list, id, symbol));
            };
            grid.Children.Add(button);
        }
        // Sous les symboles : une image à soi, ou l'icône d'un autre fichier (appli, raccourci…).
        var image = new Button { Content = "Image…", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0),
            ToolTip = "PNG, JPG, ICO, BMP ou GIF ; copiée dans les données d'OrbitWeave" };
        image.Click += (_, _) =>
        {
            _symbolPopup.IsOpen = false;
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Image de l'icône", Filter = "Images|*.png;*.jpg;*.jpeg;*.ico;*.bmp;*.gif" };
            if (dialog.ShowDialog(this) != true) return;
            string file;
            try { file = CustomIcons.Import(dialog.FileName, ActionStore.DirectoryPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                Notifications.Show("Image impossible à utiliser", ex.Message);
                return;
            }
            Apply((list, id) => ActionEdits.SetIconFile(list, id, file));
        };
        var fromFile = new Button { Content = "Icône d'un fichier…", Padding = new Thickness(10, 3, 10, 3),
            ToolTip = "L'icône d'une appli, d'un raccourci ou d'un dossier, même s'il n'est pas ce que le bouton ouvre" };
        fromFile.Click += (_, _) =>
        {
            _symbolPopup.IsOpen = false;
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Fichier dont prendre l'icône", DereferenceLinks = false };
            if (dialog.ShowDialog(this) == true) Apply((list, id) => ActionEdits.SetIconFile(list, id, dialog.FileName));
        };
        var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(9, 0, 9, 9) };
        row.Children.Add(image);
        row.Children.Add(fromFile);
        var content = new StackPanel();
        content.Children.Add(grid);
        content.Children.Add(_library);
        content.Children.Add(row);
        var border = new Border { Child = content, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
        border.SetResourceReference(Border.BackgroundProperty, "SettingsCardBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "SettingsBorderBrush");
        return border;
    }

    // « Mes icônes » (images ajoutées dans les réglages ou par « Image… ») : un clic la met sur le bouton.
    private readonly WrapPanel _library = new() { Margin = new Thickness(9, 0, 9, 8), MaxWidth = 290 };

    private void FillLibrary()
    {
        _library.Children.Clear();
        var files = CustomIcons.List(ActionStore.DirectoryPath);
        _library.Visibility = files.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        foreach (var file in files)
        {
            var image = new System.Windows.Controls.Image { Width = 22, Height = 22, Stretch = System.Windows.Media.Stretch.Uniform };
            _ = LoadLibraryImage(image, System.IO.Path.Combine(ActionStore.DirectoryPath, file));
            var button = new Button { Content = image, Width = 32, Height = 32, Padding = new Thickness(0), Margin = new Thickness(1), ToolTip = "Mes icônes" };
            button.Click += (_, _) =>
            {
                _symbolPopup.IsOpen = false;
                Apply((list, id) => ActionEdits.SetIconFile(list, id, file));
            };
            _library.Children.Add(button);
        }
    }

    private static async Task LoadLibraryImage(System.Windows.Controls.Image image, string path) =>
        image.Source = await Orbit.AppIcons.LoadAsync(new IconTarget(IconKind.Image, path));

    private void ClosePopups()
    {
        _symbolPopup.IsOpen = false;
        _appsPopup.IsOpen = false;
    }

    // Raccourcis de l'éditeur (Ctrl+Z, Ctrl+Y, Échap…) quand on n'est pas en train d'écrire.
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) return;
        _keys(e);
    }

    internal static TextBlock Muted(string text, Thickness margin)
    {
        var block = new TextBlock { Text = text, FontSize = 12, Margin = margin, TextWrapping = TextWrapping.Wrap };
        block.SetResourceReference(TextBlock.ForegroundProperty, "SettingsMutedBrush");
        return block;
    }
}
