using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OrbitWeave.Orbit;

internal sealed class LiveSubtitles : IDisposable
{
    private PerformanceCounter? _cpu;
    private bool _primed;
    private bool _disposed;
    private readonly object _gate = new();

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    public void PrimeCpu()
    {
        lock (_gate) { if (!_disposed) PrimeCpuCore(); }
    }

    private void PrimeCpuCore()
    {
        if (_primed) return;
        try
        {
            _cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total", true);
            _cpu.NextValue();
            _primed = true;
        }
        catch (Exception)
        {
            DisposeCounter();
            _cpu = null;
        }
    }

    public string? SystemSubtitle()
    {
        lock (_gate) { return _disposed ? null : ReadSystemSubtitle(); }
    }

    private string? ReadSystemSubtitle()
    {
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        var ram = GlobalMemoryStatusEx(ref memory) ? $"RAM {memory.MemoryLoad}%" : null;
        string? cpu = null;
        try
        {
            if (_primed && _cpu is not null)
                cpu = $"CPU {Math.Clamp((int)Math.Round(_cpu.NextValue()), 0, 100)}%";
        }
        catch (Exception) { }
        return cpu is not null && ram is not null ? $"{cpu} · {ram}" : ram;
    }

    public static string? FilesSubtitle()
    {
        try
        {
            var drive = new DriveInfo("C");
            if (!drive.IsReady) return null;
            return $"C: libre {Math.Round(drive.AvailableFreeSpace / 1024d / 1024 / 1024):N0} Go";
        }
        catch (Exception) { return null; }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            DisposeCounter();
        }
    }

    private void DisposeCounter()
    {
        try { _cpu?.Dispose(); }
        catch (Exception ex) { CrashLog.Write("LiveSubtitles.Dispose", ex); }
        finally { _cpu = null; }
    }
}
