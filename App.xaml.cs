using System.Windows;

namespace OrbitWeave;

public partial class App : System.Windows.Application
{
    private Mutex? _instanceMutex;
    private bool _ownsInstanceMutex;
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);

    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            CrashLog.Write("AppDomain.UnhandledException", args.ExceptionObject, args.IsTerminating);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            CrashLog.Write("TaskScheduler.UnobservedTaskException", args.Exception);
            args.SetObserved();
        };
        DispatcherUnhandledException += (_, args) =>
        {
            CrashLog.Write("DispatcherUnhandledException", args.Exception);
            try
            {
                Directory.CreateDirectory(DiagnosticFlags.DirectoryPath);
                File.AppendAllText(Path.Combine(DiagnosticFlags.DirectoryPath, "errors.log"), args.Exception + Environment.NewLine);
            }
            catch { }
            args.Handled = true;
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        if (!LaunchPolicy.CanLaunch(Environment.ProcessPath, DiagnosticFlags.Enabled, DiagnosticFlags.DataDirectory))
        {
            if (!DiagnosticFlags.Enabled)
                System.Windows.MessageBox.Show("Cette copie est réservée aux tests. Lancez bin\\Release\\net10.0-windows\\OrbitWeave.exe. Les copies de test exigent --data-dir et un mode de diagnostic.", "OrbitWeave · Copie de test");
            Shutdown(2);
            return;
        }
        base.OnStartup(e);
        // Les lancements de diagnostic ont leur propre verrou : ils tournent à côté de l'instance normale.
        var mutexName = DiagnosticFlags.Enabled ? @"Local\OrbitWeave.Diagnostics" : @"Local\OrbitWeave.DesktopWidget";
        _instanceMutex = new Mutex(true, mutexName, out var isFirstInstance);
        _ownsInstanceMutex = isFirstInstance;
        if (!isFirstInstance)
        {
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown();
            return;
        }
        try { ActionStore.EnsureMigrated(); }
        catch (Exception ex) { CrashLog.Write("Startup.EnsureMigrated", ex); }
        Ui.UiRuntime.Install(Resources);
        // Les lancements de diagnostic sont arrêtés de force : ils ne comptent pas comme des démarrages ratés.
        Ui.SafeMode.Active = Ui.SafeMode.ShouldStart(e.Args, (GetAsyncKeyState(0x10) & 0x8000) != 0, Ui.SafeMode.Folder);
        if (!DiagnosticFlags.Enabled) Ui.SafeMode.MarkStarting(Ui.SafeMode.Folder);
        var interfaceProfile = HotkeySettings.Load().InterfaceProfile;
        if (Ui.SafeMode.Active)
        {
            Ui.SafeMode.StartupMessage = "OrbitWeave a démarré en mode de secours : l'interface d'origine est utilisée. Votre profil d'interface n'a pas été modifié.";
            Ui.UiRuntime.SetProfile(new Ui.UiProfile());
        }
        else
        {
            // Une erreur de lecture ne doit jamais laisser OrbitWeave lancé sans aucune fenêtre.
            try
            {
                Ui.UiRuntime.SetProfile(Ui.UiProfileStore.Default.Load(interfaceProfile, out var problem));
                Ui.SafeMode.StartupMessage = problem;
            }
            catch (Exception ex)
            {
                CrashLog.Write("Startup.LoadProfile", ex);
                Ui.UiRuntime.SetProfile(new Ui.UiProfile());
                Ui.SafeMode.StartupMessage = "Votre profil d'interface n'a pas pu être lu : l'interface d'origine est utilisée. Le profil n'a pas été modifié.";
            }
        }
        if (DiagnosticFlags.Enabled) Directory.CreateDirectory(DiagnosticFlags.DirectoryPath);
        if (Ui.SafeMode.Active || Ui.SafeMode.StartupMessage is not null)
            CrashLog.Write("ProfileFallback", $"requested={interfaceProfile}; effective={Ui.UiRuntime.Profile.Name}; safeMode={Ui.SafeMode.Active}; reason={Ui.SafeMode.StartupMessage}");
        if (DiagnosticFlags.MeasureNoShadows)
        {
            Resources["NodeShadowEffect"] = null;
            Resources["LabelShadowEffect"] = null;
            Resources["CardShadowEffect"] = null;
        }
        MainWindow = new MainWindow();
        MainWindow.Show();
        // Les lancements de diagnostic ne vont pas sur Internet, sauf pour essayer la mise à jour.
        if (!DiagnosticFlags.Enabled || DiagnosticFlags.PretendVersion is not null) Updates.Updater.Start();
        if (DiagnosticFlags.DataDirectory is not null && e.Args.Contains("--preview-crash-checks"))
            ((MainWindow)MainWindow).StartCrashChecks();
        if (DiagnosticFlags.DataDirectory is not null && e.Args.Contains("--preview-crash-domain"))
        {
            SetErrorMode(0x0002); // Pas de boîte WER pour ce processus de test volontairement fatal.
            new Thread(() => { Thread.Sleep(200); throw new InvalidOperationException("diagnostic/fatal-thread", new ArgumentException("diagnostic/inner")); }) { IsBackground = true }.Start();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Une fermeture normale n'est pas un démarrage raté.
        if (_ownsInstanceMutex && !DiagnosticFlags.Enabled)
            try { Ui.SafeMode.MarkStarted(Ui.SafeMode.Folder); } catch (Exception ex) { CrashLog.Write("OnExit.MarkStarted", ex); }
        try { if (_ownsInstanceMutex) _instanceMutex?.ReleaseMutex(); }
        catch (Exception ex) { CrashLog.Write("OnExit.ReleaseMutex", ex); }
        finally
        {
            _ownsInstanceMutex = false;
            try { _instanceMutex?.Dispose(); } catch (Exception ex) { CrashLog.Write("OnExit.DisposeMutex", ex); }
            _instanceMutex = null;
        }
        try { base.OnExit(e); } catch (Exception ex) { CrashLog.Write("OnExit.Base", ex); }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern uint SetErrorMode(uint mode);
}
