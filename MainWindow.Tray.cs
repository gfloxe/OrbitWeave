using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OrbitWeave;

// Icône dans les icônes cachées : clic gauche = petite fenêtre (thèmes…), clic droit = Quitter.
// La NotifyIcon de Windows Forms se réinscrit d'elle-même quand l'Explorateur redémarre (TaskbarCreated).
public partial class MainWindow
{
    private NotifyIcon? _trayIcon;
    private QuickPanel? _quickPanel;
    private FrameMeter? _settingsMeter;

    private void StartTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Quitter OrbitWeave", null, (_, _) => System.Windows.Application.Current.Shutdown());
        _trayIcon = new NotifyIcon { Icon = TrayImage(), Text = "OrbitWeave", ContextMenuStrip = menu, Visible = true };
        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            if (_quickPanel is { IsVisible: true }) { _quickPanel.HidePanel(); return; }
            if (_quickPanel?.JustHidden == true) return;
            ShowQuickPanel(Control.MousePosition);
        };
        System.Windows.Application.Current.Exit += (_, _) => StopTrayIcon();
        Closed += (_, _) => StopTrayIcon();
    }

    private void StopTrayIcon()
    {
        if (_trayIcon is null) return;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _trayIcon = null;
        _quickPanel?.CloseForGood();
        _quickPanel = null;
    }

    // Depuis l'icône (près d'elle) ou le menu du rond du milieu (coin bas droit de l'écran de la roue).
    internal void ShowQuickPanel(System.Drawing.Point? anchor = null)
    {
        _quickPanel ??= new QuickPanel(() => _settings, ChangeSettings,
        [
            ("Exporter…", ExportDataAsync),
            ("Importer…", ImportDataAsync),
            ("Réinitialiser l'interface", ResetInterfaceAsync),
            ("Réinitialiser tout", ResetAllAsync)
        ], new IconLibrary(() => _actions, WriteActions, ActionStore.DirectoryPath));
        var screen = Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle).WorkingArea;
        _quickPanel.ShowNear(anchor ?? new System.Drawing.Point(screen.Right - 1, screen.Bottom - 1));
    }

    // Réglage changé hors de la grande fenêtre : enregistré, appliqué à la roue, et la grande fenêtre suit.
    private void ChangeSettings(Action<HotkeySettings> change)
    {
        // Ce qui attend d'être écrit (mode Modifier) l'est d'abord : la relecture ne doit rien perdre.
        // --trace-frames : fluidité de la roue pendant le réglage (perf.log, phase « reglages/curseur »).
        if (DiagnosticFlags.TraceFrames && _settingsMeter is null)
        {
            _settingsMeter = new FrameMeter(true);
            _settingsMeter.Begin("reglages/curseur");
            var stop = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            stop.Tick += (_, _) => { stop.Stop(); _settingsMeter?.End(); _settingsMeter = null; };
            stop.Start();
        }
        _wheelEditor?.SavePending();
        var settings = HotkeySettings.Load();
        change(settings);
        settings.Save();
        ApplyEditorChanges();
        _wheelEditor?.ApplyTheme(_settings.Theme != "Clair");
    }

    // ◉ rose OrbitWeave, lisible sur une barre des tâches claire ou sombre.
    private static System.Drawing.Icon TrayImage()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var pink = System.Drawing.Color.FromArgb(0xFF, 0x3E, 0xA5);
            using var outline = new Pen(System.Drawing.Color.FromArgb(160, 20, 20, 20), 6);
            using var ring = new Pen(pink, 4);
            g.DrawEllipse(outline, 4, 4, 24, 24);
            g.DrawEllipse(ring, 4, 4, 24, 24);
            using var dot = new SolidBrush(pink);
            g.FillEllipse(dot, 11, 11, 10, 10);
        }
        return System.Drawing.Icon.FromHandle(bitmap.GetHicon());
    }
}
