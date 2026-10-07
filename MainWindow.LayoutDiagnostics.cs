using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OrbitWeave.Ui.Editor;

namespace OrbitWeave;

public partial class MainWindow
{
    // Test isolé dans l'application lancée : même session et même mise à jour que le glisser.
    private void StartLayoutDragDiagnostic(WheelEditor page)
    {
        var delay = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        delay.Tick += (_, _) =>
        {
            delay.Stop();
            if (!_groupInfos.TryGetValue("Social", out var group)) return;
            ExpandGroup(group.Info, group.Angle);
            var id = DiagnosticFlags.LayoutDragHub ? "wheel.hub" : "wheel.section:Social";
            page.PreviewSelect(id);
            _dragId = id;
            _dragCenter = ToPoint(WheelPosition(id)!.Value);
            page.PreviewLayoutDrag(id, new Vector(), false);
            // Laisser finir la sélection du panneau avant de mesurer le mouvement lui-même.
            var ready = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            ready.Tick += (_, _) =>
            {
                ready.Stop();
                _dragRenderStart = _renderCount;
                _dragSelectionRedraws = 0;
                _layoutUpdateTimes.Clear();
                _dragMeter = new FrameMeter(true);
                _dragMeter.Begin("layout/glisser-simule");
                var clock = Stopwatch.StartNew();
                var capturedMiddle = false;
                EventHandler? frame = null;
                frame = (_, _) =>
                {
                    var fraction = Math.Min(1, clock.Elapsed.TotalMilliseconds / 1500);
                    var delta = new Vector(-70 * fraction, -70 * fraction);
                    page.PreviewLayoutDrag(id, delta, false);
                    if (DiagnosticFlags.LayoutDragCaptures && !capturedMiddle && fraction >= 0.5)
                    {
                        capturedMiddle = true;
                        SaveLayoutDiagnosticImage("layout-drag-middle.png");
                    }
                    if (fraction < 1) return;
                    CompositionTarget.Rendering -= frame;
                    _dragId = null;
                    page.PreviewLayoutDrag(id, delta, true);
                    _dragMeter.End();
                    _dragMeter = null;
                    var sorted = _layoutUpdateTimes.Order().ToArray();
                    File.AppendAllText(System.IO.Path.Combine(DiagnosticFlags.DirectoryPath, "layout-drag.log"),
                        $"{id}: updates={sorted.Length}, updateMedian={sorted[sorted.Length / 2]:F3}ms, updateMax={sorted[^1]:F3}ms, fullRenders={_renderCount - _dragRenderStart}, selectionRedraws={_dragSelectionRedraws}{Environment.NewLine}");
                    var capture = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                    capture.Tick += (_, _) => { capture.Stop(); SaveLayoutDiagnosticImage("layout-drag-final.png"); };
                    capture.Start();
                };
                CompositionTarget.Rendering += frame;
            };
            ready.Start();
        };
        delay.Start();
    }

    private void SaveLayoutDiagnosticImage(string name)
    {
        var (image, _) = SnapshotWheel();
        if (image is null) return;
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(System.IO.Path.Combine(DiagnosticFlags.DirectoryPath, name));
        png.Save(file);
    }
}
