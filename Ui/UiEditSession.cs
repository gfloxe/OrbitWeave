using System.Text.Json;
using OrbitWeave.Actions;

namespace OrbitWeave.Ui;

// Modifications d'un profil d'interface, avec historique. Ne dépend pas de WPF.
public sealed class UiEditSession(UiProfileStore store, UiProfile profile)
{
    public const string NewProfileName = "Mon interface";
    public UiProfile Profile { get; private set; } = profile;
    public EditHistory History { get; } = new();
    // Quelque chose reste à enregistrer : le profil, les actions, ou les deux.
    public bool IsDirty => _profileDirty || ActionsDirty;
    public bool ActionsDirty { get; private set; }
    public bool LastChangeIsLayout { get; private set; }
    public bool LastChangeIsActions { get; private set; }
    public bool LastChangeHasActions { get; private set; }
    // Actions (contenu de actions.json) : même historique que la disposition, pour un seul Ctrl+Z.
    public List<ActionItem> Actions { get; private set; } = [];
    public event Action? Changed;
    public event Action? ActionsSaved;
    // Aucun nouveau profil ni changement de profil actif implicite.
    public event Action? OriginEditRejected;
    private bool _profileDirty;
    private string? _actionsPath;

    public string? ValueOf(string id, string property) =>
        Profile.Elements.TryGetValue(id, out var values) && values.TryGetValue(property, out var value) ? value : null;

    public string? StateOf(string id, string state, string property) =>
        Profile.States.TryGetValue(id, out var states) && states.TryGetValue(state, out var values)
            ? values.GetValueOrDefault(property) : null;

    public string? AnimationOf(string id, string property) =>
        Profile.Animations.TryGetValue(id, out var values) ? values.GetValueOrDefault(property) : null;

    public string? ResolutionOf(string band, string id, string property) =>
        Profile.Resolutions.TryGetValue(band, out var elements) && elements.TryGetValue(id, out var values)
            ? values.GetValueOrDefault(property) : null;

    public UiCustomElement? CustomOf(string id) => Profile.CustomElements.GetValueOrDefault(id);

    public string? TokenOf(string key, string? theme) =>
        theme is null
            ? Profile.Tokens.GetValueOrDefault(key)
            : Profile.Themes.TryGetValue(theme, out var values) ? values.GetValueOrDefault(key) : null;

    public bool IsModified(string id) => Profile.Elements.ContainsKey(id) || Profile.States.ContainsKey(id) ||
        Profile.Animations.ContainsKey(id) || Profile.CustomElements.ContainsKey(id) ||
        Profile.Resolutions.Values.Any(elements => elements.ContainsKey(id)) ||
        id == "wheel.hub" && Profile.Layout.Hub is not null || Profile.Layout.Attachments.ContainsKey(id) ||
        id.StartsWith("wheel.section:") && Profile.Layout.Sections.ContainsKey(id["wheel.section:".Length..]);

    public void SetLayout(Orbit.WheelLayoutData layout, string? gesture = null) => Record([
        new UiChange(UiChangeKind.Layout, "", "layout", "", JsonSerializer.Serialize(Profile.Layout),
            JsonSerializer.Serialize(layout))], gesture);

    // Chargement (ouverture de l'éditeur, sauvegarde des Réglages) : pas d'historique, rien à enregistrer.
    public void AttachActions(IEnumerable<ActionItem> actions, string path)
    {
        Actions = CloneActions(actions);
        _actionsPath = path;
        ActionsDirty = false;
        History.Clear();
    }

    public void SetActions(IEnumerable<ActionItem> actions, string? gesture = null) => Record([
        new UiChange(UiChangeKind.Actions, "", "actions", "", SerializeActions(Actions), SerializeActions(actions))], gesture);

    public void ClearHistory() => History.Clear();

    // Nouvelle section posée sur l'anneau : les actions et la disposition, en une seule étape.
    public void SetActionsAndLayout(IEnumerable<ActionItem> actions, Orbit.WheelLayoutData layout, string? gesture = null) => Record([
        new UiChange(UiChangeKind.Actions, "", "actions", "", SerializeActions(Actions), SerializeActions(actions)),
        new UiChange(UiChangeKind.Layout, "", "layout", "", JsonSerializer.Serialize(Profile.Layout), JsonSerializer.Serialize(layout))], gesture);

