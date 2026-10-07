using OrbitWeave.Data;
using OrbitWeave.Ui;

namespace OrbitWeave;

// Données de la petite fenêtre : exporter, importer, réinitialiser. Les réinitialisations et l'import
// terminent d'abord le mode Modifier et écrivent ce qui attend, pour que rien ne soit perdu ni réécrit après coup.
public partial class MainWindow
{
    private void FinishPendingEdits()
    {
        if (_wheelEditMode) EndWheelEditing();
        _wheelEditor?.SavePending();
    }

    internal async Task ExportDataAsync()
    {
        FinishPendingEdits();
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exporter OrbitWeave",
            FileName = $"OrbitWeave-{DateTime.Now:yyyy-MM-dd}{DataBundle.Extension}",
            Filter = $"Sauvegarde OrbitWeave (*{DataBundle.Extension})|*{DataBundle.Extension}",
            DefaultExt = DataBundle.Extension
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            DataBundle.Export(ActionStore.DirectoryPath, dialog.FileName);
            await Dialogs.InfoAsync(null, "Exporté", $"Boutons, interface et réglages enregistrés dans :\n{dialog.FileName}");
        }
        catch (Exception ex)
        {
            CrashLog.Write("ExportData", ex);
            await Dialogs.InfoAsync(null, "Export impossible", ex.Message);
        }
    }

    internal async Task ImportDataAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Importer OrbitWeave",
            Filter = $"Sauvegarde OrbitWeave (*{DataBundle.Extension})|*{DataBundle.Extension}|Tous les fichiers (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return;
        await ImportDataFromAsync(dialog.FileName);
    }

    internal async Task ImportDataFromAsync(string path)
    {
        // Un fichier illisible est signalé tout de suite, sans demander de confirmer un import qui ne se fera pas.
        try { DataBundle.Check(path); }
        catch (DataBundleException ex) { await Dialogs.InfoAsync(null, "Import impossible", ex.Message); return; }
        if (!await Dialogs.ConfirmAsync(null, "Importer",
            "Les boutons, l'interface et les réglages actuels seront remplacés par ceux du fichier. Une copie de l'existant est faite avant, dans le dossier Sauvegardes.", "Importer"))
            return;
        FinishPendingEdits();
        string backup;
        try { backup = DataBundle.Import(ActionStore.DirectoryPath, path); }
        catch (DataBundleException ex) { await Dialogs.InfoAsync(null, "Import impossible", ex.Message); return; }
        catch (Exception ex)
        {
            CrashLog.Write("ImportData", ex);
            await Dialogs.InfoAsync(null, "Import impossible", ex.Message);
            return;
        }
        var settings = HotkeySettings.Load();
        UiRuntime.SetProfile(UiProfileStore.Default.Load(settings.InterfaceProfile, out _));
        ApplyEditorChanges();
        RedrawNow();
        await Dialogs.InfoAsync(null, "Importé", $"C'est fait. L'ancienne version est gardée dans :\n{backup}");
    }

    internal async Task ResetAllAsync()
    {
        if (!await Dialogs.ConfirmAsync(null, "Réinitialiser tout",
            "L'interface d'origine et les réglages par défaut (thème, comportement, raccourci, curseurs) seront rétablis. Vos boutons sont gardés, vos profils d'interface restent enregistrés.", "Réinitialiser"))
            return;
        FinishPendingEdits();
        var defaults = HotkeySettings.Defaults();
        ChangeSettings(settings =>
        {
            settings.ResetWheel(defaults);
            settings.ResetKeyboardAndWindow(defaults);
            settings.InterfaceProfile = UiProfileStore.OriginName;
        });
        UiRuntime.SetProfile(new UiProfile());
        RedrawNow();
    }

    // Tout a pu changer d'un coup (boutons, interface, thème) : la roue est reconstruite sans animation.
    private void RedrawNow()
    {
        _instantMotion = true;
        try { RenderOrbit(); }
        finally { _instantMotion = _wheelEditMode; }
        _quickPanel?.Refresh();
    }
}
