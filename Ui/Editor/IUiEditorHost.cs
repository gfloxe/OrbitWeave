using System.Windows;

namespace OrbitWeave.Ui.Editor;

// Ce que le mode Modifier (WheelEditor) demande à la roue.
public interface IUiEditorHost
{
    void SetInterfaceProfile(string name);
    // Mode édition de la roue : premier plan, un clic sélectionne au lieu d'agir.
    bool WheelEditMode { get; set; }
    bool WheelGestureActive { get; }
    Rect? WheelSelectionScreenBounds(string? id);
    event Action<string>? WheelElementPicked;
    // Sélection affichée dans la roue (contours) ; null pour effacer.
    void ShowWheelSelection(string? id);
    // Déplacement direct dans la roue : décalage proposé pendant le glisser, final au relâchement.
    event Action<string, Vector, bool>? WheelElementDragged;
    // Manipulation directe des poignées : largeur, hauteur ou rotation, valeur absolue.
    event Action<string, string, double, bool>? WheelElementTransformed;
    // Touche pressée dans la roue en mode édition (flèches, Ctrl+Z…), traitée comme dans l'éditeur.
    event Action<System.Windows.Input.KeyEventArgs>? WheelKeyDown;
    Orbit.WheelPoint? WheelPosition(string id);
    IReadOnlyList<string> WheelSectionNames { get; }
    (string Section, Orbit.WheelStackPlacement Placement)? CardStack(string id);
    Orbit.WheelPoint LimitSectionPosition(Orbit.WheelPoint point);
    Vector LimitWheelTranslation(string id, Vector delta);
    void ApplyWheelLayout();
    void CompleteWheelLayoutMove();
    // Nouvelle liste d'actions (modifiée ou annulée dans l'éditeur) : la roue se redessine avec.
    void ApplyActions(IReadOnlyList<OrbitWeave.ActionItem> actions);
    // ▶ Tester dans la bulle d'un bouton : lance l'action comme un clic sur la roue.
    Task TestAction(OrbitWeave.ActionItem action);
    // Glisser d'une carte d'un groupe (déplacer, détacher, recoller) : disposition proposée, ou null.
    Orbit.WheelLayoutData? CardDragLayout(string id, Vector delta, bool final, Orbit.WheelLayoutData start);
    // Glisser d'une carte : autre section sous le curseur (entourée), où la carte sera déplacée au lâcher ; sinon null.
    string? CardDropSection(string id, bool final);
    // Disposition proposée par un geste de la roue (✂ couper une pile).
    event Action<Orbit.WheelLayoutData>? WheelLayoutProposed;
}