    // Renommer une section : les actions (déjà renommées) et tout ce que le profil range sous son nom
    // (position, pile, groupes, surcharges), en une seule étape.
    public void RenameSection(IEnumerable<ActionItem> actions, string oldName, string newName, string? gesture = null)
    {
        var renamed = Profile.Clone(Profile.Name);
        UiSectionRename.Apply(renamed, oldName, newName);
        Record([new UiChange(UiChangeKind.Actions, "", "actions", "", SerializeActions(Actions), SerializeActions(actions)),
            .. ProfileChanges(Profile, renamed)], gesture);
    }

    private static IEnumerable<UiChange> ProfileChanges(UiProfile before, UiProfile after)
    {
        static IEnumerable<(string Scope, string Key, string Property, string Value)> Values(Dictionary<string, Dictionary<string, string>> map, string scope = "") =>
            map.SelectMany(element => element.Value.Select(pair => (scope, element.Key, pair.Key, pair.Value)));
        static IEnumerable<UiChange> Diff(UiChangeKind kind, IEnumerable<(string Scope, string Key, string Property, string Value)> old,
            IEnumerable<(string Scope, string Key, string Property, string Value)> @new)
        {
            var a = old.ToDictionary(v => (v.Scope, v.Key, v.Property), v => v.Value);
            var b = @new.ToDictionary(v => (v.Scope, v.Key, v.Property), v => v.Value);
            // Suppressions d'abord : une clé qui change de nom libère l'ancienne avant d'écrire la nouvelle.
            foreach (var (key, value) in a.Where(pair => !b.ContainsKey(pair.Key)))
                yield return new UiChange(kind, key.Scope, key.Key, key.Property, value, null);
            foreach (var (key, value) in b.Where(pair => a.GetValueOrDefault(pair.Key) != pair.Value))
                yield return new UiChange(kind, key.Scope, key.Key, key.Property, a.GetValueOrDefault(key), value);
        }
        foreach (var change in Diff(UiChangeKind.Element, Values(before.Elements), Values(after.Elements))) yield return change;
        foreach (var change in Diff(UiChangeKind.Animation, Values(before.Animations), Values(after.Animations))) yield return change;
        foreach (var change in Diff(UiChangeKind.State,
            before.States.SelectMany(element => element.Value.SelectMany(state => state.Value.Select(pair => (state.Key, element.Key, pair.Key, pair.Value)))),
            after.States.SelectMany(element => element.Value.SelectMany(state => state.Value.Select(pair => (state.Key, element.Key, pair.Key, pair.Value)))))) yield return change;
        foreach (var change in Diff(UiChangeKind.Resolution, before.Resolutions.SelectMany(band => Values(band.Value, band.Key)),
            after.Resolutions.SelectMany(band => Values(band.Value, band.Key)))) yield return change;
        foreach (var change in Diff(UiChangeKind.Custom,
            before.CustomElements.Select(pair => ("", pair.Key, "", JsonSerializer.Serialize(pair.Value))),
            after.CustomElements.Select(pair => ("", pair.Key, "", JsonSerializer.Serialize(pair.Value))))) yield return change;
        yield return new UiChange(UiChangeKind.Layout, "", "layout", "", JsonSerializer.Serialize(before.Layout), JsonSerializer.Serialize(after.Layout));
    }

    private static string SerializeActions(IEnumerable<ActionItem> actions) => JsonSerializer.Serialize(actions, ActionJson.Options);

    private static List<ActionItem> CloneActions(IEnumerable<ActionItem> actions) =>
        JsonSerializer.Deserialize<List<ActionItem>>(SerializeActions(actions), ActionJson.Options) ?? [];

    public void SetSectionPosition(string name, Orbit.WheelPoint? point, string? gesture = null)
    {
        var layout = Profile.Layout.Clone();
        if (point is { } value) layout.Sections[name] = value; else layout.Sections.Remove(name);
        SetLayout(layout, gesture);
    }

    public void SetAttachment(string id, Orbit.WheelPoint? delta, string? gesture = null)
    {
        var layout = Profile.Layout.Clone();
        if (delta is { } value) layout.Attachments[id] = value; else layout.Attachments.Remove(id);
        SetLayout(layout, gesture);
    }

    public void ResetLayout() => SetLayout(new Orbit.WheelLayoutData());

    public void SetStack(string name, Orbit.WheelStackPlacement? placement, string? gesture = null)
    {
        var layout = Profile.Layout.Clone();
        if (placement is { } value) layout.Stacks[name] = value; else layout.Stacks.Remove(name);
        SetLayout(layout, gesture);
    }

