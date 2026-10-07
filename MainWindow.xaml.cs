using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using OrbitWeave.Actions;
using OrbitWeave.Orbit;
using OrbitWeave.Ui;
using OrbitWeave.Ui.Editor;
using Forms = System.Windows.Forms;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using Panel = System.Windows.Controls.Panel;
using Size = System.Windows.Size;

namespace OrbitWeave;

public partial class MainWindow : Window, IUiEditorHost
{
    private const int HotkeyId = 0xA51;
    private const int WmHotkey = 0x0312;
    private HwndSource? _source;
    private bool _hotkeyRegistered;
    private List<ActionItem> _actions = [];
    private HotkeySettings _settings = new();
    private readonly List<UIElement> _expandedElements = [];
    private readonly Dictionary<string, Button> _groupButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (GroupInfo Info, double Angle)> _groupInfos = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Border> _groupLabels = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<UIElement> _dimmedBaseElements = [];
    private readonly Dictionary<string, Line> _spokes = new(StringComparer.OrdinalIgnoreCase);
    private string? _activeGroup;
    private bool _quickSelecting;
    private bool _folded;
    private bool _folding;
    private Button? _hub;
    private readonly System.Windows.Threading.DispatcherTimer _idleReset = new() { Interval = TimeSpan.FromSeconds(4) };
    private int _measurementRun;
    private bool _deferAnimations;
    private readonly List<Action> _pendingAnimations = [];
    private readonly List<UIElement> _baseElements = [];
    private readonly List<(string Type, Border Label, TextBlock Subtitle, double CenterX)> _liveLabels = [];
    private LiveSubtitles? _liveSubtitles;
    private System.Windows.Threading.DispatcherTimer? _subtitleTimer;
    private bool _subtitleBusy;
    private int _visibilityGeneration;
    private bool _isHiding;
    private bool _windowClosing;
    private OrbitView _orbitView = null!;
    private readonly OrbitRenderer _renderer = new();
    private WheelGeometry _wheelGeometry = WheelLayout.Compute([], new WheelMetrics(150, 254, 59, 76, 96, 62, 27));
    private Point WheelCenter => ToPoint(_wheelGeometry.HubCenter);
    private static Point ToPoint(WheelPoint point) => new(point.X, point.Y);
    private WheelGeometry CalculateWheelGeometry(IEnumerable<GroupInfo> groups)
    {
        var inputs = groups.Select(group => new WheelSectionInput(group.Name, group.BadgeCount > 0)).ToArray();
        var metrics = new WheelMetrics(Token("InnerOrbitRadius"), Token("OuterOrbitRadius"), OrbitLayout.SectionDiameter,
            OrbitLayout.HubDiameter, OrbitLayout.HubHaloDiameter, Token("SearchOffset"), Token("SearchDiameter"));
        UiLayoutMigration.Apply(UiRuntime.Profile, inputs, metrics);
        var added = _wheelGeometry.Sections.Count == 0 ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) :
            inputs.Select(item => item.Name).Except(_wheelGeometry.Sections.Select(item => item.Name), StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var geometry = WheelLayout.Compute(inputs, metrics, UiRuntime.Profile.Layout, added);
        if (added.Count > 0 && UiRuntime.Profile.Layout.Sections.Count > 0)
        {
            var automatic = WheelLayout.Compute(inputs, metrics);
            var changed = false;
            foreach (var section in geometry.Sections.Where(item => added.Contains(item.Name)))
                if (!UiRuntime.Profile.Layout.Sections.ContainsKey(section.Name) && section.Center != automatic.Section(section.Name).Center)
                { UiRuntime.Profile.Layout.Sections[section.Name] = section.Center; changed = true; }
            if (changed && UiRuntime.Profile.Name != UiProfileStore.OriginName) UiProfileStore.Default.Save(UiRuntime.Profile);
        }
        return geometry;
    }
    private sealed record GroupInfo(string Name, string Icon, List<ActionItem> Actions, double Radius,
        int BadgeCount = 0, string Type = "Ordinaire", WheelPoint? Position = null);
    private Color AccentColor => _settings.Theme switch
    {
        "Bleu" => Color.FromRgb(105, 190, 255),
        "Ambre" => Color.FromRgb(245, 191, 113),
        _ => Color.FromRgb(218, 222, 228)
    };
    private Color SurfaceColor => _settings.Theme switch
    {
        "Bleu" => Color.FromRgb(14, 27, 45),
        "Ambre" => Color.FromRgb(35, 27, 20),
        _ => Color.FromRgb(23, 25, 29)
    };
    private double AnimationScale => _settings.AnimationScale ?? HotkeySettings.PresetAnimationScale(_settings.Animation);
    private bool _instantMotion;
    private TimeSpan Motion(double milliseconds) =>
        _instantMotion ? TimeSpan.Zero : TimeSpan.FromMilliseconds(milliseconds * AnimationScale);
    private static double Token(string key) => (double)System.Windows.Application.Current.FindResource(key);
    private static Brush BrushToken(string key) => (Brush)System.Windows.Application.Current.FindResource(key);
    // Opacité finale d'un élément selon le profil d'interface actif.
    private static double TargetOpacity(UIElement element) =>
        element is FrameworkElement framework ? UiOverrides.Opacity(framework, UiRuntime.Profile) : 1;
    private static void Customize(FrameworkElement element) => UiOverrides.ApplyTree(element, UiRuntime.Profile);

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string? windowName);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern void keybd_event(byte virtualKey, byte scan, uint flags, UIntPtr extraInfo);

    public MainWindow()
    {
        InitializeComponent();
        if (DiagnosticFlags.MeasureGlass)
            Root.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
        _orbitView = new OrbitView(OrbitCanvas);
        var desktop = FindWindow("Progman", null);
        if (desktop != IntPtr.Zero) new WindowInteropHelper(this).Owner = desktop;
        _actions = ActionStore.Load();
        _settings = HotkeySettings.Load();
        SourceInitialized += (_, _) =>
        {
            _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _source?.AddHook(WndProc);
            RegisterConfiguredHotkey();
            StartInputTrace();
            StartFileDrop();
        StartWheelMenus();
        StartTrayIcon();
            StartDesktopDoubleClick();
            Dispatcher.BeginInvoke(ShowOrbit);
            Dispatcher.BeginInvoke(ReportStartup, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        };
        Activated += (_, _) => Dispatcher.BeginInvoke(SendBehindWindows);
        _idleReset.Tick += ResetAfterIdle;
        _idleReset.Interval = TimeSpan.FromSeconds(_settings.BranchCloseSeconds);
        UiRuntime.Changed += OnInterfaceChanged;
        MouseEnter += (_, _) => { _idleReset.Stop(); AnimateIdleOpacity(1); };
        MouseLeave += (_, _) => { _idleReset.Stop(); _idleReset.Start(); AnimateIdleOpacity(IdleOpacity()); };
        PreviewMouseLeftButtonUp += Window_PreviewMouseLeftButtonUp;
        OrbitCanvas.PreviewMouseLeftButtonDown += OrbitCanvas_EditMouseDown;
        PreviewMouseMove += Window_EditMouseMove;
        PreviewMouseLeftButtonUp += Window_EditMouseUp;
        LostMouseCapture += Window_LostMouseCapture;
        Closing += (_, _) => { _windowClosing = true; _visibilityGeneration++; };
        Closed += (_, _) =>
        {
            try
            {
                _subtitleTimer?.Stop();
                _liveSubtitles?.Dispose();
                UiRuntime.Changed -= OnInterfaceChanged;
                if (_source is { IsDisposed: false })
                {
                    if (_hotkeyRegistered) UnregisterHotKey(_source.Handle, HotkeyId);
                    _source.RemoveHook(WndProc);
                }
            }
            catch (Exception ex) { CrashLog.Write("MainWindow.Closed", ex); }
            finally { System.Windows.Application.Current.Shutdown(); }
        };
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        try { return WndProcCore(hwnd, msg, wParam, lParam, ref handled); }
        catch (Exception ex)
        {
            CrashLog.Write($"WndProc msg=0x{msg:X}", ex);
            handled = msg == 0x0084;
            return handled ? new IntPtr(-1) : IntPtr.Zero;
        }
    }

    private IntPtr WndProcCore(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_windowClosing || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return IntPtr.Zero;
        if (msg == 0x0084 && !IsMouseCaptured) // WM_NCHITTEST : les zones vides laissent passer les clics.
        {
            if (!IsVisible || _source is null || _source.IsDisposed || PresentationSource.FromVisual(this) is null) return IntPtr.Zero;
            var value = lParam.ToInt64();
            var screenPoint = new Point(unchecked((short)(value & 0xFFFF)), unchecked((short)((value >> 16) & 0xFFFF)));
            // Pendant un glisser venu d'ailleurs, toute la roue reste une cible de dépôt.
            var transparent = !IsWheelHit(PointFromScreen(screenPoint)) && !IsForeignDragInProgress();
            TraceMessage(msg, transparent, screenPoint);
            if (transparent) { SetClickThrough(true); handled = true; return new IntPtr(-1); }
        }
        else TraceMessage(msg, false, default);
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            if (IsVisible && !_isHiding) HideOrbit(); else ShowOrbit();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public bool RegisterConfiguredHotkey()
    {
        if (_source == null) return false;
        if (_hotkeyRegistered) UnregisterHotKey(_source.Handle, HotkeyId);
        var key = KeyInterop.VirtualKeyFromKey(Enum.TryParse<Key>(_settings.Key, out var parsed) ? parsed : Key.Space);
        uint modifiers = (_settings.Control ? 0x0002u : 0) | (_settings.Alt ? 0x0001u : 0) | (_settings.Shift ? 0x0004u : 0) | (_settings.Win ? 0x0008u : 0) | 0x4000u;
        _hotkeyRegistered = RegisterHotKey(_source.Handle, HotkeyId, modifiers, (uint)key);
        if (DiagnosticFlags.Enabled) try
        {
            Directory.CreateDirectory(DiagnosticFlags.DirectoryPath);
            File.WriteAllText(System.IO.Path.Combine(DiagnosticFlags.DirectoryPath, "hotkey-status.txt"),
                $"{DateTime.Now:O} — {(_hotkeyRegistered ? "actif" : "indisponible")} — {(_settings.Control ? "Ctrl+" : "")}{(_settings.Alt ? "Alt+" : "")}{(_settings.Shift ? "Maj+" : "")}{(_settings.Win ? "Win+" : "")}{_settings.Key}");
        }
        catch { }
        return _hotkeyRegistered;
    }

    private void ShowOrbit()
    {
        _viewportReady = false;
        _visibilityGeneration++;
        _isHiding = false;
        var showGeneration = _visibilityGeneration;
        Root.BeginAnimation(OpacityProperty, null);
        Root.Opacity = 1;
        Opacity = 0;
        var monitor = (Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0]).Bounds;
        UiRuntime.SetScreenWidth(monitor.Width);
        var dpi = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M22 ?? 1;
        var scale = Math.Clamp(Math.Min((monitor.Width / dpi - 32) / OrbitLayout.ViewportWidth,
            (monitor.Height / dpi - 32) / OrbitLayout.ViewportHeight), 0.4, 1);
        Width = OrbitLayout.ViewportWidth * scale;
        Height = OrbitLayout.ViewportHeight * scale;
        OrbitCanvas.RenderTransform = new ScaleTransform(scale, scale);
        EditOverlay.RenderTransform = OrbitCanvas.RenderTransform;
        OrbitCanvas.Margin = EditOverlay.Margin = new Thickness(-OrbitLayout.ViewportOffsetX * scale,
            -OrbitLayout.ViewportOffsetY * scale, 0, 0);
        if (DiagnosticFlags.MeasureFront) Topmost = true;
        // Seule la position « Centre » est définie ; toute autre valeur sauvegardée
        // garde ce placement jusqu'à ce qu'une position explicite soit spécifiée.
        var anchor = _settings.Position switch
        {
            "Centre" => new Point(monitor.Left + monitor.Width / 2.0, monitor.Top + monitor.Height / 2.0),
            _ => new Point(monitor.Left + monitor.Width / 2.0, monitor.Top + monitor.Height / 2.0)
        };
        var point = PointFromScreen(anchor);
        Left += point.X - Width / 2;
        Top += point.Y - Height / 2;
        if (DiagnosticFlags.PreviewFolded) _folded = true;
        var measure = !DiagnosticFlags.MeasureLayoutDrag && (DiagnosticFlags.MeasureBaseline || DiagnosticFlags.MeasureWheel ||
            DiagnosticFlags.MeasureNoShadows || DiagnosticFlags.MeasureReopen);
        var phase = DiagnosticFlags.MeasureBaseline ? "baseline" :
            DiagnosticFlags.MeasureNoShadows ? "roue-sans-ombres" : "roue-v5";
        if (DiagnosticFlags.MeasureReopen) phase += $"-affichage{++_measurementRun}";
        var meter = measure ? new FrameMeter(DiagnosticFlags.TraceFrames) : null;
        if (_baseElements.Count == 0)
        {
            _deferAnimations = true;
            RenderOrbit();
            _deferAnimations = false;
        }
        else
        {
            _orbitView.Clear();
            foreach (var element in _baseElements) _orbitView.Add(element);
            CollapseGroup();
        }
        if (DiagnosticFlags.MeasureReopen)
            File.AppendAllText(System.IO.Path.Combine(DiagnosticFlags.DirectoryPath, "ghost-test.log"),
                $"before Show: opacity={Opacity:F0}, rootChildren={OrbitCanvas.Children.Count}, activeGroup={_activeGroup ?? "none"}{Environment.NewLine}");
        if (!IsVisible) Show();
        PrepareAdaptiveViewport();
        if (DiagnosticFlags.MeasureIdle)
        {
            var idleStart = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
            idleStart.Tick += (_, _) =>
            {
                idleStart.Stop();
                var idleMeter = new FrameMeter();
                idleMeter.Begin("roue/repos");
                var idleEnd = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
                idleEnd.Tick += (_, _) => { idleEnd.Stop(); idleMeter.End(); };
                idleEnd.Start();
            };
            idleStart.Start();
        }
        void Reveal()
        {
            if (showGeneration != _visibilityGeneration || _isHiding) return;
            foreach (var start in _pendingAnimations) start();
            Root.BeginAnimation(OpacityProperty, null);
            Root.Opacity = 1;
            meter?.Begin($"{phase}/ouverture");
            Opacity = 1;
            if (!IsMouseOver) AnimateIdleOpacity(IdleOpacity());
            StartLiveSubtitles();
            if (!measure || meter == null) return;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(470) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                meter.End(460 * AnimationScale);
                var total = Math.Min(9, _actions.Select(a => a.Group.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count());
                var groups = _actions.GroupBy(a => a.Group.Trim(), StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
                if (groups.Length < 2) return;
                ExpandGroup(new GroupInfo(groups[0].Key, groups[0].First().GroupIcon, groups[0].ToList(), OrbitLayout.RadiusFor(0, total)), -Math.PI / 2);
                var settle = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(330) };
                settle.Tick += (_, _) =>
                {
                    settle.Stop();
                    ExpandGroup(new GroupInfo(groups[1].Key, groups[1].First().GroupIcon, groups[1].ToList(), OrbitLayout.RadiusFor(1, total)), -Math.PI / 4);
                    meter.Begin($"{phase}/changement-branche");
                    var finish = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                    finish.Tick += (_, _) => { finish.Stop(); meter.End(290 * AnimationScale); };
                    finish.Start();
                };
                settle.Start();
            };
            timer.Start();
        }
        EventHandler? firstFrame = null;
        var warmFrames = 0;
        firstFrame = (_, _) =>
        {
            if (showGeneration != _visibilityGeneration)
            {
                CompositionTarget.Rendering -= firstFrame;
                return;
            }
            if (++warmFrames < 5) return;
            CompositionTarget.Rendering -= firstFrame;
            Dispatcher.BeginInvoke((Action)Reveal, System.Windows.Threading.DispatcherPriority.Background);
        };
        CompositionTarget.Rendering += firstFrame;
        if (DiagnosticFlags.MeasureReopen && _measurementRun < DiagnosticFlags.ReopenCount)
        {
            var repeat = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            repeat.Tick += (_, _) =>
            {
                repeat.Stop();
                HideOrbit();
                var reopen = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                reopen.Tick += (_, _) => { reopen.Stop(); ShowOrbit(); };
                reopen.Start();
            };
            repeat.Start();
        }
        if (DiagnosticFlags.TestEscape)
        {
            var test = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            test.Tick += (_, _) =>
            {
                test.Stop();
                var source = PresentationSource.FromVisual(this);
                if (source == null) return;
                var key = new System.Windows.Input.KeyEventArgs(InputManager.Current.PrimaryKeyboardDevice, source,
                    Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                RaiseEvent(key);
                var result = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
                result.Tick += (_, _) =>
                {
                    result.Stop();
                    File.WriteAllText(System.IO.Path.Combine(DiagnosticFlags.DirectoryPath, "escape-test.txt"),
                        $"handled={key.Handled}; hidden={!IsVisible}; time={DateTime.Now:O}");
                };
                result.Start();
            };
            test.Start();
        }
        if (DiagnosticFlags.DumpUiIds)
        {
            // Liste de tous les identifiants d'interface : roue et menu du centre.
            var ids = UiAutoIds.Collect(OrbitCanvas)
                .Concat(_hub?.ContextMenu is { } hubMenu ? UiAutoIds.Collect(hubMenu) : []).ToList();
            File.WriteAllLines(System.IO.Path.Combine(DiagnosticFlags.DirectoryPath, "ui-ids.txt"), ids);
        }
        if (DiagnosticFlags.MeasureLayoutDrag || DiagnosticFlags.LayoutChecks)
        {
            StartWheelEditing();
            if (_wheelEditor is { } editor)
            {
                if (DiagnosticFlags.MeasureLayoutDrag) StartLayoutDragDiagnostic(editor);
                else StartLayoutChecks(editor);
            }
        }
        if (DiagnosticFlags.PreviewGroup || DiagnosticFlags.PreviewGroupLeft)
        {
            var groups = _actions.GroupBy(a => a.Group.Trim(), StringComparer.OrdinalIgnoreCase).Take(9).ToArray();
            var index = DiagnosticFlags.PreviewGroupLeft
                ? Enumerable.Range(0, groups.Length).FirstOrDefault(i => Math.Cos(OrbitLayout.AngleFor(i, groups.Length)) < -0.1)
                : Math.Clamp(DiagnosticFlags.PreviewGroupIndex, 0, Math.Max(0, groups.Length - 1));
            if (groups.Length > 0)
            {
                var group = groups[index];
                ExpandGroup(new GroupInfo(group.Key, group.First().GroupIcon, group.ToList(), OrbitLayout.RadiusFor(index, groups.Length)),
                    OrbitLayout.AngleFor(index, groups.Length));
            }
        }
        if (!DiagnosticFlags.MeasureFront) Dispatcher.BeginInvoke(SendBehindWindows);
        if (DiagnosticFlags.Preview || DiagnosticFlags.PreviewGroup || DiagnosticFlags.PreviewGroupLeft || DiagnosticFlags.PreviewFolded)
        {
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                try
                {
                    var image = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    image.Render(this);
                    var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
                    using var file = File.Create(System.IO.Path.Combine(DiagnosticFlags.DirectoryPath, "orbit-preview.png"));
                    png.Save(file);
                }
                catch (Exception ex) { File.AppendAllText(System.IO.Path.Combine(DiagnosticFlags.DirectoryPath, "orbit-errors.log"), ex + Environment.NewLine); }
            };
            timer.Start();
        }
    }

    private void SendBehindWindows()
    {
        if (_wheelEditMode) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var placed = SetWindowPos(handle, new IntPtr(1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        if (DiagnosticFlags.Enabled) try
        {
            Directory.CreateDirectory(DiagnosticFlags.DirectoryPath);
            File.WriteAllText(System.IO.Path.Combine(DiagnosticFlags.DirectoryPath, "desktop-status.txt"),
                $"{DateTime.Now:O} — desktop={(FindWindow("Progman", null) != IntPtr.Zero)} — arrière-plan={placed} — position={Left:F0},{Top:F0}");
        }
        catch { }
    }

    private void HideOrbit()
    {
        _subtitleTimer?.Stop();
        _isHiding = true;
        var generation = ++_visibilityGeneration;
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(Root.Opacity, 0, TimeSpan.FromMilliseconds(120)));
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(130) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (generation != _visibilityGeneration) return;
            Opacity = 0;
            Hide();
            Root.BeginAnimation(OpacityProperty, null);
            Root.Opacity = 0;
            CollapseGroup();
            _orbitView.Clear();
            _expandedElements.Clear();
            _activeGroup = null;
            if (DiagnosticFlags.MeasureReopen)
                File.AppendAllText(System.IO.Path.Combine(DiagnosticFlags.DirectoryPath, "ghost-test.log"),
                    $"after Hide: opacity={Opacity:F0}, rootChildren={OrbitCanvas.Children.Count}, activeGroup={_activeGroup ?? "none"}{Environment.NewLine}");
        };
        timer.Start();
    }

    private void RenderOrbit()
    {
        _renderer.Render(this);
        ForgetPlannedBranches();
        DrawBranchPreviews();
        UpdateAdaptiveViewport(true);
    }

    private void ExpandGroup(GroupInfo group, double angle)
    {
        var timing = System.Diagnostics.Stopwatch.StartNew();
        _stageWatch = _traceTimer is null ? null : System.Diagnostics.Stopwatch.StartNew();
        _stages = "";
        try { ExpandGroupCore(group, angle); }
        finally { if (_traceTimer is not null) Trace($"branche {group.Name} ouverte en {timing.Elapsed.TotalMilliseconds:F1} ms :{_stages}"); _stageWatch = null; }
    }

    private System.Diagnostics.Stopwatch? _stageWatch;
    private string _stages = "";

    private void Stage(string name)
    {
        if (_stageWatch is not { } watch) return;
        _stages += $" {name}={watch.Elapsed.TotalMilliseconds:F1}";
        watch.Restart();
    }

    private void ExpandGroupCore(GroupInfo group, double angle)
    {
        if (_folding || string.Equals(_activeGroup, group.Name, StringComparison.OrdinalIgnoreCase)) return;
        RestoreCoveredElements();
        foreach (var element in _expandedElements.ToArray()) FadeOutExpanded(element);
        _expandedElements.Clear();
        _openCards.Clear();
        _cardSlots.Clear();
        _activeGroup = group.Name;
        foreach (var pair in _groupButtons)
            pair.Value.Tag = pair.Key.Equals(group.Name, StringComparison.OrdinalIgnoreCase) ? "active" : null;
        foreach (var pair in _spokes)
        {
            var active = pair.Key.Equals(group.Name, StringComparison.OrdinalIgnoreCase);
            pair.Value.Stroke = BrushToken(active ? "ActiveSpokeBrush" : "WidgetSpokeBrush");
            pair.Value.StrokeThickness = Token(active ? "ActiveSpokeThickness" : "SpokeThickness");
            if (!active) UiOverrides.Apply(pair.Value, UiRuntime.Profile);
        }

        Stage("début");
        var (node, left, firstY, side, fallback, count, cardWidth, cardHeight, pitch) = PlanBranch(group);
        Stage("plan");
        if (fallback) DimCoveredElements(left, firstY, count, pitch, cardWidth, cardHeight);
        if (DiagnosticFlags.PreviewGroup && count > 0)
        {
            var nearest = Enumerable.Range(0, count).Min(i => Math.Sqrt(
                Math.Pow(left + cardWidth / 2 - node.X, 2) + Math.Pow(firstY + i * pitch - node.Y, 2)));
            var hubClearance = Enumerable.Range(0, count).Min(i => ConnectorHubDistance(node, side,
                side < 0 ? left + cardWidth : left, firstY + i * pitch));
            File.AppendAllText(System.IO.Path.Combine(DiagnosticFlags.DirectoryPath, "placement.log"),
                $"{group.Name}: nearestCardCenter={nearest:F1}px, hubClearance={hubClearance:F1}px, side={side}, shift={firstY - (node.Y - (count - 1) * pitch / 2):F0}px, fallback={fallback}{Environment.NewLine}");
        }
        left = Math.Round(left);
        _branchLeft = left;
        _branchFirstY = firstY;
        _branchSide = side;
        _branchNode = node;
        var outward = side == 0 ? Math.Sign(node.Y - WheelCenter.Y) : 0;
        if (side == 0 && count > 0)
        {
            // Pile verticale : un seul trait relie la section à la carte la plus proche.
            var labelHeight = (double)System.Windows.Application.Current.FindResource("SectionLabelHeight");
            var from = outward > 0 ? node.Y + OrbitLayout.SectionDiameter / 2 + 4 + labelHeight : node.Y - OrbitLayout.SectionDiameter / 2;
            var to = outward > 0 ? firstY - cardHeight / 2 : firstY + (count - 1) * pitch + cardHeight / 2;
            var link = new Line { X1 = Math.Round(node.X) + 0.5, Y1 = from, X2 = Math.Round(node.X) + 0.5, Y2 = to,
                Stroke = BrushToken("ConnectorBrush"), StrokeThickness = Token("ConnectorThickness"), IsHitTestVisible = false }.With($"wheel.link:{group.Name}");
            Customize(link);
            Panel.SetZIndex(link, -1);
            AddExpanded(link, 0, 0);
        }
        var cardStagger = Token("MotionCardStagger");
        var (mainCards, extraGroups) = SplitBranch(group);
        for (var i = 0; i < count; i++)
        {
            var action = mainCards[i];
            _cardSlots[action.Id] = (null, i);
            var y = Math.Round(firstY + i * pitch);
            var card = CreateBranchCard(action);
            if (fallback) Panel.SetZIndex(card, int.MaxValue);
            Canvas.SetLeft(card, left);
            Canvas.SetTop(card, Math.Round(y - cardHeight / 2));
            Customize(card);
            AddChildButtonIfEditing(action, left, Math.Round(y - cardHeight / 2), side == 0 ? 1 : side, null);
            if (side == 0)
            {
                AddExpanded(card, (outward > 0 ? i : count - 1 - i) * cardStagger, 0, outward);
                continue;
            }
            var start = new Point(node.X + side * OrbitLayout.SectionDiameter / 2, node.Y);
            var end = new Point(side < 0 ? left + cardWidth : left, y);
            var distance = Math.Max(0, side * (end.X - start.X));
            var bend = Math.Min(40, distance / 2);
            var p1 = new Point(start.X + side * bend, start.Y);
            var p2 = new Point(end.X - side * bend, end.Y);
            var path = new System.Windows.Shapes.Path
            {
                Data = new PathGeometry([new PathFigure(start,
                    [new BezierSegment(p1, p2, end, true)], false)]),
                Stroke = BrushToken("ConnectorBrush"),
                StrokeThickness = Token("ConnectorThickness"), IsHitTestVisible = false
            }.With($"wheel.link:{action.Id}");
            Customize(path);
            Panel.SetZIndex(path, -1);
            AddExpanded(path, i * cardStagger, side);
            AddExpanded(card, i * cardStagger, side);
        }
        Stage("cartes");
        AddCutButtonsIfEditing(group.Name, mainCards.Take(count).ToList(), left, firstY);
        AddPlusIfEditing(group.Name, left, firstY, count, side == 0 && outward < 0);
        foreach (var extra in extraGroups) AddCardGroup(group.Name, node, extra.Id, extra.Group, extra.Cards);
        AddColumnPlusIfEditing(group);
        Stage("groupes");
        DrawBranchPreviews();
        Stage("aperçus");
        UpdateAdaptiveViewport(!WheelGestureActive);
        Stage("fenêtre");
        UpdateEditingBanner();
        Stage("bandeau");
    }

    private (Point Node, double Left, double FirstY, int Side, bool Fallback, int Count, double CardWidth, double CardHeight, double Pitch)
        PlanBranch(GroupInfo group)
    {
        var section = _wheelGeometry.Sections.FirstOrDefault(item => item.Name.Equals(group.Name, StringComparison.OrdinalIgnoreCase));
        var node = group.Position is { } position ? ToPoint(position) : ToPoint(section.Center);
        var angle = section.Angle;
        var exteriorSide = Math.Cos(angle) < 0 ? -1 : 1;
        var count = SplitBranch(group).Main.Count;
        var cardWidth = (double)System.Windows.Application.Current.FindResource("ActionCardWidth");
        var cardHeight = (double)System.Windows.Application.Current.FindResource("ActionCardHeight");
        var pitch = (double)System.Windows.Application.Current.FindResource("ActionCardPitch");
        var (left, firstY, side, fallback) = ResolveCardPlacement(node, angle, count, pitch, cardWidth, cardHeight, exteriorSide);
        if (UiRuntime.Profile.Layout.Stacks.TryGetValue(group.Name, out var pinned))
        {
            left = node.X + pinned.X; firstY = node.Y + pinned.Y; side = pinned.Side;
            fallback = true;
        }
        if (HasCustomViewport && _viewportReady && count > 0)
        {
            var column = AdaptiveViewport.KeepInside(new Rect(left, firstY - cardHeight / 2,
                cardWidth, (count - 1) * pitch + cardHeight), _screenCanvasBounds);
            left = column.Left;
            firstY = column.Top + cardHeight / 2;
        }
        return (node, left, firstY, side, fallback, count, cardWidth, cardHeight, pitch);
    }

    private (double Left, double FirstY, int Side, bool Fallback) ResolveCardPlacement(
        Point node, double angle, int count, double pitch, double width, double height, int exteriorSide)
    {
        var first = node.Y - (count - 1) * pitch / 2;
        var gap = Token("CardGap");
        // Toutes les places possibles, de la plus proche de la section à la plus éloignée ;
        // la première qui ne recouvre rien l'emporte.
        var candidates = new List<(double Cost, double Left, double FirstY, int Side)>();
        foreach (var side in new[] { exteriorSide, -exteriorSide })
            for (var offset = gap; offset <= 320; offset += 12)
                for (var shift = -3 * pitch; shift <= 3 * pitch + 0.1; shift += pitch / 4)
                {
                    var left = side < 0 ? node.X - offset - width : node.X + offset;
                    var cost = (offset - gap) + Math.Abs(shift) * 0.9 + (side == exteriorSide ? 0 : 40);
                    candidates.Add((cost, left, first + shift, side));
                }
        // Pile dans le prolongement de la section, sous son étiquette ou au-dessus du nœud.
        var outward = Math.Sign(node.Y - WheelCenter.Y);
        if (outward != 0)
        {
            var labelHeight = (double)System.Windows.Application.Current.FindResource("SectionLabelHeight");
            var stackFirst = outward > 0
                ? node.Y + OrbitLayout.SectionDiameter / 2 + 4 + labelHeight + 14 + height / 2
                : node.Y - OrbitLayout.SectionDiameter / 2 - 14 - height / 2 - (count - 1) * pitch;
            candidates.Add((60, node.X - width / 2, stackFirst, 0));
        }
        // Une place dont les fils ne traversent aucune autre section est préférée ;
        // sinon la plus proche qui ne recouvre rien.
        (double Left, double FirstY, int Side)? nearest = null;
        foreach (var candidate in candidates.OrderBy(c => c.Cost))
        {
            if (!PlacementFits(node, candidate.Side, candidate.Left, candidate.FirstY, count, pitch, width, height)) continue;
            nearest ??= (candidate.Left, candidate.FirstY, candidate.Side);
            if (!ConnectorsCrossSections(node, candidate.Side, candidate.Left, candidate.FirstY, count, pitch, width))
                return (candidate.Left, candidate.FirstY, candidate.Side, false);
        }
        if (nearest is { } found) return (found.Left, found.FirstY, found.Side, false);
        var originalLeft = exteriorSide < 0 ? node.X - gap - width : node.X + gap;
        return (originalLeft, first, exteriorSide, true);
    }

    private bool PlacementFits(Point node, int side, double left, double firstY,
        int count, double pitch, double width, double height)
    {
        var bounds = HasCustomViewport && _viewportReady ? _screenCanvasBounds : new Rect(30, 120, 1060, 880);
        var minX = bounds.Left + 4;
        var maxX = bounds.Right - 4;
        var minY = bounds.Top + 4;
        var maxY = bounds.Bottom - 4;
        if (left < minX || left + width > maxX ||
            firstY - height / 2 < minY || firstY + (count - 1) * pitch + height / 2 > maxY)
            return false;
        for (var i = 0; i < count; i++)
        {
            var y = firstY + i * pitch;
            var card = new Rect(left, y - height / 2, width, height);
            if (CoveredElements(card).Any()) return false;
            if (card.IntersectsWith(new Rect(WheelCenter.X - 50, WheelCenter.Y - 50, 100, 100))) return false;
            if (side != 0 && ConnectorHubDistance(node, side, side < 0 ? left + width : left, y) < 50) return false;
        }
        return true;
    }

    private IEnumerable<UIElement> CoveredElements(Rect card)
    {
        foreach (var button in _groupButtons.Values)
            if (card.IntersectsWith(new Rect(Canvas.GetLeft(button) - 4, Canvas.GetTop(button) - 4,
                button.Width + 8, button.Height + 8))) yield return button;
        foreach (var label in _groupLabels.Values)
        {
            var width = label.ActualWidth > 0 ? label.ActualWidth : label.DesiredSize.Width;
            if (card.IntersectsWith(new Rect(Canvas.GetLeft(label) - 4, Canvas.GetTop(label) - 4,
                width + 8, label.Height + 8))) yield return label;
        }
    }

    private bool ConnectorsCrossSections(Point node, int side, double left, double firstY, int count, double pitch, double width)
    {
        if (side == 0) return false;
        var own = new Point(node.X, node.Y + OrbitLayout.SectionDiameter / 2 + 8);
        var obstacles = new List<Rect>();
        foreach (var button in _groupButtons.Values)
        {
            var rect = new Rect(Canvas.GetLeft(button), Canvas.GetTop(button), button.Width, button.Height);
            if (!rect.Contains(node)) obstacles.Add(rect);
        }
        foreach (var label in _groupLabels.Values)
        {
            var labelWidth = label.ActualWidth > 0 ? label.ActualWidth : label.DesiredSize.Width;
            var rect = new Rect(Canvas.GetLeft(label), Canvas.GetTop(label), labelWidth, label.Height);
            if (!rect.Contains(own)) obstacles.Add(rect);
        }
        for (var i = 0; i < count; i++)
        {
            var start = new Point(node.X + side * OrbitLayout.SectionDiameter / 2, node.Y);
            var end = new Point(side < 0 ? left + width : left, firstY + i * pitch);
            var bend = Math.Min(40, Math.Max(0, side * (end.X - start.X)) / 2);
            var p1 = new Point(start.X + side * bend, start.Y);
            var p2 = new Point(end.X - side * bend, end.Y);
            for (var sample = 1; sample < 32; sample++)
            {
                var t = sample / 32.0;
                var u = 1 - t;
                var point = new Point(
                    u * u * u * start.X + 3 * u * u * t * p1.X + 3 * u * t * t * p2.X + t * t * t * end.X,
                    u * u * u * start.Y + 3 * u * u * t * p1.Y + 3 * u * t * t * p2.Y + t * t * t * end.Y);
                if (obstacles.Any(r => r.Contains(point))) return true;
            }
        }
        return false;
    }

    private double ConnectorHubDistance(Point node, int side, double innerEdge, double cardY)
    {
        var start = new Point(node.X + side * OrbitLayout.SectionDiameter / 2, node.Y);
        var end = new Point(innerEdge, cardY);
        var bend = Math.Min(40, Math.Max(0, side * (end.X - start.X)) / 2);
        var p1 = new Point(start.X + side * bend, start.Y);
        var p2 = new Point(end.X - side * bend, end.Y);
        var closest = double.PositiveInfinity;
        for (var sample = 0; sample <= 32; sample++)
        {
            var t = sample / 32.0;
            var u = 1 - t;
            var x = u * u * u * start.X + 3 * u * u * t * p1.X + 3 * u * t * t * p2.X + t * t * t * end.X;
            var y = u * u * u * start.Y + 3 * u * u * t * p1.Y + 3 * u * t * t * p2.Y + t * t * t * end.Y;
            closest = Math.Min(closest, Math.Sqrt(Math.Pow(x - WheelCenter.X, 2) + Math.Pow(y - WheelCenter.Y, 2)));
        }
        return closest;
    }

    private void DimCoveredElements(double left, double firstY, int count, double pitch, double width, double height)
    {
        for (var i = 0; i < count; i++)
            foreach (var element in CoveredElements(new Rect(left, firstY + i * pitch - height / 2, width, height)))
                if (!_dimmedBaseElements.Contains(element)) _dimmedBaseElements.Add(element);
        // Dernier recours : ce qui reste sous les cartes s'efface presque entièrement.
        foreach (var element in _dimmedBaseElements) AnimateBaseOpacity(element, 0.06);
    }

    private void RestoreCoveredElements()
    {
        foreach (var element in _dimmedBaseElements) AnimateBaseOpacity(element, TargetOpacity(element));
        _dimmedBaseElements.Clear();
    }

    private void AnimateBaseOpacity(UIElement element, double target)
    {
        var current = element.Opacity;
        element.BeginAnimation(OpacityProperty, null);
        element.Opacity = current;
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(current, target, Motion(Token("MotionQuick"))));
    }

    private void AddExpanded(UIElement element, double delay, int side, int vertical = 0)
    {
        OrbitCanvas.Children.Add(element);
        _expandedElements.Add(element);
        var duration = Motion(Token("MotionCard"));
        var begin = Motion(delay);
        element.BeginAnimation(OpacityProperty, null);
        element.Opacity = 0;
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TargetOpacity(element), duration)
        { BeginTime = begin, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        if (element is not Button card) return;
        if (_wheelEditMode) card.CacheMode = new BitmapCache { RenderAtScale = 1 };
        if (side == 0)
        {
            var slide = new TranslateTransform(0, -vertical * 16);
            card.RenderTransform = slide;
            slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-vertical * 16, 0, duration)
            { BeginTime = begin, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            return;
        }
        var translate = new TranslateTransform(-side * 24, 0);
        card.RenderTransform = translate;
        translate.BeginAnimation(TranslateTransform.XProperty, null);
        translate.X = -side * 24;
        translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-side * 24, 0, duration)
        { BeginTime = begin, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void FadeOutExpanded(UIElement element, bool shrink = true)
    {
        var current = element.Opacity;
        element.BeginAnimation(OpacityProperty, null);
        element.Opacity = current;
        var animation = new DoubleAnimation(current, 0, Motion(Token("MotionQuick")));
        animation.Completed += (_, _) => { OrbitCanvas.Children.Remove(element); if (shrink) UpdateAdaptiveViewport(true); };
        element.BeginAnimation(OpacityProperty, animation);
    }

    private void Window_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_quickSelecting) return;
        _quickSelecting = false;
        _quickHover = null;
        var hit = VisualTreeHelper.HitTest(OrbitCanvas, e.GetPosition(OrbitCanvas))?.VisualHit;
        ReleaseMouseCapture();
        while (hit != null)
        {
            if (hit is Button { Tag: ActionItem action })
            {
                // Une carte-dossier garde sa sous-branche ouverte au lieu de lancer « rien ».
                if (!(Actions.ActionTree.HasChildren(_actions, action.Id) && Actions.ActionTree.IsFolderOnly(action))) Execute(action);
                break;
            }
            hit = VisualTreeHelper.GetParent(hit);
        }
        e.Handled = true;
    }

    // Alt+Tab ou une fenêtre système peut voler la capture en plein geste : sans relâchement reçu,
    // la roue restait figée (sections qui ne se replient plus, glissement jamais terminé).
    private void Window_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, this)) return;
        _quickSelecting = false;
        if (_dragId is null && _transformId is null) return;
        Window_EditMouseUp(this, new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        { RoutedEvent = PreviewMouseLeftButtonUpEvent });
    }

    private void CollapseGroup() => CollapseGroup(false);

    private void CollapseGroup(bool animate)
    {
        RestoreCoveredElements();
        foreach (var element in _expandedElements)
            if (animate) FadeOutExpanded(element); else OrbitCanvas.Children.Remove(element);
        _expandedElements.Clear();
        _openCards.Clear();
        _cardSlots.Clear();
        _activeGroup = null;
        foreach (var button in _groupButtons.Values) button.Tag = null;
        foreach (var spoke in _spokes.Values)
        {
            spoke.Stroke = BrushToken("WidgetSpokeBrush");
            spoke.StrokeThickness = Token("SpokeThickness");
            UiOverrides.Apply(spoke, UiRuntime.Profile);
        }
        DrawBranchPreviews();
        if (!animate) UpdateAdaptiveViewport(true);
    }

    // Après 10 s sans plantage, le démarrage est réussi ; un message éventuel (mode de secours,
    // profil illisible) s'affiche une fois la roue visible.
    private void ReportStartup()
    {
        if (!DiagnosticFlags.Enabled)
        {
            var started = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            started.Tick += (_, _) =>
            {
                started.Stop();
                try { SafeMode.MarkStarted(SafeMode.Folder); } catch (IOException) { }
            };
            started.Start();
        }
        if (SafeMode.StartupMessage is { } message)
        {
            SafeMode.StartupMessage = null;
            _ = Dialogs.InfoAsync(null, SafeMode.Active ? "Mode de secours" : "Interface d'origine", message);
        }
    }

    internal async Task ResetInterfaceAsync()
    {
        if (!await Dialogs.ConfirmAsync(null, "Réinitialiser l'interface",
            "La roue reprend sa disposition d'origine (places, tailles). Vos boutons sont gardés.", "Réinitialiser"))
            return;
        if (_wheelEditMode) EndWheelEditing();
        _settings.InterfaceProfile = UiProfileStore.OriginName;
        _settings.Save();
        UiRuntime.SetProfile(new UiProfile());
    }

    // Un nouveau profil d'interface redessine la roue tout de suite, sans animation,
    // en gardant la branche ouverte et la sélection de l'éditeur.
    private void OnInterfaceChanged()
    {
        if (!IsVisible || _isHiding || _folding) return;
        var active = _activeGroup;
        _instantMotion = true;
        try
        {
            RenderOrbit();
            if (active is not null && _groupInfos.TryGetValue(active, out var open)) ExpandGroup(open.Info, open.Angle);
        }
        finally { _instantMotion = _wheelEditMode; }
        DrawEditSelection();
    }

    // ---------- Éditeur d'interface ----------

    private bool _wheelEditMode;
    private string? _editSelection;
    private string? _dragId;
    // Départ du glisser en pixels d'écran : ouvrir une sous-branche peut agrandir la fenêtre sous la souris ;
    // un glisser se mesure donc à l'écran, puis se convertit avec la position actuelle de la roue.
    private Point _dragOriginScreen;
    private Point _dragCenter;
    private bool _dragMoved;
    private FrameMeter? _dragMeter;
    private int _renderCount;
    private int _dragRenderStart;
    private readonly List<double> _layoutUpdateTimes = [];
    private string? _transformId;
    private bool _transformRotationMode;
    private Point _transformOrigin;
    private Point _transformCenter;
    private double _transformWidth;
    private double _transformHeight;
    private double _transformRotation;
    private double _transformStartAngle;
    private bool _transformMoved;
    public event Action<string>? WheelElementPicked;
    public event Action<string, Vector, bool>? WheelElementDragged;
    public event Action<string, string, double, bool>? WheelElementTransformed;
    public event Action<System.Windows.Input.KeyEventArgs>? WheelKeyDown;

    public void SetInterfaceProfile(string name)
    {
        _settings.InterfaceProfile = name;
        _settings.Save();
    }

    // Mode édition : la roue passe devant, reste ouverte, sans animation ; un clic sélectionne.
    public bool WheelEditMode
    {
        get => _wheelEditMode;
        set
        {
            if (_wheelEditMode == value) return;
            _wheelEditMode = value;
            _instantMotion = value;
            _idleReset.Stop();
            if (value)
            {
                if (!IsVisible || _isHiding) ShowOrbit();
                if (_folded) { _folded = false; RenderOrbit(); }
                Root.BeginAnimation(OpacityProperty, null);
                Root.Opacity = 1;
                Topmost = true;
                Activate();
            }
            else
            {
                _dragId = _transformId = null;
                _cardDrag = null;
                _dragMeter?.End();
                _dragMeter = null;
                ReleaseMouseCapture();
                Topmost = false;
                EditOverlay.Children.Clear();
                Dispatcher.BeginInvoke(SendBehindWindows);
            }
            // Les ciseaux n'existent qu'en mode édition : la branche ouverte se reconstruit avec ou sans eux.
            if (_activeGroup is not null) RebuildActiveBranch();
            DrawBranchPreviews();
            DrawEditSelection();
        }
    }

    public (System.Windows.Media.Imaging.BitmapSource? Image, double Scale) SnapshotWheel()
    {
        var scale = (OrbitCanvas.RenderTransform as ScaleTransform)?.ScaleX ?? 1;
        if (!IsVisible || ActualWidth <= 0) return (null, scale);
        var dpi = VisualTreeHelper.GetDpi(this);
        var overlay = EditOverlay.Visibility;
        EditOverlay.Visibility = Visibility.Hidden;
        var image = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Round(ActualWidth * dpi.DpiScaleX),
            (int)Math.Round(ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        image.Render(Root);
        EditOverlay.Visibility = overlay;
        image.Freeze();
        return (image, scale);
    }

    public void ShowWheelSelection(string? id)
    {
        _editSelection = id;
        DrawEditSelection();
    }

    public Rect? WheelSelectionScreenBounds(string? id)
    {
        if (id is null) return null;
        var selection = Rect.Empty;
        foreach (var element in WheelElements(id))
            if (CanvasBounds(element) is { } bounds) selection.Union(bounds);
        if (selection.IsEmpty) return null;
        return new Rect(OrbitCanvas.PointToScreen(selection.TopLeft), OrbitCanvas.PointToScreen(selection.BottomRight));
    }

    private IEnumerable<FrameworkElement> WheelElements(string id)
    {
        var all = new List<FrameworkElement>();
        void Walk(DependencyObject node)
        {
            if (node is FrameworkElement element && UiId.Get(element) is { } elementId &&
                (elementId == id || elementId.StartsWith(id + ":")))
                all.Add(element);
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) Walk(child);
        }
        Walk(OrbitCanvas);
        return all;
    }

    private Rect? CanvasBounds(FrameworkElement element)
    {
        if (!element.IsVisible || element.ActualWidth <= 0 && element is not Line) return null;
        try { return element.TransformToAncestor(OrbitCanvas).TransformBounds(new Rect(element.RenderSize)); }
        catch (InvalidOperationException) { return null; }
    }

    private void DrawEditSelection()
    {
        UpdateEditingBanner();
        if (_dragId is not null) _dragSelectionRedraws++;
        var edgeGuide = EditOverlay.Children.OfType<FrameworkElement>().FirstOrDefault(item => item.Tag as string == "screen-edge");
        var magnetGuide = EditOverlay.Children.OfType<FrameworkElement>().FirstOrDefault(item => item.Tag as string == "card-magnet");
        var sectionTarget = EditOverlay.Children.OfType<FrameworkElement>().FirstOrDefault(item => item.Tag as string == "section-target");
        EditOverlay.Children.Clear();
        if (magnetGuide is not null && _cardDrag is not null) EditOverlay.Children.Add(magnetGuide);
        if (sectionTarget is not null && _dragId is not null) EditOverlay.Children.Add(sectionTarget);
        if (!WheelEditingRules.DrawSelection(_wheelEditMode, _editSelection) || _editSelection is not { } id) return;
        if (edgeGuide is not null) EditOverlay.Children.Add(edgeGuide);
        UpdateLayout();
        var handlesAdded = false;
        Rect? cardBounds = null;
        foreach (var element in WheelElements(id))
        {
            if (CanvasBounds(element) is not { } bounds) continue;
            cardBounds ??= bounds;
            var outline = new System.Windows.Shapes.Rectangle
            {
                Width = Math.Max(4, bounds.Width + 6), Height = Math.Max(4, bounds.Height + 6), RadiusX = 4, RadiusY = 4,
                Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0x3E, 0xA5)), StrokeThickness = 2,
                StrokeDashArray = [3, 2], IsHitTestVisible = false
            };
            Canvas.SetLeft(outline, bounds.X - 3);
            Canvas.SetTop(outline, bounds.Y - 3);
            EditOverlay.Children.Add(outline);
            if (!_wheelEditMode || handlesAdded || !UiOverrides.Supports(element, "Width")) continue;
            handlesAdded = true;
            AddTransformHandle(element, id, bounds, false);
            AddTransformHandle(element, id, bounds, true);
        }
        if (_wheelEditMode && cardBounds is { } card && id.StartsWith("wheel.card:", StringComparison.Ordinal) &&
            Guid.TryParse(id["wheel.card:".Length..], out var cardId) && _actions.FirstOrDefault(a => a.Id == cardId) is { } action)
            AddSubCardPlus(card, action);
    }

    private void AddTransformHandle(FrameworkElement element, string id, Rect bounds, bool rotate)
    {
        var handle = new Ellipse { Width = 12, Height = 12,
            Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x3E, 0xA5)),
            Stroke = Brushes.White, StrokeThickness = 1.5,
            Cursor = rotate ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.SizeNWSE,
            ToolTip = rotate ? "Faire pivoter" : "Redimensionner" };
        Canvas.SetLeft(handle, rotate ? bounds.X + bounds.Width / 2 - 6 : bounds.Right - 6);
        Canvas.SetTop(handle, rotate ? bounds.Y - 22 : bounds.Bottom - 6);
        handle.PreviewMouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            _transformId = id;
            _transformRotationMode = rotate;
            _transformOrigin = e.GetPosition(OrbitCanvas);
            _transformCenter = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
            _transformWidth = CurrentEditorNumber(id, "Width", element.ActualWidth);
            _transformHeight = CurrentEditorNumber(id, "Height", element.ActualHeight);
            _transformRotation = CurrentEditorNumber(id, "Rotation", 0);
            _transformStartAngle = Math.Atan2(_transformOrigin.Y - _transformCenter.Y,
                _transformOrigin.X - _transformCenter.X);
            _transformMoved = false;
            CaptureMouse();
        };
        EditOverlay.Children.Add(handle);
    }

    private static double CurrentEditorNumber(string id, string property, double fallback) =>
        UiOverrides.Resolve(UiRuntime.Profile, id).TryGetValue(property, out var text) &&
        double.TryParse(text, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private void OrbitCanvas_EditMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_wheelEditMode) return;
        e.Handled = true;
        if (TryCutAt(e.OriginalSource as DependencyObject)) return;
        if (TryAddAt(e.OriginalSource as DependencyObject)) return;
        var node = e.OriginalSource as DependencyObject;
        // Un aperçu cliqué : sa branche s'ouvre et son bouton est sélectionné.
        if (PreviewAt(node) is { } preview)
        {
            if (ActivatePreview(preview) is not { } real) return;
            node = real;
        }
        while (node is not null && !ReferenceEquals(node, OrbitCanvas) && !(node is FrameworkElement { } candidate && UiId.Get(candidate) is not null))
            node = VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node);
        if (node is not FrameworkElement element || ReferenceEquals(node, OrbitCanvas) || UiId.Get(element) is null) return;
        // Un clic prend l'élément entier (la section, la carte) ; Alt+clic garde la partie visée (icône, titre).
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            for (var parent = LogicalTreeHelper.GetParent(element); parent is not null && !ReferenceEquals(parent, OrbitCanvas); parent = LogicalTreeHelper.GetParent(parent))
                if (parent is FrameworkElement outer && UiId.Get(outer) is not null) element = outer;
        var id = UiId.Get(element)!;
        if (id == "wheel.hub.hit") id = "wheel.hub";
        // Une section cliquée s'ouvre aussi, pour atteindre ses cartes.
        if (id.StartsWith("wheel.section:") && _groupInfos.TryGetValue(id["wheel.section:".Length..], out var group))
            ExpandGroup(group.Info, group.Angle);
        // Une carte à sous-cartes cliquée s'ouvre aussi, pour atteindre ses sous-cartes.
        if (id.StartsWith("wheel.card:") && element is Button { Tag: ActionItem cardAction } cardButton && Actions.ActionTree.HasChildren(_actions, cardAction.Id))
            ExpandCard(cardAction, cardButton);
        // Un modèle sélectionné (« toutes les cartes ») reste sélectionné quand on clique une de ses instances.
        var target = !id.StartsWith("wheel.section:") && !id.StartsWith("wheel.label") && !id.StartsWith("wheel.badge:") &&
            _editSelection is { } selected && id.StartsWith(selected + ":") ? selected : id;
        if (target != _editSelection) WheelElementPicked?.Invoke(target);
        _editSelection = target;
        DrawEditSelection();
        _dragId = target;
        _dragOriginScreen = CursorOnScreen();
        _dragCenter = CanvasBounds(element) is { } bounds ? new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2) : e.GetPosition(OrbitCanvas);
        if (id.StartsWith("wheel.section:") && WheelPosition(id) is { } sectionPosition) _dragCenter = ToPoint(sectionPosition);
        _dragMoved = false;
        if (DiagnosticFlags.Enabled && id.StartsWith("wheel.section:"))
        {
            _dragMeter = new FrameMeter(true);
            _dragMeter.Begin("layout/glisser");
            _dragRenderStart = _renderCount;
            _layoutUpdateTimes.Clear();
        }
        CaptureMouse();
    }

    private void Window_EditMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_quickSelecting) { QuickSelectHover(e.GetPosition(OrbitCanvas)); return; }
        if (_transformId is { } transformId && IsMouseCaptured)
        {
            var point = e.GetPosition(OrbitCanvas);
            if (!_transformMoved && (point - _transformOrigin).Length < 3) return;
            _transformMoved = true;
            if (_transformRotationMode)
            {
                var angle = Math.Atan2(point.Y - _transformCenter.Y, point.X - _transformCenter.X);
                var rotation = _transformRotation + (angle - _transformStartAngle) * 180 / Math.PI;
                WheelElementTransformed?.Invoke(transformId, "Rotation", Math.Round(rotation), false);
            }
            else
            {
                var resizeDelta = point - _transformOrigin;
                WheelElementTransformed?.Invoke(transformId, "Width", Math.Round(Math.Max(8, _transformWidth + resizeDelta.X)), false);
                WheelElementTransformed?.Invoke(transformId, "Height", Math.Round(Math.Max(8, _transformHeight + resizeDelta.Y)), false);
            }
            return;
        }
        if (_dragId is null || !IsMouseCaptured) return;
        if (!_dragMoved && (CursorOnScreen() - _dragOriginScreen).Length < 3) return;
        _dragMoved = true;
        var delta = DragDelta();
        WheelElementDragged?.Invoke(_dragId, Snap(delta), false);
        if (_dragId.StartsWith("wheel.section:")) _ = SnapSection(delta);
    }

    private Point CursorOnScreen() => GetCursorPos(out var cursor) ? new Point(cursor.X, cursor.Y) : PointToScreen(Mouse.GetPosition(this));

    private Vector DragDelta() => OrbitCanvas.PointFromScreen(CursorOnScreen()) - OrbitCanvas.PointFromScreen(_dragOriginScreen);

    private void Window_EditMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_transformId is { } transformId)
        {
            _transformId = null;
            ReleaseMouseCapture();
            if (_transformMoved)
            {
                var point = e.GetPosition(OrbitCanvas);
                if (_transformRotationMode)
                {
                    var angle = Math.Atan2(point.Y - _transformCenter.Y, point.X - _transformCenter.X);
                    WheelElementTransformed?.Invoke(transformId, "Rotation",
                        Math.Round(_transformRotation + (angle - _transformStartAngle) * 180 / Math.PI), true);
                }
                else
                {
                    var delta = point - _transformOrigin;
                    WheelElementTransformed?.Invoke(transformId, "Width", Math.Round(Math.Max(8, _transformWidth + delta.X)), false);
                    WheelElementTransformed?.Invoke(transformId, "Height", Math.Round(Math.Max(8, _transformHeight + delta.Y)), true);
                }
            }
            e.Handled = true;
            return;
        }
        if (_dragId is null) return;
        var id = _dragId;
        var finalDelta = Snap(DragDelta());
        _dragId = null;
        ReleaseMouseCapture();
        if (_dragMoved) WheelElementDragged?.Invoke(id, finalDelta, true);
        _dragMeter?.End();
        _dragMeter = null;
        if (DiagnosticFlags.Enabled && _layoutUpdateTimes.Count > 0)
        {
            var sorted = _layoutUpdateTimes.Order().ToArray();
            File.AppendAllText(System.IO.Path.Combine(DiagnosticFlags.DirectoryPath, "layout-drag.log"),
                $"{id}: updates={sorted.Length}, updateMedian={sorted[sorted.Length / 2]:F3}ms, updateMax={sorted[^1]:F3}ms, fullRenders={_renderCount - _dragRenderStart}{Environment.NewLine}");
        }
        EditOverlay.Children.OfType<Line>().ToList().ForEach(EditOverlay.Children.Remove);
        EditOverlay.Children.OfType<Ellipse>().Where(item => item.Tag as string == "layout-guide").ToList().ForEach(EditOverlay.Children.Remove);
        e.Handled = true;
    }

    // Grille, puis aimantation sur les axes du centre de la roue (guides roses).
    private Vector Snap(Vector delta)
    {
        if (_dragId?.StartsWith("wheel.section:", StringComparison.Ordinal) == true) return SnapSection(delta);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return delta;
        var center = WheelCenter;
        var moved = _dragCenter + delta;
        EditOverlay.Children.OfType<Line>().ToList().ForEach(EditOverlay.Children.Remove);
        var guide = new SolidColorBrush(Color.FromRgb(0xFF, 0x3E, 0xA5));
        if (Math.Abs(moved.X - center.X) < 6)
        {
            delta.X += center.X - moved.X;
            EditOverlay.Children.Add(new Line { X1 = center.X, X2 = center.X, Y1 = 0, Y2 = OrbitLayout.CanvasSize, Stroke = guide, StrokeThickness = 1 });
        }
        if (Math.Abs(moved.Y - center.Y) < 6)
        {
            delta.Y += center.Y - moved.Y;
            EditOverlay.Children.Add(new Line { X1 = 0, X2 = OrbitLayout.CanvasSize, Y1 = center.Y, Y2 = center.Y, Stroke = guide, StrokeThickness = 1 });
        }
        return delta;
    }

    // Sans interaction pendant quelques secondes, la roue revient à son état de départ.
    private void ResetAfterIdle(object? sender, EventArgs e)
    {
        _idleReset.Stop();
        if (_wheelEditMode || IsMouseOver || _quickSelecting || _folding || _activeGroup is null) return;
        CollapseGroup(true);
    }

    // Animation inverse de l'ouverture : les sections rentrent dans le centre avant le repli.
    private void FoldWheel()
    {
        if (_folding) return;
        _folding = true;
        _treeOpen = false;
        CollapseGroup(true);
        var center = WheelCenter;
        var duration = Motion(Token("MotionFold"));
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseIn };
        DoubleAnimation To(double value, TimeSpan time) => new(value, time) { EasingFunction = easing };
        foreach (var element in OrbitCanvas.Children.Cast<UIElement>().ToList())
        {
            if (ReferenceEquals(element, _hub)) continue;
            if (element is Button node && _groupButtons.ContainsValue(node) &&
                node.RenderTransform is TransformGroup group && group.Children.Count == 2 &&
                group.Children[0] is ScaleTransform scale && group.Children[1] is TranslateTransform translate)
            {
                var nodeCenter = new Point(Canvas.GetLeft(node) + node.Width / 2, Canvas.GetTop(node) + node.Height / 2);
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, To(0.6, duration));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, To(0.6, duration));
                translate.BeginAnimation(TranslateTransform.XProperty, To(center.X - nodeCenter.X, duration));
                translate.BeginAnimation(TranslateTransform.YProperty, To(center.Y - nodeCenter.Y, duration));
                node.BeginAnimation(OpacityProperty, new DoubleAnimation(0, Motion(Token("MotionFade"))) { BeginTime = Motion(Token("MotionFoldDelay")) });
                continue;
            }
            if (element is Line spoke && _spokes.ContainsValue(spoke))
            {
                spoke.BeginAnimation(Line.X2Property, To(center.X, duration));
                spoke.BeginAnimation(Line.Y2Property, To(center.Y, duration));
            }
            element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, Motion(Token("MotionFade"))));
        }
        var done = new System.Windows.Threading.DispatcherTimer { Interval = duration + TimeSpan.FromMilliseconds(20) };
        done.Tick += (_, _) =>
        {
            done.Stop();
            _folding = false;
            _folded = true;
            RenderOrbit();
        };
        done.Start();
    }

    private void StartLiveSubtitles()
    {
        if (_liveLabels.Count == 0) return;
        _liveSubtitles ??= new LiveSubtitles();
        _ = PrimeCpuAsync(_liveSubtitles);
        _subtitleTimer ??= new System.Windows.Threading.DispatcherTimer
        { Interval = TimeSpan.FromSeconds(2) };
        _subtitleTimer.Tick -= RefreshLiveSubtitles;
        _subtitleTimer.Tick += RefreshLiveSubtitles;
        _subtitleTimer.Start();
        _ = RefreshFilesSubtitleAsync(_visibilityGeneration);
    }

    private async Task RefreshFilesSubtitleAsync(int generation)
    {
        try
        {
            var files = await Task.Run(LiveSubtitles.FilesSubtitle);
            if (generation != _visibilityGeneration || _windowClosing || Dispatcher.HasShutdownStarted || !IsVisible || files is null) return;
            foreach (var entry in _liveLabels)
                if (entry.Type == "Fichiers") SetLiveSubtitle(entry, files);
        }
        catch (Exception ex) { CrashLog.Write("RefreshFilesSubtitleAsync", ex); }
    }

    private static async Task PrimeCpuAsync(LiveSubtitles service)
    {
        try { await Task.Run(service.PrimeCpu); }
        catch (Exception ex) { CrashLog.Write("PrimeCpuAsync", ex); }
    }

    private async void RefreshLiveSubtitles(object? sender, EventArgs e)
    {
        if (_subtitleBusy || !IsVisible || _isHiding || _liveSubtitles is null) return;
        _subtitleBusy = true;
        var generation = _visibilityGeneration;
        try
        {
            var service = _liveSubtitles;
            var result = await Task.Run(() => (System: service.SystemSubtitle(), Files: LiveSubtitles.FilesSubtitle()));
            if (generation != _visibilityGeneration || _windowClosing || Dispatcher.HasShutdownStarted || !IsVisible || _isHiding) return;
            foreach (var entry in _liveLabels)
            {
                if (entry.Type == "Système" && result.System is not null) SetLiveSubtitle(entry, result.System);
                if (entry.Type == "Fichiers" && result.Files is not null) SetLiveSubtitle(entry, result.Files);
            }
        }
        catch (Exception ex) { CrashLog.Write("RefreshLiveSubtitles", ex); }
        finally { _subtitleBusy = false; }
    }

    private static void SetLiveSubtitle((string Type, Border Label, TextBlock Subtitle, double CenterX) entry, string value)
    {
        if (entry.Subtitle.Text == value) return;
        entry.Subtitle.Text = value;
        entry.Label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(entry.Label, Math.Round(entry.CenterX - entry.Label.DesiredSize.Width / 2));
    }

    private Button SearchButton()
    {
        var icon = new Canvas { Width = 16, Height = 16, IsHitTestVisible = false };
        var iconBrush = BrushToken("SearchIconBrush");
        var glass = new Ellipse { Width = 9, Height = 9, Stroke = iconBrush, StrokeThickness = 1.5 };
        Canvas.SetLeft(glass, 1);
        Canvas.SetTop(glass, 1);
        icon.Children.Add(glass);
        icon.Children.Add(new Line { X1 = 9, Y1 = 9, X2 = 14, Y2 = 14,
            Stroke = iconBrush, StrokeThickness = 1.5 });
        var size = Token("SearchDiameter");
        var button = new Button { Width = size, Height = size, Content = icon, Opacity = 0,
            Style = (Style)System.Windows.Application.Current.FindResource("OrbitNode") };
        // Loupe : la roue se replie comme au clic sur le rond du milieu (elle reste là), puis la recherche Windows s'ouvre.
        button.Click += (_, _) =>
        {
            if (!_folded) FoldWheel();
            keybd_event(0x5B, 0, 0, UIntPtr.Zero);
            keybd_event(0x53, 0, 0, UIntPtr.Zero);
            keybd_event(0x53, 0, 2, UIntPtr.Zero);
            keybd_event(0x5B, 0, 2, UIntPtr.Zero);
        };
        return button;
    }

    private double IdleOpacity() => _settings.IdleOpacity ?? HotkeySettings.PresetIdleOpacity(_settings.Idle);

    private void AnimateIdleOpacity(double target)
    {
        if (!IsVisible || _isHiding || _wheelEditMode) return;
        var current = Root.Opacity;
        Root.BeginAnimation(OpacityProperty, null);
        Root.Opacity = current;
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(current, target, Motion(Token("MotionIdle"))));
    }

    private void Execute(ActionItem action)
    {
        CollapseGroup();
        _ = RunActionAsync(action);
    }

    public Task TestAction(ActionItem action) => RunActionAsync(action);

    private async Task RunActionAsync(ActionItem action)
    {
        RunResult? result;
        try { result = await _runner.RunAsync(action); }
        catch (OperationCanceledException) { return; }
        if (result is not { Success: false, Message: { } message }) return;
        var verb = action.Steps is [OpenStep] ? "d'ouvrir" : "d'exécuter";
        Notifications.Show($"Impossible {verb} « {action.Name} »", message);
    }

    private readonly ActionRunner _runner = new(new SystemStepExecutor());

    private WheelEditor? _wheelEditor;

    // Clic droit sur le rond du milieu › Modifier la roue : la roue passe en mode Modifier, sans fenêtre.
    internal void StartWheelEditing()
    {
        if (_wheelEditMode || _wheelEditor is not null) return;
        var editor = new WheelEditor(this, _settings.Theme != "Clair");
        _wheelEditor = editor;
        editor.CommandsChanged += UpdateEditingCommands;
        editor.ActionsSaved += () => _actions = ActionStore.Load();
        editor.Stopped += () => { if (_wheelEditor == editor) _wheelEditor = null; UpdateEditingCommands(); };
        UpdateEditingCommands();
    }

    private void ApplyEditorChanges()
    {
        var previous = _settings;
        var previousActions = System.Text.Json.JsonSerializer.Serialize(_actions);
        _actions = ActionStore.Load();
        _settings = HotkeySettings.Load();
        var hotkeyChanged = previous.Control != _settings.Control || previous.Alt != _settings.Alt ||
            previous.Shift != _settings.Shift || previous.Win != _settings.Win || previous.Key != _settings.Key;
        if (hotkeyChanged && !RegisterConfiguredHotkey())
        {
            _settings.Control = previous.Control;
            _settings.Alt = previous.Alt;
            _settings.Shift = previous.Shift;
            _settings.Win = previous.Win;
            _settings.Key = previous.Key;
            _settings.Save();
            RegisterConfiguredHotkey();
            _ = Dialogs.InfoAsync(null, "Raccourci indisponible", "Ce raccourci est déjà utilisé par une autre application ou réservé par Windows. L'ancien raccourci a été conservé.");
        }
        // Les comportements (survol, clic, vitesse) sont lus à la volée : seule une nouvelle
        // liste d'actions ou un nouveau thème demande de reconstruire la roue.
        if (previous.Theme != _settings.Theme || previous.GlassOpacity != _settings.GlassOpacity ||
            previousActions != System.Text.Json.JsonSerializer.Serialize(_actions))
        {
            if (previousActions != System.Text.Json.JsonSerializer.Serialize(_actions)) _wheelEditor?.ReloadActions();
            _instantMotion = true;
            try { RenderOrbit(); }
            // En édition les mouvements restent instantanés (voir WheelEditMode).
            finally { _instantMotion = _wheelEditMode; }
            Dispatcher.BeginInvoke(SendBehindWindows);
        }
        if (previous.IdleOpacity != _settings.IdleOpacity && !IsMouseOver) AnimateIdleOpacity(IdleOpacity());
        _idleReset.Interval = TimeSpan.FromSeconds(_settings.BranchCloseSeconds);
    }

    public void ApplyActions(IReadOnlyList<ActionItem> actions)
    {
        // Section renommée (une disparaît, une apparaît) : sa branche ouverte reste ouverte sous le nouveau nom.
        static HashSet<string> Names(IEnumerable<ActionItem> list) => list.Select(a => a.Group.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var (before, after) = (Names(_actions), Names(actions));
        if (_activeGroup is { } open && !after.Contains(open.Trim()) &&
            after.Except(before, StringComparer.OrdinalIgnoreCase).ToList() is [var renamed] &&
            before.Except(after, StringComparer.OrdinalIgnoreCase).Count() == 1)
            _activeGroup = renamed;
        _actions = System.Text.Json.JsonSerializer.Deserialize<List<ActionItem>>(
            System.Text.Json.JsonSerializer.Serialize(actions, Actions.ActionJson.Options), Actions.ActionJson.Options) ?? [];
        // Même chemin qu'un nouveau profil : redessin instantané, branche ouverte gardée.
        OnInterfaceChanged();
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Échap termine l'édition ; pendant un geste il ne ferme jamais la roue.
        if (_wheelEditMode)
        {
            if (e.Key == Key.Escape)
            {
                if (!WheelGestureActive) EndWheelEditing();
                e.Handled = true;
                return;
            }
            WheelKeyDown?.Invoke(e);
            return;
        }
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        HideOrbit();
    }
}


