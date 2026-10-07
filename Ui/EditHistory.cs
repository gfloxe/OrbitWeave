namespace OrbitWeave.Ui;

public enum UiChangeKind { Element, Token, State, Animation, Custom, Resolution, Layout, Actions }

// Une valeur modifiée : surcharge d'élément (Key = id, Property = propriété) ou jeton (Key = jeton,
// Scope = thème, vide pour tous les thèmes). Null = pas de valeur.
public sealed record UiChange(UiChangeKind Kind, string Scope, string Key, string Property, string? Old, string? New)
{
    public bool SameTarget(UiChange other) =>
        Kind == other.Kind && Scope == other.Scope && Key == other.Key && Property == other.Property;
}

// Une entrée d'historique : un geste de l'utilisateur, éventuellement plusieurs valeurs.
public sealed record UiEdit(IReadOnlyList<UiChange> Changes, string? Gesture);

public sealed class EditHistory
{
    public const int Capacity = 500;
    private readonly LinkedList<UiEdit> _undo = new();
    private readonly Stack<UiEdit> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int Count => _undo.Count;
    // Geste de la dernière entrée (null : pas de geste, ou historique vide).
    public string? LastGesture => _undo.Last?.Value.Gesture;

    // Les modifications d'un même geste (un glisser, une saisie au clavier) ne forment qu'une entrée.
    public void Push(UiEdit edit)
    {
        _redo.Clear();
        if (edit.Gesture is not null && _undo.Last?.Value is { } top && top.Gesture == edit.Gesture)
        {
            var merged = top.Changes.ToList();
            foreach (var change in edit.Changes)
            {
                var index = merged.FindIndex(existing => existing.SameTarget(change));
                if (index >= 0) merged[index] = merged[index] with { New = change.New };
                else merged.Add(change);
            }
            _undo.RemoveLast();
            _undo.AddLast(new UiEdit(merged, top.Gesture));
            return;
        }
        _undo.AddLast(edit);
        while (_undo.Count > Capacity) _undo.RemoveFirst();
    }

    public UiEdit? Undo()
    {
        if (_undo.Last?.Value is not { } edit) return null;
        _undo.RemoveLast();
        _redo.Push(edit);
        return edit;
    }

    public UiEdit? Redo()
    {
        if (!_redo.TryPop(out var edit)) return null;
        _undo.AddLast(edit);
        return edit;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