    public void SetCustom(UiCustomElement element, string? gesture = null) => Record([
        new UiChange(UiChangeKind.Custom, "", element.Id, "",
            Profile.CustomElements.TryGetValue(element.Id, out var old) ? JsonSerializer.Serialize(old) : null,
            JsonSerializer.Serialize(element))], gesture);

    public IReadOnlyList<string> GroupMembers(string id)
    {
        if (!Profile.CustomElements.TryGetValue(id, out var item) || string.IsNullOrWhiteSpace(item.Group)) return [id];
        return Profile.CustomElements.Values.Where(other => other.Parent == item.Parent && other.Group == item.Group)
            .Select(other => other.Id).ToList();
    }

    public void MoveGroup(string id, double dx, double dy, string? gesture = null)
    {
        var changes = new List<UiChange>();
        var layout = Profile.Layout.Clone();
        var hasAttached = false;
        foreach (var member in GroupMembers(id))
        {
            if (CustomOf(member) is { AttachTo.Length: > 0 })
            {
                layout.Attachments[member] = layout.Attachments.GetValueOrDefault(member) + new Orbit.WheelPoint(dx, dy);
                hasAttached = true;
                continue;
            }
            foreach (var (property, delta) in new[] { ("OffsetX", dx), ("OffsetY", dy) })
            {
                if (delta == 0) continue;
                var old = ValueOf(member, property);
                var start = double.TryParse(old, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 0;
                var next = (start + delta).ToString(System.Globalization.CultureInfo.InvariantCulture);
                changes.Add(new UiChange(UiChangeKind.Element, "", member, property, old, next));
            }
        }
        if (hasAttached) changes.Add(new UiChange(UiChangeKind.Layout, "", "layout", "",
            JsonSerializer.Serialize(Profile.Layout), JsonSerializer.Serialize(layout)));
        Record(changes, gesture);
    }

    public void RemoveCustom(string id)
    {
        if (!Profile.CustomElements.TryGetValue(id, out var custom)) return;
        var changes = new List<UiChange> { new(UiChangeKind.Custom, "", id, "", JsonSerializer.Serialize(custom), null) };
        if (Profile.Elements.TryGetValue(id, out var values))
            changes.AddRange(values.Select(pair => new UiChange(UiChangeKind.Element, "", id, pair.Key, pair.Value, null)));
        if (Profile.States.TryGetValue(id, out var states))
            changes.AddRange(states.SelectMany(state => state.Value.Select(pair =>
                new UiChange(UiChangeKind.State, state.Key, id, pair.Key, pair.Value, null))));
        if (Profile.Animations.TryGetValue(id, out var animation))
            changes.AddRange(animation.Select(pair => new UiChange(UiChangeKind.Animation, "", id, pair.Key, pair.Value, null)));
        changes.AddRange(Profile.Resolutions.SelectMany(band => band.Value.TryGetValue(id, out var properties)
            ? properties.Select(pair => new UiChange(UiChangeKind.Resolution, band.Key, id, pair.Key, pair.Value, null))
            : []));
        Record(changes, null);
    }

    public void Set(string id, string property, string? value, string? gesture = null) =>
        Record([new UiChange(UiChangeKind.Element, "", id, property, ValueOf(id, property), Clean(value))], gesture);

    public void SetState(string id, string state, string property, string? value, string? gesture = null)
    {
        if (state is not ("Hover" or "Pressed" or "Disabled")) throw new ArgumentOutOfRangeException(nameof(state));
        Record([new UiChange(UiChangeKind.State, state, id, property, StateOf(id, state, property), Clean(value))], gesture);
    }

    public void SetAnimation(string id, string property, string? value, string? gesture = null)
    {
        if (property is not ("Type" or "DurationMs" or "DelayMs" or "Easing"))
            throw new ArgumentOutOfRangeException(nameof(property));
        Record([new UiChange(UiChangeKind.Animation, "", id, property, AnimationOf(id, property), Clean(value))], gesture);
    }

    public void SetResolution(string band, string id, string property, string? value, string? gesture = null)
    {
        if (band is not (UiResolutionBands.Small or UiResolutionBands.Medium or
            UiResolutionBands.Large or UiResolutionBands.ExtraLarge))
            throw new ArgumentOutOfRangeException(nameof(band));
        Record([new UiChange(UiChangeKind.Resolution, band, id, property,
            ResolutionOf(band, id, property), Clean(value))], gesture);
    }

    public void SetToken(string key, string? value, string? theme, string? gesture = null) =>
        Record([new UiChange(UiChangeKind.Token, theme ?? "", key, "", TokenOf(key, theme), Clean(value))], gesture);

    public void ResetElement(string id)
    {
        var changes = Profile.Elements.TryGetValue(id, out var values)
            ? values.Select(pair => new UiChange(UiChangeKind.Element, "", id, pair.Key, pair.Value, null)).ToList()
            : [];
        if (Profile.States.TryGetValue(id, out var states))
            changes.AddRange(states.SelectMany(state => state.Value.Select(pair =>
                new UiChange(UiChangeKind.State, state.Key, id, pair.Key, pair.Value, null))));
        if (Profile.Animations.TryGetValue(id, out var animation))
            changes.AddRange(animation.Select(pair => new UiChange(UiChangeKind.Animation, "", id, pair.Key, pair.Value, null)));
        changes.AddRange(Profile.Resolutions.SelectMany(band => band.Value.TryGetValue(id, out var properties)
            ? properties.Select(pair => new UiChange(UiChangeKind.Resolution, band.Key, id, pair.Key, pair.Value, null))
            : []));
        var layout = Profile.Layout.Clone();
        if (id == "wheel.hub") layout.Hub = null;
        if (id.StartsWith("wheel.section:")) layout.Sections.Remove(id["wheel.section:".Length..]);
        layout.Attachments.Remove(id);
        if (JsonSerializer.Serialize(layout) != JsonSerializer.Serialize(Profile.Layout))
            changes.Add(new UiChange(UiChangeKind.Layout, "", "layout", "", JsonSerializer.Serialize(Profile.Layout), JsonSerializer.Serialize(layout)));
        Record(changes, null);
    }

    public void ResetAll()
    {
        var changes = Profile.Elements.SelectMany(element => element.Value.Select(pair =>
                new UiChange(UiChangeKind.Element, "", element.Key, pair.Key, pair.Value, null)))
            .Concat(Profile.Tokens.Select(pair => new UiChange(UiChangeKind.Token, "", pair.Key, "", pair.Value, null)))
            .Concat(Profile.Themes.SelectMany(theme => theme.Value.Select(pair =>
                new UiChange(UiChangeKind.Token, theme.Key, pair.Key, "", pair.Value, null))))
            .Concat(Profile.States.SelectMany(element => element.Value.SelectMany(state => state.Value.Select(pair =>
                new UiChange(UiChangeKind.State, state.Key, element.Key, pair.Key, pair.Value, null)))))
            .Concat(Profile.Animations.SelectMany(element => element.Value.Select(pair =>
                new UiChange(UiChangeKind.Animation, "", element.Key, pair.Key, pair.Value, null))))
            .Concat(Profile.CustomElements.Select(pair => new UiChange(UiChangeKind.Custom, "", pair.Key, "",
                JsonSerializer.Serialize(pair.Value), null)))
            .Concat(Profile.Resolutions.SelectMany(band => band.Value.SelectMany(element => element.Value.Select(pair =>
                new UiChange(UiChangeKind.Resolution, band.Key, element.Key, pair.Key, pair.Value, null)))))
            .ToList();
        if (!Profile.Layout.IsEmpty) changes.Add(new UiChange(UiChangeKind.Layout, "", "layout", "",
            JsonSerializer.Serialize(Profile.Layout), JsonSerializer.Serialize(new Orbit.WheelLayoutData())));
        Record(changes, null);
    }

    public bool Undo()
    {
        if (History.Undo() is not { } edit) return false;
        foreach (var change in edit.Changes.Reverse()) Write(change, change.Old);
        Touch(edit.Changes);
        return true;
    }

    public bool Redo()
    {
        if (History.Redo() is not { } edit) return false;
        foreach (var change in edit.Changes) Write(change, change.New);
        Touch(edit.Changes);
        return true;
    }

    // Changement de profil : l'historique repart de zéro.
    public void Load(UiProfile profile)
    {
        Profile = profile;
        History.Clear();
        _profileDirty = false;
        LastChangeIsLayout = false;
        LastChangeIsActions = false;
        LastChangeHasActions = false;
        Changed?.Invoke();
    }

    public void Save()
    {
        if (_profileDirty && Profile.Name != UiProfileStore.OriginName) store.Save(Profile);
        _profileDirty = false;
        if (ActionsDirty && _actionsPath is { } path)
        {
            ActionStore.Save(Actions, path);
            ActionsDirty = false;
            ActionsSaved?.Invoke();
        }
    }

    private void Record(List<UiChange> changes, string? gesture)
    {
        changes = changes.Where(change => change.Old != change.New).ToList();
        if (changes.Count == 0) return;
        // « Origine » est en lecture seule pour l'interface, pas pour les actions.
        if (Profile.Name == UiProfileStore.OriginName && changes.Any(change => change.Kind != UiChangeKind.Actions))
        {
            OriginEditRejected?.Invoke();
            return;
        }
        foreach (var change in changes) Write(change, change.New);
        History.Push(new UiEdit(changes, gesture));
        Touch(changes);
    }

    private void Touch(IReadOnlyList<UiChange> changes)
    {
        if (changes.Any(change => change.Kind != UiChangeKind.Actions)) _profileDirty = true;
        if (changes.Any(change => change.Kind == UiChangeKind.Actions)) ActionsDirty = true;
        LastChangeIsLayout = changes.All(change => change.Kind == UiChangeKind.Layout);
        LastChangeIsActions = changes.All(change => change.Kind == UiChangeKind.Actions);
        LastChangeHasActions = changes.Any(change => change.Kind == UiChangeKind.Actions);
        Changed?.Invoke();
    }

    private void Write(UiChange change, string? value)
    {
        if (change.Kind == UiChangeKind.Actions)
        {
            Actions = value is null ? [] : JsonSerializer.Deserialize<List<ActionItem>>(value, ActionJson.Options) ?? [];
            return;
        }
        if (change.Kind == UiChangeKind.Layout)
        {
            Profile.Layout = value is null ? new() : JsonSerializer.Deserialize<Orbit.WheelLayoutData>(value)!.Clone();
            return;
        }
        if (change.Kind == UiChangeKind.Custom)
        {
            if (value is null) Profile.CustomElements.Remove(change.Key);
            else Profile.CustomElements[change.Key] = JsonSerializer.Deserialize<UiCustomElement>(value)!;
            return;
        }
        var target = change.Kind switch
        {
            UiChangeKind.Element => Slot(Profile.Elements, change.Key),
            UiChangeKind.State => StateSlot(change.Key, change.Scope),
            UiChangeKind.Animation => Slot(Profile.Animations, change.Key),
            UiChangeKind.Resolution => ResolutionSlot(change.Scope, change.Key),
            _ => change.Scope.Length == 0 ? Profile.Tokens : Slot(Profile.Themes, change.Scope)
        };
        var key = change.Kind == UiChangeKind.Token ? change.Key : change.Property;
        if (value is null) target.Remove(key); else target[key] = value;
        // Un élément ou un thème sans valeur disparaît du profil.
        if (change.Kind == UiChangeKind.Element && target.Count == 0) Profile.Elements.Remove(change.Key);
        if (change.Kind == UiChangeKind.Token && change.Scope.Length > 0 && target.Count == 0) Profile.Themes.Remove(change.Scope);
        if (change.Kind == UiChangeKind.State && target.Count == 0)
        {
            Profile.States[change.Key].Remove(change.Scope);
            if (Profile.States[change.Key].Count == 0) Profile.States.Remove(change.Key);
        }
        if (change.Kind == UiChangeKind.Animation && target.Count == 0) Profile.Animations.Remove(change.Key);
        if (change.Kind == UiChangeKind.Resolution && target.Count == 0)
        {
            Profile.Resolutions[change.Scope].Remove(change.Key);
            if (Profile.Resolutions[change.Scope].Count == 0) Profile.Resolutions.Remove(change.Scope);
        }
    }

    private Dictionary<string, string> StateSlot(string id, string state)
    {
        if (!Profile.States.TryGetValue(id, out var states)) Profile.States[id] = states = new();
        if (!states.TryGetValue(state, out var values)) states[state] = values = new();
        return values;
    }

    private Dictionary<string, string> ResolutionSlot(string band, string id)
    {
        if (!Profile.Resolutions.TryGetValue(band, out var elements)) Profile.Resolutions[band] = elements = new();
        return Slot(elements, id);
    }

    private static Dictionary<string, string> Slot(Dictionary<string, Dictionary<string, string>> map, string key)
    {
        if (!map.TryGetValue(key, out var values)) map[key] = values = new();
        return values;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
