using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using OrbitWeave.Actions;
using OrbitWeave.Orbit;
using OrbitWeave.Ui;
using Button = System.Windows.Controls.Button;
using MenuItem = System.Windows.Controls.MenuItem;
using ContextMenu = System.Windows.Controls.ContextMenu;

namespace OrbitWeave;

// Mode Modifier : clic droit sur un bouton ou une section, et ce que ces menus font.
// Les actions passent par WriteActions (historique de l'éditeur) ; détacher / recoller par la disposition.
public partial class MainWindow
{
    private void StartWheelMenus()
    {
        OrbitCanvas.PreviewMouseRightButtonUp += OrbitCanvas_EditRightButtonUp;
        // La roue appartient au bureau : un clic droit qui l'active fait repasser le bureau devant elle un instant,
        // et le relâcher partait au bureau (menu de Windows au lieu du sien). La roue garde la souris jusqu'au relâcher,
        // comme un bouton le fait pour le clic gauche.
        // C'est l'élément cliqué qui la garde : le menu vise toujours ce qui est sous la souris.
        PreviewMouseRightButtonDown += (_, e) =>
        {
            if (Mouse.Captured is null && e.OriginalSource is UIElement element && element.CaptureMouse()) _rightCapture = element;
        };
        PreviewMouseRightButtonUp += (_, _) =>
        {
            if (_rightCapture is not { } element) return;
            _rightCapture = null;
            if (ReferenceEquals(Mouse.Captured, element)) element.ReleaseMouseCapture();
        };
    }

    private UIElement? _rightCapture;

