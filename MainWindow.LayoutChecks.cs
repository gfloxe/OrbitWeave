using System.Windows;
using System.Windows.Threading;
using OrbitWeave.Ui;
using OrbitWeave.Ui.Editor;

namespace OrbitWeave;

public partial class MainWindow
{
    // Parcours déterministe dans une instance lancée sur des données de test, sans simuler de saisie Windows.
    private void StartLayoutChecks(WheelEditor page)
    {
        void OpenSocial()
        {
            if (_groupInfos.TryGetValue("Social", out var group)) ExpandGroup(group.Info, group.Angle);
        }
        void Drag(string id, Vector delta)
        {
            _dragId = id;
            page.PreviewLayoutDrag(id, delta, false);
            _dragId = null;
            page.PreviewLayoutDrag(id, delta, true);
        }
        var steps = new Queue<(string Name, Action Run)>([
            ("origin", OpenSocial),
            ("section", () => Drag("wheel.section:Social", new Vector(-70, -70))),
            ("undo", page.PreviewLayoutUndo),
            ("redo", page.PreviewLayoutRedo),
            ("label", () => Drag("wheel.label:Social", new Vector(15, 20))),
            ("subtitle", () => Drag("wheel.label.subtitle:Social", new Vector(3, -2))),
            ("label-follows", () => Drag("wheel.section:Social", new Vector(-20, 25))),
            ("reset", page.PreviewLayoutReset),
            ("reset-undo", page.PreviewLayoutUndo),
            ("saved", page.SavePending)
        ]);
        if (DiagnosticFlags.ViewportChecks)
        {
            steps.Clear();
            steps.Enqueue(("corner", OpenSocial));
            steps.Enqueue(("edge-section", () => Drag("wheel.section:Social", new Vector(-5000, -5000))));
            steps.Enqueue(("edge-cards", () => Drag("wheel.card", new Vector(-5000, -5000))));
            steps.Enqueue(("edge-label", () => Drag("wheel.label:Social", new Vector(-5000, -5000))));
            steps.Enqueue(("edge-hub", () => Drag("wheel.hub", new Vector(5000, 5000))));
        }
        var delay = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        delay.Tick += (_, _) =>
        {
            delay.Stop();
            _editSelection = null;
            DrawEditSelection();
            var stepTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            string? pending = null;
            stepTimer.Tick += (_, _) =>
            {
                if (pending is { } name)
                {
                    SaveLayoutDiagnosticImage($"layout-check-{name}.png");
                    if (DiagnosticFlags.ViewportChecks)
                    {
                        UpdateLayout();
                        var clipped = OrbitCanvas.Children.OfType<FrameworkElement>()
                            .Where(item => item.Visibility == Visibility.Visible && item is not System.Windows.Shapes.Line)
                            .Where(item => !_currentViewport.Contains(FinalElementBounds(item)))
                            .Select(item => $"{OrbitWeave.Ui.UiId.Get(item) ?? item.GetType().Name}({FinalElementBounds(item)})").ToArray();
                        File.AppendAllText(System.IO.Path.Combine(DiagnosticFlags.DirectoryPath, "viewport-checks.log"),
                            $"{name}: clipped={string.Join(',', clipped)}; blankHit={IsWheelHit(new Point(2, 2))}; canvasOrigin={OrbitCanvas.PointToScreen(new Point())}; viewport={_currentViewport}{Environment.NewLine}");
                    }
                    var social = _wheelGeometry.Section("Social");
                    File.AppendAllText(System.IO.Path.Combine(DiagnosticFlags.DirectoryPath, "layout-checks.log"),
                        $"{name}: node={social.Center}; label={social.LabelAnchor}; stack={_branchLeft:F2},{_branchFirstY:F2}; labelLeft={System.Windows.Controls.Canvas.GetLeft(_groupLabels["Social"]):F2}; desired={_groupLabels["Social"].DesiredSize.Width:F2}; actual={_groupLabels["Social"].ActualWidth:F2}{Environment.NewLine}");
                    pending = null;
                }
                if (steps.TryDequeue(out var step)) { step.Run(); pending = step.Name; return; }
                stepTimer.Stop();
                File.WriteAllText(System.IO.Path.Combine(DiagnosticFlags.DirectoryPath, "layout-checks-complete.txt"), UiRuntime.Profile.Name);
            };
            stepTimer.Start();
        };
        delay.Start();
    }
}
