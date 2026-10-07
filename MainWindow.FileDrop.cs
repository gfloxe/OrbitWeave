using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using OrbitWeave.Actions;
using OrbitWeave.Orbit;
using OrbitWeave.Ui;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using IDataObject = System.Windows.IDataObject;
using Brush = System.Windows.Media.Brush;
using Rectangle = System.Windows.Shapes.Rectangle;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace OrbitWeave;

// Glisser-déposer d'un fichier, d'une appli, d'un dossier ou d'un lien sur la roue (mode Modifier ou non).
// Sur un bouton : « Remplacer » à gauche, « + Ajouter » à droite ; sur une section : elle s'ouvre après ½ s ;
// dans le vide pendant qu'une branche est ouverte : un nouveau bouton dans cette section.
public partial class MainWindow
{
    private enum DropZone { None, Replace, Add, Create }

    private Canvas? _dropOverlay;
    private readonly DispatcherTimer _dropHoverTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private string? _dropHoverId;
    private (DropZone Zone, Guid? Card, string? Section) _dropTarget;
    // Fenêtre à transparence par pixel : là où rien n'est dessiné, Windows ne la voit pas et un fichier
    // glissé passe au travers. Pendant un glisser venu d'ailleurs, un fond presque invisible la rend visible partout.
    private readonly DispatcherTimer _dragWatch = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private static readonly Brush DragCatcher = CreateDragCatcher();
    private bool _catchingDrag;

