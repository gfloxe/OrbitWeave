using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using OrbitWeave.Ui;

namespace OrbitWeave;

// --trace-input : journal de ce que la roue reçoit de Windows, pour comprendre un gel sans le reproduire à coup sûr.
public partial class MainWindow
{
    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int Size, Flags;
        public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public int CaretLeft, CaretTop, CaretRight, CaretBottom;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);
    [DllImport("user32.dll")] private static extern IntPtr GetCapture();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }

    private System.Windows.Threading.DispatcherTimer? _traceTimer;
    private string _traceState = "";
    private int _traceHitTests, _traceTransparent, _traceMoves, _traceButtons;
    private string _traceLastHit = "";
    private int _traceResizes, _traceMovesWindow, _traceFrames;
    private long _traceLastTick;
    private bool _traceStackDumped;

    private void StartInputTrace()
    {
        if (!DiagnosticFlags.TraceInput || _traceTimer is not null) return;
        Trace("début du journal");
        GotMouseCapture += (_, e) => Trace($"GotMouseCapture source={e.OriginalSource.GetType().Name}");
        LostMouseCapture += (_, e) => Trace($"LostMouseCapture source={e.OriginalSource.GetType().Name}");
        Activated += (_, _) => Trace("Activated");
        Deactivated += (_, _) => Trace("Deactivated");
        MouseEnter += (_, _) => Trace("MouseEnter");
        MouseLeave += (_, _) => Trace("MouseLeave");
        PreviewMouseDown += (_, e) => Trace($"PreviewMouseDown {e.ChangedButton} sur {e.OriginalSource.GetType().Name} ({UiId.Get(e.OriginalSource as FrameworkElement)})");
        PreviewMouseUp += (_, e) => Trace($"PreviewMouseUp {e.ChangedButton} sur {e.OriginalSource.GetType().Name}");
        IsVisibleChanged += (_, _) => Trace($"IsVisible={IsVisible}");
        SizeChanged += (_, _) => _traceResizes++;
        LocationChanged += (_, _) => _traceMovesWindow++;
        System.Windows.Media.CompositionTarget.Rendering += (_, _) => _traceFrames++;
        _traceTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        var counters = Stopwatch.StartNew();
        _traceLastTick = Stopwatch.GetTimestamp();
        _traceTimer.Tick += (_, _) =>
        {
            var late = Stopwatch.GetElapsedTime(_traceLastTick).TotalMilliseconds;
            if (late > 1000) Trace($"minuteur en retard de {late:F0} ms");
            _traceLastTick = Stopwatch.GetTimestamp();
            _traceStackDumped = false;
            var state = TraceSnapshot();
            if (state != _traceState) { Trace(state); _traceState = state; }
            if (counters.Elapsed.TotalSeconds < 2) return;
            if (_traceHitTests + _traceMoves + _traceButtons + _traceResizes + _traceMovesWindow > 0)
                Trace($"messages/2s : hittest={_traceHitTests} (transparent={_traceTransparent}) move={_traceMoves} boutons={_traceButtons} " +
                      $"taille={_traceResizes} position={_traceMovesWindow} images={_traceFrames}");
            _traceHitTests = _traceTransparent = _traceMoves = _traceButtons = _traceResizes = _traceMovesWindow = _traceFrames = 0;
            counters.Restart();
        };
        _traceTimer.Start();
    }

    private void TraceMessage(int msg, bool transparent, Point screen)
    {
        if (_traceTimer is null) return;
        // Messages traités mais minuteur muet : le fil est occupé ailleurs ; la pile dit par qui.
        if (!_traceStackDumped && Stopwatch.GetElapsedTime(_traceLastTick).TotalMilliseconds > 1500)
        {
            _traceStackDumped = true;
            Trace($"minuteur bloqué depuis {Stopwatch.GetElapsedTime(_traceLastTick).TotalMilliseconds:F0} ms, message 0x{msg:X4}, pile :{Environment.NewLine}{Environment.StackTrace}");
        }
        if (msg == 0x0084)
        {
            _traceHitTests++;
            if (transparent) _traceTransparent++;
            var hit = transparent ? "transparent" : "roue";
            if (hit != _traceLastHit) { Trace($"hittest → {hit} à {screen.X:F0},{screen.Y:F0}"); _traceLastHit = hit; }
        }
        else if (msg == 0x0200) _traceMoves++;
        else if (msg is 0x0201 or 0x0202 or 0x0204 or 0x0205) { _traceButtons++; Trace($"WM 0x{msg:X4}"); }
    }

    private string TraceSnapshot()
    {
        var foreground = GetForegroundWindow();
        var thread = GetWindowThreadProcessId(foreground, out var process);
        var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
        GetGUIThreadInfo(thread, ref info);
        GetCursorPos(out var cursor);
        var under = WindowFromPoint(cursor);
        GetWindowThreadProcessId(under, out var underProcess);
        var own = new WindowInteropHelper(this).Handle;
        return $"premier plan={Describe(foreground, process)} capture(premier plan)={Describe(info.Capture, null)} " +
               $"sous la souris={Describe(under, underProcess)} capture(roue)={(GetCapture() == own ? "roue" : GetCapture() == IntPtr.Zero ? "aucune" : "autre")} " +
               $"wpfCapture={Mouse.Captured?.GetType().Name ?? "aucune"} survol={IsMouseOver} rapide={_quickSelecting} " +
               $"section={_activeGroup ?? "-"} visible={IsVisible} opacité={Opacity:F2}/{Root.Opacity:F2} masquage={_isHiding} édition={_wheelEditMode}";
    }

    private static string Describe(IntPtr hwnd, uint? process)
    {
        if (hwnd == IntPtr.Zero) return "aucune";
        var name = new StringBuilder(64);
        GetClassName(hwnd, name, name.Capacity);
        var pid = process ?? (GetWindowThreadProcessId(hwnd, out var other) == 0 ? 0 : other);
        string app;
        try { app = Process.GetProcessById((int)pid).ProcessName; } catch { app = pid.ToString(); }
        return $"{app}/{name}";
    }

    private static void Trace(string text)
    {
        try
        {
            Directory.CreateDirectory(DiagnosticFlags.DirectoryPath);
            File.AppendAllText(Path.Combine(DiagnosticFlags.DirectoryPath, "input-trace.log"), $"{DateTime.Now:HH:mm:ss.fff} {text}{Environment.NewLine}");
        }
        catch (IOException) { }
    }
}
