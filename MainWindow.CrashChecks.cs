using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;

namespace OrbitWeave;

public partial class MainWindow
{
    internal void StartCrashChecks()
    {
        var step = 0;
        var detached = new MainWindow();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += (_, _) =>
        {
            try
            {
                if (step == 0)
                {
                    var handled = false;
                    detached.WndProc(IntPtr.Zero, 0x0084, IntPtr.Zero, IntPtr.Zero, ref handled);
                    File.AppendAllText(Path.Combine(DiagnosticFlags.DirectoryPath, "crash-checks.log"), "detached hit-test: no exception" + Environment.NewLine);
                    Hide();
                    WndProc(IntPtr.Zero, 0x0084, IntPtr.Zero, IntPtr.Zero, ref handled);
                    Show();
                    File.AppendAllText(Path.Combine(DiagnosticFlags.DirectoryPath, "crash-checks.log"), "hidden hit-test: no exception" + Environment.NewLine);
                    _windowClosing = true;
                    WndProc(IntPtr.Zero, 0x0084, IntPtr.Zero, IntPtr.Zero, ref handled);
                    _windowClosing = false;
                    File.AppendAllText(Path.Combine(DiagnosticFlags.DirectoryPath, "crash-checks.log"), "closing hit-test: no exception" + Environment.NewLine);
                    CreateUnobservedFault();
                    Dispatcher.BeginInvoke(() => throw new InvalidOperationException("diagnostic/dispatcher"));
                }
                else if (step < 8)
                {
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                }
                else
                {
                    timer.Stop();
                    var log = File.ReadAllText(CrashLog.FilePath);
                    if (!log.Contains("TaskScheduler.UnobservedTaskException") || !log.Contains("DispatcherUnhandledException"))
                        throw new InvalidOperationException("Hook de journalisation non observé");
                    File.WriteAllText(Path.Combine(DiagnosticFlags.DirectoryPath, "crash-checks-complete.txt"), "complete");
                    Close(); // Valide aussi l'arrêt des sous-titres et la libération du mutex.
                }
                step++;
            }
            catch (Exception ex)
            {
                timer.Stop();
                CrashLog.Write("CrashChecks", ex);
                File.WriteAllText(Path.Combine(DiagnosticFlags.DirectoryPath, "crash-checks-error.txt"), ex.ToString());
                System.Windows.Application.Current.Shutdown(1);
            }
        };
        timer.Start();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CreateUnobservedFault()
    {
        var task = Task.Run(() => throw new InvalidOperationException("diagnostic/unobserved-task"));
        while (!task.IsCompleted) Thread.Sleep(1);
    }
}
