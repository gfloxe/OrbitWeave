using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace OrbitWeave;

// Zones vides de la roue traversables pour de vrai. Répondre HTTRANSPARENT à WM_NCHITTEST ne suffit pas
// quand le bureau (autre processus) est dessous : souris immobile sur une zone vide, Windows renvoyait
// le message en boucle (≈ 600 000/s), le fil de la roue ne faisait plus rien d'autre et tout semblait
// figé jusqu'à un Alt+Tab. Ici la fenêtre prend WS_EX_TRANSPARENT dès qu'une zone vide est testée :
// Windows ne l'interroge plus du tout ; un minuteur la rend cliquable quand la souris revient sur la roue.
public partial class MainWindow
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x20;
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }

    private DispatcherTimer? _clickThroughTimer;
    private NativePoint _clickThroughLastCursor;
    private int _clickThroughIdleTicks;

    private bool IsClickThrough =>
        _source is { IsDisposed: false } && (GetWindowLongPtr(_source.Handle, GwlExStyle).ToInt64() & WsExTransparent) != 0;

    private void SetClickThrough(bool on)
    {
        if (_source is not { IsDisposed: false } source) return;
        var style = GetWindowLongPtr(source.Handle, GwlExStyle).ToInt64();
        var wanted = on ? style | WsExTransparent : style & ~WsExTransparent;
        if (wanted == style) return;
        SetWindowLongPtr(source.Handle, GwlExStyle, new IntPtr(wanted));
        if (_traceTimer is not null) Trace(on ? "traversable" : "cliquable");
        if (!on) return;
        _clickThroughIdleTicks = int.MaxValue; // premier passage : vérifier tout de suite
        _clickThroughTimer ??= CreateClickThroughTimer();
        _clickThroughTimer.Start();
    }

    private DispatcherTimer CreateClickThroughTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            if (_windowClosing || _source is not { IsDisposed: false } source) { timer.Stop(); return; }
            if (!IsClickThrough) { timer.Stop(); return; }
            if (!IsVisible) { SetClickThrough(false); return; }
            if (!GetCursorPos(out var cursor) || !GetWindowRect(source.Handle, out var rect)) return;
            if (cursor.X < rect.Left || cursor.X >= rect.Right || cursor.Y < rect.Top || cursor.Y >= rect.Bottom) return;
            // Un fichier glissé depuis une autre fenêtre arrive sur la roue : elle redevient une cible de dépôt.
            if (IsForeignDragInProgress()) { SetClickThrough(false); return; }
            // Souris immobile : la roue peut quand même changer sous elle (branche ouverte, mode Modifier) ; contrôle espacé.
            var moved = cursor.X != _clickThroughLastCursor.X || cursor.Y != _clickThroughLastCursor.Y;
            _clickThroughLastCursor = cursor;
            if (!moved && ++_clickThroughIdleTicks < 8) return;
            _clickThroughIdleTicks = 0;
            if (PresentationSource.FromVisual(this) is null) return;
            if (IsWheelHit(PointFromScreen(new Point(cursor.X, cursor.Y)))) SetClickThrough(false);
        };
        return timer;
    }
}
