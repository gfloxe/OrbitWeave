using System.Windows;
using System.Windows.Controls;
using OrbitWeave.Actions;
using OrbitWeave.Orbit;
using OrbitWeave.Ui;
using Panel = System.Windows.Controls.Panel;

namespace OrbitWeave;

// Mode Modifier : un + entre deux sections voisines sur l'anneau crée une section à cet endroit.
public partial class MainWindow
{
    // After : la section qui précède dans l'ordre des actions ; At : centre du nouveau nœud sur la toile.
    private sealed record RingAddRequest(string After, Point At);

    private readonly List<UIElement> _ringPlus = [];

    private void DrawRingPlus()
    {
        foreach (var element in _ringPlus) OrbitCanvas.Children.Remove(element);
        _ringPlus.Clear();
        if (!_wheelEditMode || _folded || _folding || WheelGestureActive) return;
        var hub = WheelCenter;
        double Angle(WheelSectionGeometry section) => Math.Atan2(section.Center.Y - hub.Y, section.Center.X - hub.X);
        double Radius(WheelSectionGeometry section) => Math.Sqrt(Math.Pow(section.Center.X - hub.X, 2) + Math.Pow(section.Center.Y - hub.Y, 2));
        var sections = _wheelGeometry.Sections.OrderBy(Angle).ToList();
        if (sections.Count == 0) return;
        var diameter = OrbitLayout.SectionDiameter;
        for (var i = 0; i < sections.Count; i++)
        {
            var (from, to) = (sections[i], sections[(i + 1) % sections.Count]);
            var start = Angle(from);
            var end = Angle(to);
            if (end <= start) end += Math.PI * 2;
            var middle = (start + end) / 2;
            var radius = (Radius(from) + Radius(to)) / 2;
            var at = new Point(hub.X + Math.Cos(middle) * radius, hub.Y + Math.Sin(middle) * radius);
            // Pas de + là où une section neuve chevaucherait ses voisines.
            if (sections.Any(section => (ToPoint(section.Center) - at).Length < diameter * 0.9)) continue;
            var plus = PlusBadge(new RingAddRequest(from.Name, at), true, "Ajouter une section ici");
            Canvas.SetLeft(plus, Math.Round(at.X - 12));
            Canvas.SetTop(plus, Math.Round(at.Y - 12));
            Panel.SetZIndex(plus, int.MaxValue - 1);
            OrbitCanvas.Children.Add(plus);
            _ringPlus.Add(plus);
        }
    }

    // La section neuve garde la place du + ; les autres sont figées où elles sont (leur place automatique dépend du nombre de sections).
    private void AddSectionAt(RingAddRequest request)
    {
        if (_wheelEditor is not { } editor) return;
        var (actions, name) = ActionEdits.NewSection(_actions, request.After);
        var layout = UiRuntime.Profile.Layout.Clone();
        foreach (var section in _wheelGeometry.Sections) layout.Sections.TryAdd(section.Name, section.Center);
        layout.Sections[name] = LimitSectionPosition(new WheelPoint(request.At.X, request.At.Y));
        editor.AddSectionFromWheel(actions, layout, name);
        Dispatcher.BeginInvoke(() =>
        {
            if (_groupInfos.TryGetValue(name, out var group)) ExpandGroup(group.Info, group.Angle);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }
}
