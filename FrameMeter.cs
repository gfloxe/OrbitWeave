using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Media;

namespace OrbitWeave;

internal sealed class FrameMeter
{
    private readonly bool _trace;
    public FrameMeter(bool trace = false) => _trace = trace;
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern int GetDeviceCaps(IntPtr hdc, int index);

    private static int RefreshRateHz()
    {
        var dc = GetDC(IntPtr.Zero);
        if (dc == IntPtr.Zero) return 0;
        try { return GetDeviceCaps(dc, 116); }
        finally { ReleaseDC(IntPtr.Zero, dc); }
    }
    private readonly List<(double Time, double Interval)> _samples = [];
    private readonly List<(double Time, double Interval)> _renderIntervals = [];
    // Temps passé par le fil de l'interface après chaque image (mise en page, dessin…) avant d'être de nouveau libre.
    private readonly List<double> _busy = [];
    private TimeSpan? _lastRenderingTime;
    private string _phase = "";
    private long _started;
    private long _lastFrame;
    private TimeSpan _cpuStarted;
    private bool _active;

    public void Begin(string phase)
    {
        if (_active) End();
        _phase = phase;
        _samples.Clear();
        _renderIntervals.Clear();
        _busy.Clear();
        _lastRenderingTime = null;
        _started = Stopwatch.GetTimestamp();
        _lastFrame = 0;
        _cpuStarted = Process.GetCurrentProcess().TotalProcessorTime;
        _active = true;
        CompositionTarget.Rendering += OnRendering;
    }

    public void End(double? sampleUntilMs = null)
    {
        if (!_active) return;
        CompositionTarget.Rendering -= OnRendering;
        _active = false;
        var elapsed = Stopwatch.GetElapsedTime(_started).TotalMilliseconds;
        var process = Process.GetCurrentProcess();
        var cpu = (process.TotalProcessorTime - _cpuStarted).TotalMilliseconds /
                  Math.Max(1, elapsed * Environment.ProcessorCount) * 100;
        var sorted = _samples.Where(sample => !sampleUntilMs.HasValue || sample.Time <= sampleUntilMs.Value)
            .Select(sample => sample.Interval).Order().ToArray();
        var median = Percentile(sorted, 0.5);
        var p95 = Percentile(sorted, 0.95);
        var renderSorted = _renderIntervals.Where(sample => !sampleUntilMs.HasValue || sample.Time <= sampleUntilMs.Value)
            .Select(sample => sample.Interval).Order().ToArray();
        try
        {
            Directory.CreateDirectory(DiagnosticFlags.DirectoryPath);
            File.AppendAllText(Path.Combine(DiagnosticFlags.DirectoryPath, "perf.log"),
                $"{DateTime.Now:O} | {_phase} | refreshHz={RefreshRateHz()} | frames={sorted.Length + (sorted.Length == 0 ? 0 : 1)} | median={median:F1}ms | p95={p95:F1}ms | >25ms={sorted.Count(v => v > 25)} | >50ms={sorted.Count(v => v > 50)} | renderMedian={Percentile(renderSorted, 0.5):F1}ms | renderP95={Percentile(renderSorted, 0.95):F1}ms | busyMedian={Percentile([.. _busy.Order()], 0.5):F1}ms | busyP95={Percentile([.. _busy.Order()], 0.95):F1}ms | window={sampleUntilMs?.ToString("F0") ?? "all"}ms | elapsed={elapsed:F0}ms | cpu={cpu:F2}% | workingSet={process.WorkingSet64 / 1048576.0:F1}MiB{Environment.NewLine}");
            if (_trace)
                File.AppendAllLines(Path.Combine(DiagnosticFlags.DirectoryPath, "perf-frames.csv"),
                    _samples.Select(sample => $"{_phase};{sample.Time:F2};{sample.Interval:F2}"));
        }
        catch { }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = Stopwatch.GetTimestamp();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle,
            () => { if (_active) _busy.Add(Stopwatch.GetElapsedTime(now).TotalMilliseconds); });
        if (e is RenderingEventArgs rendering)
        {
            if (_lastRenderingTime.HasValue)
                _renderIntervals.Add(((now - _started) * 1000.0 / Stopwatch.Frequency,
                    (rendering.RenderingTime - _lastRenderingTime.Value).TotalMilliseconds));
            _lastRenderingTime = rendering.RenderingTime;
        }
        if (_lastFrame != 0)
        {
            var interval = (now - _lastFrame) * 1000.0 / Stopwatch.Frequency;
            _samples.Add(((now - _started) * 1000.0 / Stopwatch.Frequency, interval));
        }
        _lastFrame = now;
    }

    private static double Percentile(double[] values, double p)
    {
        if (values.Length == 0) return 0;
        var index = Math.Clamp((int)Math.Ceiling(values.Length * p) - 1, 0, values.Length - 1);
        return values[index];
    }
}
