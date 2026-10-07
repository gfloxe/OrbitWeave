using System.Windows;
using System.Windows.Media;
using OrbitWeave.Ui.Editor;

namespace OrbitWeave;

public partial class MainWindow
{
    private EditBanner? _editBanner;

    // Boutons du bandeau grisés quand il n'y a rien à annuler ou à rétablir.
    private void UpdateEditingCommands() => _editBanner?.SetCommands(_wheelEditor?.CanUndo == true, _wheelEditor?.CanRedo == true);

    private void EndWheelEditing()
    {
        _wheelEditor?.Stop();
        WheelEditMode = false;
    }

    // Bandeau fixé en haut au milieu de l'écran de la roue, visible tant que dure le mode Modifier.
    private void UpdateEditingBanner()
    {
        if (!_wheelEditMode || !IsVisible)
        {
            _editBanner?.Hide();
            return;
        }
        _editBanner ??= new EditBanner(() => _wheelEditor?.Undo(), () => _wheelEditor?.Redo(), EndWheelEditing,
            e => { if (_wheelEditor is { } editor) editor.HandleKey(e); else if (e.Key == System.Windows.Input.Key.Escape) EndWheelEditing(); });
        // Déjà affiché : sa place ne change pas. Le replacer forçait une mise en page complète (15 à 25 ms) à chaque branche ouverte.
        if (!_editBanner.IsVisible) _editBanner.ShowOn(this);
        UpdateEditingCommands();
    }
}