    private void OrbitCanvas_EditRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_wheelEditMode || WheelGestureActive) return;
        var hit = e.OriginalSource as DependencyObject;
        if (PreviewAt(hit) is { } preview)
        {
            if (ActivatePreview(preview) is not { } real) return;
            hit = real;
        }
        var (id, element) = MenuTargetAt(hit);
        ContextMenu? menu = null;
        var previous = _editSelection;
        if (id?.StartsWith("wheel.card:", StringComparison.Ordinal) == true && element is Button { Tag: ActionItem action })
        {
            Pick(id);
            menu = CardMenu(action);
        }
        else if (id?.StartsWith("wheel.section:", StringComparison.Ordinal) == true &&
                 _groupInfos.TryGetValue(id["wheel.section:".Length..], out var group))
        {
            if (!string.Equals(_activeGroup, group.Info.Name, StringComparison.OrdinalIgnoreCase)) ExpandGroup(group.Info, group.Angle);
            Pick(id);
            menu = SectionMenu(group.Info.Name);
        }
        if (menu is null) return;
        e.Handled = true;
        menu.PlacementTarget = element;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        if (previous == _editSelection) { menu.IsOpen = true; return; }
        // Nouveau bouton ou nouvelle section sélectionnés : l'éditeur ouvre sa bulle (≈ 100 ms). Le menu s'ouvre après, pour rester devant elle.
        var delay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        delay.Tick += (_, _) => { delay.Stop(); if (_wheelEditMode) menu.IsOpen = true; };
        delay.Start();
    }

    // Carte (pas une de ses parties) ou section sous le clic.
    private static (string? Id, FrameworkElement? Element) MenuTargetAt(DependencyObject? node)
    {
        for (; node is not null; node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
        {
            if (node is not FrameworkElement element || UiId.Get(element) is not { } id) continue;
            if (id.StartsWith("wheel.card:", StringComparison.Ordinal) || id.StartsWith("wheel.section:", StringComparison.Ordinal)) return (id, element);
            if (id.StartsWith("wheel.label:", StringComparison.Ordinal)) return ($"wheel.section:{id["wheel.label:".Length..]}", element);
        }
        return (null, null);
    }

    private ContextMenu CardMenu(ActionItem action)
    {
        var menu = WheelMenu();
        AddEntry(menu, "▶  Tester", () => _ = TestAction(action));
        AddEntry(menu, "⧉  Dupliquer", () =>
        {
            var (list, id, error) = ActionEdits.Duplicate(_actions, action.Id);
            ApplyAdded(list, id, error, action.Group, action.ParentId);
        });
        AddEntry(menu, "↳  Ajouter une sous-carte", () =>
        {
            var (list, id, error) = ActionEdits.NewButton(_actions, action.Group, action.Id);
            ApplyAdded(list, id, error, action.Group, action.Id);
        });
        if (action.ParentId is null && _cardSlots.TryGetValue(action.Id, out var slot))
        {
            if (slot.Group is { } groupId) AddEntry(menu, "⇤  Recoller à la pile", () => Reattach(groupId));
            else if (slot.Index > 0) AddEntry(menu, "⇥  Détacher", () => DetachCard(action));
        }
        menu.Items.Add(new Separator());
        AddEntry(menu, "🗑  Supprimer", () => DeleteCard(action));
        return menu;
    }

    private ContextMenu SectionMenu(string section)
    {
        var menu = WheelMenu();
        AddEntry(menu, "+  Ajouter un bouton", () => AddButton(section));
        menu.Items.Add(KnownButtonsMenu(section));
        AddEntry(menu, "▥  Ajouter une colonne", () => AddColumn(section));
        AddEntry(menu, "✎  Renommer", () => _wheelEditor?.FocusSectionName(section));
        menu.Items.Add(new Separator());
        AddEntry(menu, "🗑  Supprimer la section…", () => DeleteSection(section));
        return menu;
    }

    // « Ajouter un bouton connu » : boutons d'origine, outils de Windows, applis installées (lues au premier survol).
    // Ce qui est déjà dans la section est coché et grisé.
    private MenuItem KnownButtonsMenu(string section)
    {
        var root = new MenuItem { Header = "★  Ajouter un bouton connu", Foreground = Brushes.White };
        MenuItem Entry(CatalogEntry entry)
        {
            var present = ButtonCatalog.IsIn(_actions, section, entry.Target);
            var item = new MenuItem { Header = $"{entry.Icon}  {entry.Name}", Foreground = Brushes.White, IsEnabled = !present, IsChecked = present };
            item.Click += (_, _) =>
            {
                var (list, id, error) = ButtonCatalog.Add(_actions, section, entry);
                ApplyAdded(list, id, error, section, null);
            };
            return item;
        }
        MenuItem Category(string header, IEnumerable<CatalogEntry> entries)
        {
            var category = new MenuItem { Header = header, Foreground = Brushes.White };
            foreach (var entry in entries) category.Items.Add(Entry(entry));
            return category;
        }
        root.Items.Add(Category("Boutons d'origine", ButtonCatalog.Originals()));
        root.Items.Add(Category("Outils Windows", ButtonCatalog.WindowsTools));
        var apps = new MenuItem { Header = "Applis installées", Foreground = Brushes.White };
        apps.Items.Add(new MenuItem { Header = "Chargement…", Foreground = Brushes.White, IsEnabled = false });
        var loaded = false;
        apps.SubmenuOpened += async (_, _) =>
        {
            if (loaded) return;
            loaded = true;
            var list = await InstalledApps.Cached;
            apps.Items.Clear();
            // Des centaines d'applis : une entrée par initiale, chacune avec ses applis.
            foreach (var letter in list.Where(a => a.Path.Length > 0).GroupBy(a => ButtonCatalog.Initial(a.Name)).OrderBy(g => g.Key == "#" ? "~" : g.Key, StringComparer.Ordinal))
                apps.Items.Add(Category(letter.Key, letter.Select(app => new CatalogEntry(app.Name, "✦", app.Path))));
            if (apps.Items.Count == 0) apps.Items.Add(new MenuItem { Header = "Aucune appli trouvée", Foreground = Brushes.White, IsEnabled = false });
        };
        root.Items.Add(apps);
        return root;
    }

    private ContextMenu WheelMenu() => new()
    {
        Background = new SolidColorBrush(SurfaceColor), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(AccentColor)
    };

    private static void AddEntry(ContextMenu menu, string header, Action run, bool enabled = true)
    {
        var item = new MenuItem { Header = header, Foreground = Brushes.White, IsEnabled = enabled };
        item.Click += (_, _) => run();
        menu.Items.Add(item);
    }

    private void AddButton(string section)
    {
        var (list, id, error) = ActionEdits.NewButton(_actions, section);
        ApplyAdded(list, id, error, section, null);
    }

    // Nouveau bouton : écrit, puis sélectionné (sa bulle s'ouvre) une fois la roue redessinée.
    private void ApplyAdded(List<ActionItem> list, Guid? id, string? error, string section, Guid? parent)
    {
        if (error is not null) { Notifications.Show("Impossible d'ajouter", error); return; }
        if (id is not { } added || !WriteActions(list)) return;
        Dispatcher.BeginInvoke(() => SelectCard(section, added, parent), DispatcherPriority.Loaded);
    }

    private void SelectCard(string section, Guid card, Guid? parent)
    {
        if (_groupInfos.TryGetValue(section, out var group) &&
            !string.Equals(_activeGroup, group.Info.Name, StringComparison.OrdinalIgnoreCase))
            ExpandGroup(group.Info, group.Angle);
        // Sous-carte : on ouvre le chemin jusqu'à son parent.
        if (parent is { } parentId)
            foreach (var ancestor in ActionTree.Ancestors(_actions, card))
                if (OpenCardButton(ancestor.Id) is { } button) ExpandCard(ancestor, button);
        Pick($"wheel.card:{card}");
    }

    private void Pick(string id)
    {
        if (id != _editSelection) WheelElementPicked?.Invoke(id);
        _editSelection = id;
        DrawEditSelection();
    }

    private void DeleteCard(ActionItem action)
    {
        var previous = ActionEdits.Clone(_actions);
        var children = ActionTree.Descendants(_actions, action.Id).Count;
        var section = action.Group;
        Pick($"wheel.section:{section.Trim()}");
        if (!WriteActions(ActionEdits.Remove(_actions, action.Id))) return;
        var what = children > 0 ? $"« {action.Name} » et ses {children} sous-carte{(children > 1 ? "s" : "")}" : $"« {action.Name} »";
        Notifications.Show("Supprimé", what, "Annuler", () => WriteActions(previous));
    }

    private void DeleteSection(string section)
    {
        var count = _actions.Count(a => a.Group.Trim().Equals(section.Trim(), StringComparison.OrdinalIgnoreCase));
        var answer = System.Windows.MessageBox.Show(this,
            $"Supprimer la section « {section} » et ses {count} bouton{(count > 1 ? "s" : "")} ?",
            "OrbitWeave", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK) return;
        var previous = ActionEdits.Clone(_actions);
        _editSelection = null;
        DrawEditSelection();
        if (!WriteActions(ActionEdits.RemoveSection(_actions, section))) return;
        Notifications.Show("Supprimé", $"La section « {section} »", "Annuler", () => WriteActions(previous));
    }

    // Détacher : la carte seule quitte la pile (les suivantes remontent) et se pose à côté, une largeur de carte plus loin.
    private void DetachCard(ActionItem action)
    {
        if (_activeGroup is not { } section || OpenCardButton(action.Id) is not { } button) return;
        var layout = UiRuntime.Profile.Layout.Clone();
        PinMainStack(layout, section);
        var away = _branchSide < 0 ? -1 : 1;
        var x = Canvas.GetLeft(button) - _branchNode.X + away * (Token("ActionCardWidth") + 24);
        var y = Canvas.GetTop(button) + Token("ActionCardHeight") / 2 - _branchNode.Y;
        CardGroups.Detach(layout, section, action.Id, x, y);
        WheelLayoutProposed?.Invoke(layout);
    }

    private void Reattach(string groupId)
    {
        var layout = UiRuntime.Profile.Layout.Clone();
        if (_activeGroup is { } section) PinMainStack(layout, section);
        CardGroups.Merge(layout, groupId, null, true);
        WheelLayoutProposed?.Invoke(layout);
    }

    // ---------- Les + ----------

    // Un + : nouveau bouton en bas de la pile (Parent null) ou nouvelle sous-carte (Parent = la carte).
    private sealed record AddRequest(string Section, Guid? Parent);
    private const string DisabledPlus = "plus-disabled";

    private Border PlusBadge(object request, bool enabled, string tooltip) => new()
    {
        Width = 24, Height = 24, CornerRadius = new CornerRadius(12),
        Background = BrushToken("CardGlassBrush"), BorderBrush = BrushToken("ConnectorBrush"), BorderThickness = new Thickness(1),
        Cursor = enabled ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow, ToolTip = tooltip, Opacity = enabled ? 1 : 0.5,
        Tag = enabled ? request : DisabledPlus, // grisé : il garde le clic (rien ne se passe) et montre son info-bulle
        Child = new TextBlock { Text = "+", FontSize = 15, Foreground = BrushToken("WidgetPrimaryTextBrush"),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, -2, 0, 0), IsHitTestVisible = false }
    };

    // + sous la dernière carte de la pile ouverte (au-dessus de la première quand la pile monte).
    private void AddPlusIfEditing(string section, double left, double firstY, int count, bool upward)
    {
        if (!_wheelEditMode || count == 0) return;
        var plus = PlusBadge(new AddRequest(section, null), true, "Ajouter un bouton");
        var pitch = Token("ActionCardPitch");
        var y = upward ? firstY - pitch * 0.5 - Token("ActionCardHeight") / 2 + 2 : firstY + (count - 1) * pitch + Token("ActionCardHeight") / 2 + 8;
        Canvas.SetLeft(plus, Math.Round(left + Token("ActionCardWidth") / 2 - 12));
        Canvas.SetTop(plus, Math.Round(upward ? y - 24 : y));
        System.Windows.Controls.Panel.SetZIndex(plus, int.MaxValue);
        OrbitCanvas.Children.Add(plus);
        _expandedElements.Add(plus);
    }

    // + de sous-carte, sur le côté extérieur de la carte sélectionnée (calque d'édition).
    private void AddSubCardPlus(Rect bounds, ActionItem action)
    {
        var request = new AddRequest(action.Group, action.Id);
        var plus = PlusBadge(request, true, "Ajouter une sous-carte");
        var outward = bounds.Left + bounds.Width / 2 < WheelCenter.X ? -1 : 1;
        Canvas.SetLeft(plus, Math.Round(outward < 0 ? bounds.Left - 34 : bounds.Right + 10));
        Canvas.SetTop(plus, Math.Round(bounds.Top + bounds.Height / 2 - 12));
        plus.PreviewMouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            RunAdd(request);
        };
        EditOverlay.Children.Add(plus);
    }

    private bool TryAddAt(DependencyObject? hit)
    {
        for (; hit is not null && !ReferenceEquals(hit, OrbitCanvas); hit = VisualTreeHelper.GetParent(hit))
        {
            if (hit is FrameworkElement { Tag: AddRequest request }) { RunAdd(request); return true; }
            if (hit is FrameworkElement { Tag: RingAddRequest ring }) { AddSectionAt(ring); return true; }
            if (hit is FrameworkElement { Tag: ColumnRequest column }) { AddColumn(column.Section); return true; }
            if (hit is FrameworkElement { Tag: GroupAddRequest inGroup }) { AddToGroup(inGroup); return true; }
        }
        return false;
    }

    private void RunAdd(AddRequest request)
    {
        var (list, id, error) = ActionEdits.NewButton(_actions, request.Section, request.Parent);
        ApplyAdded(list, id, error, request.Section, request.Parent);
    }

    // ---------- Déplacer vers une autre section ----------

    // Pendant le glisser d'une carte : la section (nœud ou aperçu) sous le curseur, si ce n'est pas déjà la sienne.
    // Elle s'entoure de rose ; au lâcher (final), la carte y sera déplacée par l'éditeur.
    public string? CardDropSection(string id, bool final)
    {
        string? found = null;
        if (id.StartsWith("wheel.card:", StringComparison.Ordinal) && Guid.TryParse(id["wheel.card:".Length..], out var card) &&
            _actions.FirstOrDefault(a => a.Id == card) is { } action && GetCursorPos(out var cursor))
        {
            var point = OrbitCanvas.PointFromScreen(new Point(cursor.X, cursor.Y));
            bool Own(string section) => action.ParentId is null && section.Trim().Equals(action.Group.Trim(), StringComparison.OrdinalIgnoreCase);
            foreach (var (name, button) in _groupButtons)
            {
                var center = new Point(Canvas.GetLeft(button) + button.Width / 2, Canvas.GetTop(button) + button.Height / 2);
                if (!Own(name) && (point - center).Length <= button.Width / 2 + 10) { found = name; break; }
            }
            if (found is null)
                foreach (var preview in _branchPreviews.OfType<Button>())
                    if (preview.Tag is BranchPreview { Section: var section } && !Own(section) &&
                        new Rect(Canvas.GetLeft(preview), Canvas.GetTop(preview), preview.ActualWidth, preview.ActualHeight).Contains(point))
                    { found = section; break; }
        }
        ShowSectionTarget(final ? null : found);
        if (final && found is not null)
        {
            _cardDrag = null;
            ShowMagnet(null);
        }
        return found;
    }

    private void ShowSectionTarget(string? section)
    {
        EditOverlay.Children.OfType<FrameworkElement>().Where(item => item.Tag as string == "section-target").ToList().ForEach(EditOverlay.Children.Remove);
        if (section is null || !_groupButtons.TryGetValue(section, out var button)) return;
        var size = button.Width + 16;
        var ring = new System.Windows.Shapes.Ellipse
        {
            Width = size, Height = size, Tag = "section-target", IsHitTestVisible = false,
            Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0x3E, 0xA5)), StrokeThickness = 2, StrokeDashArray = [4, 3]
        };
        Canvas.SetLeft(ring, Canvas.GetLeft(button) + button.Width / 2 - size / 2);
        Canvas.SetTop(ring, Canvas.GetTop(button) + button.Height / 2 - size / 2);
        EditOverlay.Children.Add(ring);
    }
}
