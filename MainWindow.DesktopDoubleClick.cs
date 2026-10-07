using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using OrbitWeave.Orbit;

namespace OrbitWeave;

// Double-clic sur le bureau, là où il n'y a rien : la branche ouverte se replie.
// Le crochet souris tourne sur son propre fil : il ne fait que compter les appuis, la roue n'est
// prévenue qu'au double-clic et ne ralentit jamais la souris, même quand elle est occupée.
public partial class MainWindow
{
    private delegate IntPtr LowLevelMouseProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)] private struct MouseHookInfo { public int X, Y; public uint MouseData, Flags, Time; public IntPtr ExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, LowLevelMouseProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern int GetMessage(out NativeMessage message, IntPtr window, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
    [DllImport("oleacc.dll")] private static extern int AccessibleObjectFromPoint(NativePoint point,
        [MarshalAs(UnmanagedType.Interface)] out Accessibility.IAccessible accessible, out object child);

    [StructLayout(LayoutKind.Sequential)] private struct NativeMessage { public IntPtr Window; public uint Message; public IntPtr WParam, LParam; public uint Time; public NativePoint Point; }

    private uint _desktopHookThread;
    private LowLevelMouseProc? _desktopHookProc;

    private void StartDesktopDoubleClick()
    {
        var thread = new Thread(RunDesktopHook) { IsBackground = true, Name = "Double-clic bureau" };
        thread.Start();
        var probe = new NativePoint { X = 40, Y = 40 };
        Trace($"bureau en (40,40) : {DescribeWindowAt(probe)}, vide={IsEmptyDesktopAt(probe)}");
        Closed += (_, _) => { if (_desktopHookThread != 0) PostThreadMessage(_desktopHookThread, 0x0012 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero); };
    }

    private void RunDesktopHook()
    {
        try
        {
            _desktopHookThread = GetCurrentThreadId();
            var detector = new DoubleClickDetector(GetDoubleClickTime(), GetSystemMetrics(36 /* SM_CXDOUBLECLK */), GetSystemMetrics(37 /* SM_CYDOUBLECLK */));
            var hook = IntPtr.Zero;
            _desktopHookProc = (code, wParam, lParam) =>
            {
                if (code >= 0 && wParam == 0x0201 /* WM_LBUTTONDOWN */)
                {
                    var info = Marshal.PtrToStructure<MouseHookInfo>(lParam);
                    if (detector.Press(info.Time, info.X, info.Y))
                        Dispatcher.BeginInvoke(() => OnDoubleClickAnywhere(info.X, info.Y));
                }
                return CallNextHookEx(hook, code, wParam, lParam);
            };
            hook = SetWindowsHookEx(14 /* WH_MOUSE_LL */, _desktopHookProc, GetModuleHandle(null), 0);
            Dispatcher.BeginInvoke(() => Trace(hook == IntPtr.Zero ? "crochet du double-clic refusé" : "crochet du double-clic posé"));
            if (hook == IntPtr.Zero) return;
            while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { }
            UnhookWindowsHookEx(hook);
        }
        catch (Exception ex) { CrashLog.Write("DesktopDoubleClick", ex); }
    }

    private void OnDoubleClickAnywhere(int x, int y)
    {
        if (_windowClosing || _activeGroup is null || _wheelEditMode || _folding || !IsVisible) return;
        var point = new NativePoint { X = x, Y = y };
        if (!IsEmptyDesktopAt(point))
        {
            Trace($"double-clic ({x},{y}) hors du bureau vide : {DescribeWindowAt(point)}");
            return;
        }
        Trace($"double-clic sur le bureau vide ({x},{y}) : repli de {_activeGroup}");
        CollapseGroup(true);
    }

    // Le bureau : la liste des icônes (ou le fond quand les icônes sont masquées). Une icône n'est pas « rien ».
    private static bool IsEmptyDesktopAt(NativePoint point)
    {
        var window = WindowFromPoint(point);
        if (window == IntPtr.Zero) return false;
        var name = ClassName(window);
        if (name == "SysListView32")
        {
            if (ClassName(GetParent(window)) != "SHELLDLL_DefView") return false;
            try
            {
                if (AccessibleObjectFromPoint(point, out var accessible, out var child) != 0) return false;
                // 0x22 : élément de liste, c'est-à-dire une icône.
                return !(accessible.get_accRole(child) is int role && role == 0x22);
            }
            catch (COMException) { return false; }
        }
        // Icônes masquées, ou fond animé posé derrière les icônes : tout ce qui appartient au bureau.
        return ClassName(GetAncestor(window, 2 /* GA_ROOT */)) is "Progman" or "WorkerW";
    }

    private static string DescribeWindowAt(NativePoint point)
    {
        var window = WindowFromPoint(point);
        return $"{ClassName(window)} (racine {ClassName(GetAncestor(window, 2))})";
    }

    private static string ClassName(IntPtr window)
    {
        if (window == IntPtr.Zero) return "";
        var name = new StringBuilder(64);
        return GetClassName(window, name, name.Capacity) > 0 ? name.ToString() : "";
    }
}