    private static Brush CreateDragCatcher()
    {
        var brush = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        brush.Freeze();
        return brush;
    }

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);

    // Un glisser venu d'ailleurs (Explorateur, navigateur…) passe sur la roue : bouton principal enfoncé,
    // sans que la roue ait la souris. Ses zones vides doivent alors rester une cible de dépôt.
    // La roue appartient au bureau (Progman) : Windows partage alors l'état de la souris avec l'Explorateur,
    // et un glisser parti du bureau apparaît comme une capture… de l'Explorateur. Seule une capture de la roue compte.
    private static bool IsForeignDragInProgress()
    {
        var button = GetSystemMetrics(23) != 0 ? 0x02 : 0x01; // SM_SWAPBUTTON : bouton principal à droite
        if ((GetAsyncKeyState(button) & 0x8000) == 0) return false;
        var capture = GetCapture();
        if (capture == IntPtr.Zero) return true;
        GetWindowThreadProcessId(capture, out var process);
        return process != (uint)Environment.ProcessId;
    }

    private void StartFileDrop()
    {
        AllowDrop = true;
        _dropOverlay = new Canvas { IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        System.Windows.Controls.Panel.SetZIndex(_dropOverlay, 1000);
        Root.Children.Add(_dropOverlay);
        _dropHoverTimer.Tick += (_, _) => { _dropHoverTimer.Stop(); OpenHovered(); };
        DragEnter += (_, e) => OnDrag(e);
        DragOver += (_, e) => OnDrag(e);
        DragLeave += (_, _) => EndDrag();
        Drop += OnDrop;
        _dragWatch.Tick += (_, _) => WatchForeignDrag();
        IsVisibleChanged += (_, _) => { if (IsVisible) _dragWatch.Start(); else { _dragWatch.Stop(); CatchDrag(false); } };
        if (IsVisible) _dragWatch.Start();
    }

    private DispatcherTimer? _dragRelease;

    private DispatcherTimer CreateDragRelease()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_catchingDrag) return;
            Root.Background = null;
            EndDrag();
        };
        return timer;
    }

    private void WatchForeignDrag()
    {
        if (_source is not { IsDisposed: false } source) return;
        var dragging = IsForeignDragInProgress();
        if (dragging && !_catchingDrag)
        {
            if (!GetCursorPos(out var cursor) || !GetWindowRect(source.Handle, out var rect) ||
                cursor.X < rect.Left || cursor.X >= rect.Right || cursor.Y < rect.Top || cursor.Y >= rect.Bottom) return;
            CatchDrag(true);
        }
        else if (!dragging && _catchingDrag) CatchDrag(false);
    }

    private void CatchDrag(bool on)
    {
        if (_catchingDrag == on) return;
        _catchingDrag = on;
        if (on)
        {
            _dragRelease?.Stop();
            Root.Background = DragCatcher;
            SetClickThrough(false);
        }
        else
        {
            // Bouton relâché : Windows livre le dépôt juste après. La cible reste prête un instant,
            // c'est le dépôt (ou la sortie) qui la nettoie ; sinon, ce délai le fait.
            _dragRelease ??= CreateDragRelease();
            _dragRelease.Stop();
            _dragRelease.Start();
        }
        if (_traceTimer is not null) Trace(on ? "glisser venu d'ailleurs : roue cible de dépôt" : "fin du glisser");
    }

    private void OnDrag(DragEventArgs e)
    {
        e.Handled = true;
        if (_dropOverlay is null || DroppedItems(e.Data) is not { Count: > 0 })
        {
            e.Effects = DragDropEffects.None;
            EndDrag();
            return;
        }
        _idleReset.Stop();
        AnimateIdleOpacity(1);
        var point = e.GetPosition(OrbitCanvas);
        var (id, element) = DropElementAt(point);
        HoverForOpening(id);
        _dropTarget = (DropZone.None, null, null);
        if (id?.StartsWith("wheel.card:", StringComparison.Ordinal) == true && element is not null &&
            Guid.TryParse(id["wheel.card:".Length..], out var card) && _actions.FirstOrDefault(a => a.Id == card) is { } action)
        {
            var bounds = element.TransformToAncestor(OrbitCanvas).TransformBounds(new Rect(element.RenderSize));
            var zone = action.Steps.Count == 0 || point.X < bounds.Left + bounds.Width / 2 ? DropZone.Replace : DropZone.Add;
            if (action.Steps.Count == 0) zone = DropZone.Add;
            _dropTarget = (zone, card, action.Group);
            DrawDropZones(bounds, zone, action.Steps.Count == 0);
        }
        else if (id is null && _activeGroup is { } open)
        {
            _dropTarget = (DropZone.Create, null, open);
            DrawCreateHint(point, open);
        }
        else DrawRingOnly();
        e.Effects = _dropTarget.Zone == DropZone.None ? DragDropEffects.None : DragDropEffects.Copy;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var target = _dropTarget;
        var items = DroppedItems(e.Data);
        EndDrag();
        if (target.Zone == DropZone.None || items is not { Count: > 0 }) return;
        var (steps, missing) = DropEdits.StepsFor(items, path => File.Exists(path) || Directory.Exists(path));
        if (missing.Count > 0 || steps.Count == 0)
        {
            Notifications.Show("Impossible d'ajouter", missing.Count > 0
                ? $"« {System.IO.Path.GetFileName(missing[0].TrimEnd('\\'))} » est introuvable. Rien n'a été modifié."
                : "Ce qui a été déposé n'est ni un fichier, ni un dossier, ni un lien web.");
            return;
        }
        var what = steps.Count == 1 ? $"« {DropEdits.NameFor(steps[0])} »" : $"{steps.Count} éléments";
        switch (target.Zone)
        {
            case DropZone.Replace when target.Card is { } card:
                ApplyDrop(DropEdits.Replace(_actions, card, steps), $"{ButtonName(card)} lance maintenant {what}");
                break;
            case DropZone.Add when target.Card is { } card:
                ApplyDrop(DropEdits.Add(_actions, card, steps), $"{what} ajouté à {ButtonName(card)}");
                break;
            case DropZone.Create when target.Section is { } section:
                var (created, _, error) = DropEdits.Create(_actions, section, steps);
                if (error is not null) { Notifications.Show("Impossible d'ajouter un bouton", error); return; }
                ApplyDrop(created, $"Nouveau bouton {what} dans « {section} »");
                break;
        }
    }

    private string ButtonName(Guid card) => $"« {_actions.FirstOrDefault(a => a.Id == card)?.Name ?? "le bouton"} »";

    // Éditeur ouvert : la modification passe par son historique (Ctrl+Z). Sinon : écrite tout de suite.
    // Dans les deux cas, « Annuler » dans la notification remet la liste d'avant.
    private void ApplyDrop(List<ActionItem> updated, string message)
    {
        var previous = ActionEdits.Clone(_actions);
        if (!WriteActions(updated)) return;
        Notifications.Show("Bouton modifié", message, "Annuler", () => WriteActions(previous));
    }

    private bool WriteActions(List<ActionItem> actions)
    {
        if (_wheelEditor is { } editor)
        {
            editor.ApplyActionsFromWheel(actions);
            return true;
        }
        try
        {
            ActionStore.Save(actions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Notifications.Show("Impossible d'enregistrer", ex.Message);
            return false;
        }
        ApplyActions(actions);
        return true;
    }

    // Section survolée ½ s : sa branche s'ouvre ; le rond du milieu replié se déplie.
    private void HoverForOpening(string? id)
    {
        var opener = id is not null && (id == "wheel.hub" || id.StartsWith("wheel.section:", StringComparison.Ordinal)) ? id : null;
        if (opener == _dropHoverId) return;
        _dropHoverId = opener;
        _dropHoverTimer.Stop();
        if (opener is not null) _dropHoverTimer.Start();
    }

    private void OpenHovered()
    {
        if (_dropHoverId == "wheel.hub")
        {
            if (_folded) { _folded = false; RenderOrbit(); }
            return;
        }
        if (_dropHoverId?["wheel.section:".Length..] is { } name && _groupInfos.TryGetValue(name, out var group))
            ExpandGroup(group.Info, group.Angle);
    }

    // Élément de la roue sous le curseur, calque d'édition ignoré : bouton, section ou rond du milieu.
    private (string? Id, FrameworkElement? Element) DropElementAt(Point canvasPoint)
    {
        for (var node = OrbitCanvas.InputHitTest(canvasPoint) as DependencyObject; node is not null;
            node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
        {
            if (node is not FrameworkElement element || UiId.Get(element) is not { } id) continue;
            if (id.StartsWith("wheel.card:", StringComparison.Ordinal) || id.StartsWith("wheel.section:", StringComparison.Ordinal) ||
                id == "wheel.hub" || id == "wheel.hub.hit")
                return (id == "wheel.hub.hit" ? "wheel.hub" : id, element);
            if (id.StartsWith("wheel", StringComparison.Ordinal) && !id.StartsWith("wheel.card.", StringComparison.Ordinal) &&
                !id.StartsWith("wheel.section.", StringComparison.Ordinal)) return (id, element);
        }
        return (null, null);
    }

    private static List<string>? DroppedItems(IDataObject data)
    {
        try
        {
            if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files) return files.ToList();
            foreach (var format in new[] { "UniformResourceLocatorW", DataFormats.UnicodeText, DataFormats.Text })
            {
                if (!data.GetDataPresent(format)) continue;
                var value = data.GetData(format) switch
                {
                    string text => text,
                    MemoryStream stream => System.Text.Encoding.Unicode.GetString(stream.ToArray()).TrimEnd('\0'),
                    _ => null
                };
                var line = value?.Split('\n')[0].Trim();
                if (line is not null && Uri.TryCreate(line, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") return [line];
            }
        }
        catch (Exception ex) when (ex is COMException or OutOfMemoryException or InvalidOperationException) { }
        return null;
    }

    private void EndDrag()
    {
        _dropHoverTimer.Stop();
        _dropHoverId = null;
        _dropTarget = (DropZone.None, null, null);
        _dropOverlay?.Children.Clear();
    }

    // Calque aligné sur le canevas de la roue (mêmes coordonnées, même échelle).
    private Canvas PrepareOverlay()
    {
        var overlay = _dropOverlay!;
        overlay.Width = OrbitCanvas.Width;
        overlay.Height = OrbitCanvas.Height;
        overlay.Margin = OrbitCanvas.Margin;
        overlay.RenderTransform = OrbitCanvas.RenderTransform;
        overlay.Children.Clear();
        var center = WheelCenter;
        var radius = _wheelGeometry.Sections.Count == 0 ? OrbitLayout.HubDiameter
            : _wheelGeometry.Sections.Max(s => Math.Sqrt(Math.Pow(s.Center.X - center.X, 2) + Math.Pow(s.Center.Y - center.Y, 2))) + OrbitLayout.SectionDiameter / 2 + 24;
        var ring = new Ellipse { Width = radius * 2, Height = radius * 2, StrokeThickness = 3, Stroke = Accent(0xB0),
            Fill = Accent(0x14), Effect = null };
        Canvas.SetLeft(ring, center.X - radius);
        Canvas.SetTop(ring, center.Y - radius);
        overlay.Children.Add(ring);
        return overlay;
    }

    private void DrawRingOnly() => PrepareOverlay();

    private void DrawDropZones(Rect card, DropZone zone, bool empty)
    {
        var overlay = PrepareOverlay();
        if (empty)
        {
            overlay.Children.Add(Zone(card, "Ouvrir ceci", true));
            return;
        }
        var half = card.Width / 2;
        overlay.Children.Add(Zone(new Rect(card.Left, card.Top, half - 2, card.Height), "Remplacer", zone == DropZone.Replace));
        overlay.Children.Add(Zone(new Rect(card.Left + half + 2, card.Top, half - 2, card.Height), "+ Ajouter", zone == DropZone.Add));
    }

    private void DrawCreateHint(Point point, string section)
    {
        var overlay = PrepareOverlay();
        var pill = new Border
        {
            CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 5, 12, 5), Background = Accent(0xE6),
            Child = new TextBlock { Text = $"+ Nouveau bouton dans « {section} »", Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold }
        };
        // À droite du curseur, ou à gauche s'il sortirait de la fenêtre de la roue.
        pill.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        var right = OrbitCanvas.TranslatePoint(new Point(point.X + 18 + pill.DesiredSize.Width, point.Y), Root).X;
        Canvas.SetLeft(pill, right > Root.ActualWidth - 8 ? point.X - 18 - pill.DesiredSize.Width : point.X + 18);
        Canvas.SetTop(pill, point.Y + 14);
        overlay.Children.Add(pill);
    }

    private FrameworkElement Zone(Rect bounds, string label, bool active)
    {
        var zone = new Border
        {
            Width = Math.Max(0, bounds.Width), Height = Math.Max(0, bounds.Height), CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(2), BorderBrush = Accent(active ? (byte)0xFF : (byte)0x80),
            Background = active ? Accent(0xD0) : new SolidColorBrush(Color.FromArgb(0xB0, 0x20, 0x20, 0x24)),
            Child = new TextBlock { Text = label, Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        Canvas.SetLeft(zone, bounds.Left);
        Canvas.SetTop(zone, bounds.Top);
        return zone;
    }

    private Brush Accent(byte alpha)
    {
        var accent = AccentColor;
        var brush = new SolidColorBrush(Color.FromArgb(alpha, accent.R, accent.G, accent.B));
        brush.Freeze();
        return brush;
    }
}
