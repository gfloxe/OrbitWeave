using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace OrbitWeave.Ui.Editor;

// Mode Modifier de la roue, sans fenêtre : sélection, glisser, bulles, historique (Annuler / Rétablir)
// et enregistrement. Créé à l'entrée dans le mode, arrêté à sa sortie (ce qui est fait est enregistré).
public sealed class WheelEditor
{
    private readonly IUiEditorHost _host;
    private readonly UiProfileStore _store = UiProfileStore.Default;
    private readonly UiEditSession _session;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly DispatcherTimer _wheelPickTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly ResourceDictionary _look = new();
    private string? _pendingWheelPick;
    private string? _selectedId;
    private bool _refreshPending;
    private bool _stopped;
    private int _interacting;
    private int _gesture;
    private DateTime _lastStep;
    private string? _stepTarget;
    private Vector? _dragStart;
    private Vector _acceptedScreenDelta;
    private Vector _dragGroupDelta;
    private Orbit.WheelLayoutData? _hubDragLayout;
    private Orbit.WheelLayoutData? _cardDragLayout;
    private Dictionary<string, Orbit.WheelPoint>? _hubDragSections;
    private ActionBubble? _bubble;
    private SectionBubble? _sectionBubble;

    // Annuler / Rétablir possibles ont changé (bandeau du mode Modifier).
    public event Action? CommandsChanged;
    // Le mode est terminé (Échap, Terminer…) : l'appelant oublie cet éditeur.
    public event Action? Stopped;
    // Les actions de la session ont été écrites dans actions.json.
    public event Action? ActionsSaved;

    public bool CanUndo => _session.History.CanUndo;
    public bool CanRedo => _session.History.CanRedo;
    public string? SelectedElementId => _selectedId;
    public string ProfileName => _session.Profile.Name;

    public WheelEditor(IUiEditorHost host, bool dark)
    {
        _host = host;
        // L'interface d'origine est en lecture seule : on modifie une copie, qui devient l'interface active.
        if (UiRuntime.Profile.Name == UiProfileStore.OriginName)
        {
            var copy = UiRuntime.Profile.Clone(_store.UniqueName(UiEditSession.NewProfileName));
            _store.Save(copy);
            UiRuntime.SetProfile(copy, notify: false);
            _host.SetInterfaceProfile(copy.Name);
        }
        _session = new UiEditSession(_store, UiRuntime.Profile);
        _session.AttachActions(ActionStore.Load(), ActionStore.FilePath);
        _session.ActionsSaved += () => ActionsSaved?.Invoke();
        _session.Changed += OnSessionChanged;
        _saveTimer.Tick += (_, _) => { if (_interacting > 0) return; _saveTimer.Stop(); Save(); };
        _wheelPickTimer.Tick += (_, _) =>
        {
            if (_host.WheelGestureActive || _interacting > 0) return;
            _wheelPickTimer.Stop();
            if (_pendingWheelPick is { } id) { _pendingWheelPick = null; Select(id); }
        };
        ApplyTheme(dark);
        _host.WheelElementPicked += OnWheelPicked;
        _host.WheelElementDragged += OnWheelDragged;
        _host.WheelLayoutProposed += OnWheelLayoutProposed;
        _host.WheelElementTransformed += OnWheelTransformed;
        _host.WheelKeyDown += OnWheelKeyDown;
        _host.WheelEditMode = true;
        ShowSelection();
    }

    // Couleurs des bulles : claires sur le thème Clair, sombres sinon.
    public void ApplyTheme(bool dark)
    {
        _look["SettingsCardBrush"] = new SolidColorBrush(dark ? Color.FromRgb(48, 49, 51) : Colors.White);
        _look["SettingsBorderBrush"] = new SolidColorBrush(dark ? Color.FromRgb(58, 59, 61) : Color.FromRgb(234, 234, 232));
        _look["SettingsTextBrush"] = new SolidColorBrush(dark ? Colors.White : Color.FromRgb(32, 32, 32));
        _look["SettingsMutedBrush"] = new SolidColorBrush(dark ? Color.FromRgb(177, 177, 177) : Color.FromRgb(116, 116, 116));
        _look["SettingsHoverBrush"] = new SolidColorBrush(dark ? Color.FromRgb(63, 64, 66) : Color.FromRgb(224, 224, 220));
        if (_bubble is not null) ShareBubbleLook(_bubble);
        if (_sectionBubble is not null) ShareBubbleLook(_sectionBubble);
    }

    // Fin du mode : ce qui est fait est enregistré, l'historique est fermé (plus d'Annuler).
    public void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        _dragStart = null; _acceptedScreenDelta = _dragGroupDelta = new Vector();
        _hubDragLayout = null; _hubDragSections = null; _cardDragLayout = null; _interacting = 0; _gesture++;
        _wheelPickTimer.Stop();
        _saveTimer.Stop();
        Save();
        _bubble?.CloseForGood(); _bubble = null;
        _sectionBubble?.CloseForGood(); _sectionBubble = null;
        _host.WheelElementPicked -= OnWheelPicked;
        _host.WheelElementDragged -= OnWheelDragged;
        _host.WheelLayoutProposed -= OnWheelLayoutProposed;
        _host.WheelElementTransformed -= OnWheelTransformed;
        _host.WheelKeyDown -= OnWheelKeyDown;
        _host.ShowWheelSelection(null);
        _host.WheelEditMode = false;
        Stopped?.Invoke();
    }

    public void SavePending() { _saveTimer.Stop(); Save(); }
    public void Undo() => FollowSectionRename(() => _session.Undo());
    public void Redo() => FollowSectionRename(() => _session.Redo());
    public void Select(string id) { _selectedId = id; ShowSelection(); UpdateCommands(); }

    // Fichier déposé sur la roue pendant le mode Modifier : la modification entre dans son historique.
    public void ApplyActionsFromWheel(List<ActionItem> actions) => _session.SetActions(actions);
    // actions.json a été réécrit ailleurs : la session repart de ce fichier (historique vidé).
    public void ReloadActions() { _session.AttachActions(ActionStore.Load(), ActionStore.FilePath); UpdateCommands(); }

    // Diagnostics de disposition (--measure-layout-drag, --layout-checks).
    public void PreviewSelect(string id) => Select(id);
    public void PreviewLayoutDrag(string id, Vector delta, bool final) => OnWheelDragged(id, delta, final);
    public void PreviewLayoutUndo() => _session.Undo();
    public void PreviewLayoutRedo() => _session.Redo();
    public void PreviewLayoutReset() => _session.ResetLayout();

    // ---------- Session et enregistrement ----------

    private void OnSessionChanged()
    {
        if (_session.LastChangeHasActions)
        {
            // Actions et profil ensemble (section renommée ou ajoutée) : le profil d'abord, sans redessin, puis la roue avec les actions.
            if (!_session.LastChangeIsActions) UiRuntime.SetProfile(_session.Profile, notify: false);
            _host.ApplyActions(_session.Actions);
            // La roue est redessinée avant de mesurer la carte où se colle la bulle.
            _dispatcher.BeginInvoke(UpdateBubble, DispatcherPriority.Loaded);
        }
        else UiRuntime.SetProfile(_session.Profile, notify: !_session.LastChangeIsLayout);
        if (_session.LastChangeIsLayout) _host.ApplyWheelLayout();
        _saveTimer.Stop();
        _saveTimer.Start();
        if (_interacting == 0) UpdateCommands();
        ScheduleRefresh();
    }

    private void Save()
    {
        if (!_session.IsDirty) return;
        try { _session.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Notifications.Show("Enregistrement impossible", ex.Message);
        }
    }

    private void UpdateCommands() => CommandsChanged?.Invoke();

    // La bulle suit la modification, sauf pendant un geste.
    private void ScheduleRefresh()
    {
        if (_interacting > 0 || _refreshPending) return;
        _refreshPending = true;
        _dispatcher.BeginInvoke(() =>
        {
            _refreshPending = false;
            if (_interacting > 0 || _stopped) return;
            ShowSelection();
        }, DispatcherPriority.Background);
    }

    private string NextGesture() => $"g{++_gesture}";

    // Flèches : les pas rapprochés sur la même valeur forment une seule entrée d'historique.
    private string StepGesture(string target)
    {
        if (_stepTarget != target || DateTime.Now - _lastStep > TimeSpan.FromMilliseconds(700)) { _stepTarget = target; _gesture++; }
        _lastStep = DateTime.Now;
        return $"step{_gesture}";
    }

    // Un renommage de section annulé ou rétabli : les autres profils enregistrés suivent, comme au renommage.
    private void FollowSectionRename(Func<bool> step)
    {
        static HashSet<string> Names(IEnumerable<ActionItem> actions) => actions.Select(a => a.Group.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var before = Names(_session.Actions);
        if (!step()) return;
        var after = Names(_session.Actions);
        if (before.Except(after, StringComparer.OrdinalIgnoreCase).ToList() is not [var gone] ||
            after.Except(before, StringComparer.OrdinalIgnoreCase).ToList() is not [var came]) return;
        try { _store.RenameSectionInOtherProfiles(gone, came, _session.Profile.Name); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { CrashLog.Write("WheelEditor.FollowSectionRename", ex); }
    }

    // ---------- Clavier ----------

    // Touches de la roue et des bulles (hors champ de saisie) : Échap, Ctrl+Z / Ctrl+Y, flèches.
    public void HandleKey(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !_host.WheelGestureActive) { Stop(); e.Handled = true; return; }
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (ctrl && e.Key == Key.Z) { if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) Redo(); else Undo(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.Y) { Redo(); e.Handled = true; return; }
        if (_selectedId is null) return;
        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        var (dx, dy) = e.Key switch { Key.Left => (-step, 0), Key.Right => (step, 0), Key.Up => (0, -step), Key.Down => (0, step), _ => (0, 0) };
        if (dx == 0 && dy == 0) return;
        Nudge(_selectedId, dx, dy, StepGesture(_selectedId + "|nudge"));
        e.Handled = true;
    }

    private void OnWheelKeyDown(KeyEventArgs e) => HandleKey(e);

    private void Nudge(string id, double dx, double dy, string gesture)
    {
        var limited = _host.LimitWheelTranslation(id, new Vector(dx, dy));
        dx = limited.X; dy = limited.Y;
        if (id.StartsWith("wheel.spoke", StringComparison.Ordinal)) return;
        if (id is "wheel.label" or "wheel.label.title" or "wheel.label.subtitle" or "wheel.badge")
        {
            var layout = _session.Profile.Layout.Clone();
            foreach (var name in _host.WheelSectionNames)
            {
                var attachmentId = $"{id}:{name}";
                layout.Attachments[attachmentId] = layout.Attachments.GetValueOrDefault(attachmentId) + new Orbit.WheelPoint(dx, dy);
            }
            _session.SetLayout(layout, gesture);
            return;
        }
        if (id == "wheel.section")
        {
            var layout = _session.Profile.Layout.Clone();
            foreach (var name in _host.WheelSectionNames)
                if (_host.WheelPosition($"wheel.section:{name}") is { } position)
                    layout.Sections[name] = _host.LimitSectionPosition(position + new Orbit.WheelPoint(dx, dy));
            _session.SetLayout(layout, gesture);
            return;
        }
        if (id == "wheel.hub" && _host.WheelPosition(id) is { } hub)
        {
            var sections = _host.WheelSectionNames.ToDictionary(name => name, name => _host.WheelPosition($"wheel.section:{name}")!.Value);
            _session.SetLayout(Orbit.WheelLayout.MoveHub(_session.Profile.Layout, hub, sections, new Orbit.WheelPoint(dx, dy), false,
                double.NegativeInfinity, double.NegativeInfinity, double.PositiveInfinity, double.PositiveInfinity), gesture);
            return;
        }
        if (IsAttachment(id))
        {
            _session.SetAttachment(id, _session.Profile.Layout.Attachments.GetValueOrDefault(id) + new Orbit.WheelPoint(dx, dy), gesture);
            return;
        }
        if (_host.CardStack(id) is { } stack)
        {
            _session.SetStack(stack.Section, stack.Placement with { X = stack.Placement.X + dx, Y = stack.Placement.Y + dy }, gesture);
            return;
        }
        if (id.StartsWith("wheel.section:", StringComparison.Ordinal) && _host.WheelPosition(id) is { } point)
        {
            _session.SetSectionPosition(id["wheel.section:".Length..],
                _host.LimitSectionPosition(new Orbit.WheelPoint(point.X + dx, point.Y + dy)), gesture);
            _host.CompleteWheelLayoutMove();
            return;
        }
        if (_session.CustomOf(id) is not null) { _session.MoveGroup(id, dx, dy, gesture); return; }
        if (dx != 0) _session.Set(id, "OffsetX", Format(Number(_session.ValueOf(id, "OffsetX")) + dx), gesture);
        if (dy != 0) _session.Set(id, "OffsetY", Format(Number(_session.ValueOf(id, "OffsetY")) + dy), gesture);
    }

    // ---------- Sélection et bulles ----------

    private void ShowSelection()
    {
        _host.ShowWheelSelection(_selectedId is not null && _selectedId.StartsWith("wheel") ? _selectedId : null);
        UpdateBubble();
    }

    // Carte sélectionnée sur la roue (la carte ou l'une de ses parties) → id de l'action.
    private static Guid? CardOf(string? id)
    {
        if (id is null || !id.StartsWith("wheel.card", StringComparison.Ordinal)) return null;
        var colon = id.IndexOf(':');
        return colon > 0 && Guid.TryParse(id[(colon + 1)..], out var guid) ? guid : null;
    }

    // Section sélectionnée sur la roue (le nœud ou son étiquette) → son nom.
    private static string? SectionOf(string? id) =>
        id?.StartsWith("wheel.section:", StringComparison.Ordinal) == true ? id["wheel.section:".Length..]
        : id?.StartsWith("wheel.label:", StringComparison.Ordinal) == true ? id["wheel.label:".Length..] : null;

    // Bulle de l'élément sélectionné (bouton ou section) ; cachée pendant un glisser ou sans sélection.
    private void UpdateBubble()
    {
        if (_stopped) return;
        var active = _host.WheelEditMode && !_host.WheelGestureActive;
        var card = active ? CardOf(_selectedId) : null;
        var section = active && card is null ? SectionOf(_selectedId) : null;
        if (section is null || _host.WheelSelectionScreenBounds($"wheel.section:{section}") is not { } sectionBounds ||
            !_session.Actions.Any(a => a.Group.Trim().Equals(section.Trim(), StringComparison.OrdinalIgnoreCase)))
            _sectionBubble?.HideBubble();
        else ShowSectionBubble(section, sectionBounds);
        if (card is not { } id || _session.Actions.All(a => a.Id != id) ||
            _host.WheelSelectionScreenBounds($"wheel.card:{id}") is not { } bounds)
        {
            // --trace-frames : même mesure sans bulle (sélection d'autre chose qu'un bouton), pour comparer.
            if (DiagnosticFlags.TraceFrames && _host.WheelEditMode && _selectedId is not null) MeasureFrames("edit-select");
            _bubble?.HideBubble();
            return;
        }
        _bubble ??= new ActionBubble(() => _session.Actions, (list, gesture) => _session.SetActions(list, gesture),
            _host.TestAction, HandleKey, () => { _selectedId = null; ShowSelection(); });
        ShareBubbleLook(_bubble);
        // --trace-frames : fluidité pendant l'ouverture de la bulle (perf.log, phase « bubble-open »).
        if (DiagnosticFlags.TraceFrames && !_bubble.IsVisible) MeasureFrames("bubble-open");
        _bubble.ShowFor(id, bounds, AttachBubble(_bubble));
    }

    private void ShowSectionBubble(string section, Rect bounds)
    {
        _sectionBubble ??= new SectionBubble(() => _session.Actions, RenameSectionFromWheel,
            (name, icon) => _session.SetActions(Actions.ActionEdits.SetSectionIcon(_session.Actions, name, icon)), HandleKey);
        ShareBubbleLook(_sectionBubble);
        // --trace-frames : fluidité pendant l'ouverture de la bulle d'une section (perf.log, phase « section-bubble-open »).
        if (DiagnosticFlags.TraceFrames && !_sectionBubble.IsVisible) MeasureFrames("section-bubble-open");
        _sectionBubble.ShowFor(section, bounds, AttachBubble(_sectionBubble));
    }

    // « Renommer » du clic droit sur une section : sa bulle s'ouvre, le nom prêt à être remplacé.
    public void FocusSectionName(string section)
    {
        _selectedId = $"wheel.section:{section}";
        _host.ShowWheelSelection(_selectedId);
        UpdateBubble();
        _sectionBubble?.FocusName();
    }

    private void ShareBubbleLook(Window bubble)
    {
        foreach (var key in _look.Keys) bubble.Resources[key] = _look[key];
    }

    // Appartient à la roue : elle reste devant elle même quand la roue est cliquée ou ouvre un menu.
    private Rect? AttachBubble(Window bubble)
    {
        if (bubble.Owner is null && _host is Window wheel && wheel.IsVisible) bubble.Owner = wheel;
        return null;
    }

    // Nom validé dans la bulle d'une section : actions et profil actif en une étape (Annuler les remet) ;
    // les autres profils enregistrés suivent tout de suite.
    private string? RenameSectionFromWheel(string oldName, string newName)
    {
        newName = newName.Trim();
        var (renamed, error) = Actions.ActionEdits.RenameSection(_session.Actions, oldName, newName);
        if (error is not null) return error;
        if (SectionOf(_selectedId) is { } selected && selected.Trim().Equals(oldName.Trim(), StringComparison.OrdinalIgnoreCase))
            _selectedId = $"wheel.section:{newName}";
        _session.RenameSection(renamed, oldName.Trim(), newName, NextGesture());
        try { _store.RenameSectionInOtherProfiles(oldName.Trim(), newName, _session.Profile.Name); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { CrashLog.Write("WheelEditor.RenameSection", ex); }
        _host.ShowWheelSelection(_selectedId);
        return null;
    }

    // Nouvelle section posée par un + de l'anneau : une étape, puis sa bulle s'ouvre, le nom prêt à être remplacé.
    public void AddSectionFromWheel(List<ActionItem> actions, Orbit.WheelLayoutData layout, string section)
    {
        _session.SetActionsAndLayout(actions, layout, NextGesture());
        _dispatcher.BeginInvoke(() => FocusSectionName(section), DispatcherPriority.Loaded);
    }

    // Nouvelle colonne : le bouton et sa place arrivent en une seule étape (un seul ↶ les retire).
    public void ApplyActionsAndLayoutFromWheel(List<ActionItem> actions, Orbit.WheelLayoutData layout) =>
        _session.SetActionsAndLayout(actions, layout, NextGesture());

    private FrameMeter? _meter;
    private void MeasureFrames(string phase)
    {
        if (_meter is not null) return;
        _meter = new FrameMeter(true);
        _meter.Begin(phase);
        var stop = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        stop.Tick += (_, _) => { stop.Stop(); _meter?.End(); _meter = null; };
        stop.Start();
    }

    // ---------- Gestes de la roue ----------

    private void OnWheelPicked(string id)
    {
        // La sélection dans la roue est immédiate ; la bulle attend la fin du geste.
        _selectedId = id;
        _pendingWheelPick = id;
        _wheelPickTimer.Stop(); _wheelPickTimer.Start();
    }

    private void OnWheelDragged(string id, Vector delta, bool final)
    {
        if (!final) { _bubble?.HideBubble(); _sectionBubble?.HideBubble(); }
        if (id.StartsWith("wheel.spoke", StringComparison.Ordinal)) return;
        var stepDelta = _host.LimitWheelTranslation(id, delta - _acceptedScreenDelta);
        delta = _acceptedScreenDelta + stepDelta;
        _acceptedScreenDelta = final ? new Vector() : delta;
        if (id == "wheel.hub" && _host.WheelPosition(id) is { } hub)
        {
            if (_dragStart is null)
            {
                _dragStart = new Vector(hub.X, hub.Y); _interacting++;
                _hubDragLayout = _session.Profile.Layout.Clone();
                _hubDragSections = _host.WheelSectionNames.ToDictionary(name => name, name => _host.WheelPosition($"wheel.section:{name}")!.Value);
            }
            var whole = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || DiagnosticFlags.LayoutDragWholeWheel;
            var layout = Orbit.WheelLayout.MoveHub(_hubDragLayout!,
                new Orbit.WheelPoint(_dragStart.Value.X, _dragStart.Value.Y), _hubDragSections!, new Orbit.WheelPoint(delta.X, delta.Y), whole,
                double.NegativeInfinity, double.NegativeInfinity, double.PositiveInfinity, double.PositiveInfinity);
            _session.SetLayout(layout, $"drag{_gesture}");
            FinishLayoutDrag(final);
            if (final) { _hubDragLayout = null; _hubDragSections = null; }
            return;
        }
        if (id.StartsWith("wheel.card:", StringComparison.Ordinal))
        {
            // Lâchée sur une autre section : tout ce que le glisser a changé (pile, groupe, décalage d'une sous-carte)
            // est annulé, puis la carte part dans cette section — une seule entrée d'historique.
            if (_host.CardDropSection(id, final) is { } targetSection && final && CardOf(id) is { } movedCard)
            {
                var moveGesture = $"drag{_gesture}";
                if (_session.History.LastGesture == moveGesture) _session.Undo();
                var (moved, error) = Actions.ActionEdits.MoveToSection(_session.Actions, movedCard, targetSection);
                if (error is not null) Notifications.Show("Impossible de déplacer", error);
                else _session.SetActions(moved, moveGesture);
                _cardDragLayout = null;
                if (_dragStart is null) { _dragStart = new Vector(); _interacting++; }
                FinishLayoutDrag(true);
                return;
            }
            _cardDragLayout ??= _session.Profile.Layout.Clone();
            if (_host.CardDragLayout(id, delta, final, _cardDragLayout) is { } cardLayout)
            {
                if (_dragStart is null) { _dragStart = new Vector(); _interacting++; }
                _session.SetLayout(cardLayout, $"drag{_gesture}");
                FinishLayoutDrag(final);
                if (final) _cardDragLayout = null;
                return;
            }
            _cardDragLayout = null;
        }
        if (_host.CardStack(id) is { } stack)
        {
            if (_dragStart is null) { _dragStart = new Vector(stack.Placement.X, stack.Placement.Y); _interacting++; }
            var moved = _dragStart.Value + delta;
            _session.SetStack(stack.Section, new Orbit.WheelStackPlacement(moved.X, moved.Y, stack.Placement.Side), $"drag{_gesture}");
            FinishLayoutDrag(final);
            return;
        }
        if (IsAttachment(id))
        {
            var start = _session.Profile.Layout.Attachments.GetValueOrDefault(id);
            if (_dragStart is null) { _dragStart = new Vector(start.X, start.Y); _interacting++; }
            var moved = _dragStart.Value + delta;
            _session.SetAttachment(id, new Orbit.WheelPoint(moved.X, moved.Y), $"drag{_gesture}");
            FinishLayoutDrag(final);
            return;
        }
        if (id.StartsWith("wheel.section:", StringComparison.Ordinal) && _host.WheelPosition(id) is { } point)
        {
            if (_dragStart is null) { _dragStart = new Vector(point.X, point.Y); _interacting++; }
            var moved = _dragStart.Value + delta;
            _session.SetSectionPosition(id["wheel.section:".Length..], new Orbit.WheelPoint(moved.X, moved.Y), $"drag{_gesture}");
            if (final)
            {
                _dragStart = null; _gesture++; _interacting = Math.Max(0, _interacting - 1);
                _host.CompleteWheelLayoutMove(); UpdateCommands(); ScheduleRefresh();
            }
            return;
        }
        if (_session.CustomOf(id) is not null)
        {
            var step = delta - _dragGroupDelta;
            if (step.Length > 0) _session.MoveGroup(id, step.X, step.Y, $"drag{_gesture}");
            _dragGroupDelta = delta;
            if (final) { _dragGroupDelta = new Vector(); _gesture++; }
            return;
        }
        _dragStart ??= new Vector(Number(_session.ValueOf(id, "OffsetX")), Number(_session.ValueOf(id, "OffsetY")));
        if (_dragStart.Value.X == 0 && _dragStart.Value.Y == 0 && delta.Length < 0.5 && final) { _dragStart = null; return; }
        var gesture = $"drag{_gesture}";
        var target = _dragStart.Value + delta;
        _session.Set(id, "OffsetX", Format(Math.Round(target.X)), gesture);
        _session.Set(id, "OffsetY", Format(Math.Round(target.Y)), gesture);
        if (!final) return;
        _dragStart = null;
        _gesture++;
    }

    private void OnWheelLayoutProposed(Orbit.WheelLayoutData layout)
    {
        _session.SetLayout(layout);
        _host.CompleteWheelLayoutMove(); UpdateCommands(); ScheduleRefresh();
    }

    private static bool IsAttachment(string id) => id is "wheel.label" or "wheel.label.title" or "wheel.label.subtitle" or "wheel.badge" ||
        id.StartsWith("wheel.label:", StringComparison.Ordinal) ||
        id.StartsWith("wheel.label.title:", StringComparison.Ordinal) || id.StartsWith("wheel.label.subtitle:", StringComparison.Ordinal) ||
        id.StartsWith("wheel.badge:", StringComparison.Ordinal) || id == "wheel.search";

    private void FinishLayoutDrag(bool final)
    {
        if (!final) return;
        _dragStart = null; _gesture++; _interacting = Math.Max(0, _interacting - 1);
        _host.CompleteWheelLayoutMove(); UpdateCommands(); ScheduleRefresh();
    }

    private void OnWheelTransformed(string id, string property, double value, bool final)
    {
        _session.Set(id, property, Format(value), $"transform{_gesture}");
        if (final) _gesture++;
    }

    private static double Number(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static string Format(double value) => value.ToString(CultureInfo.InvariantCulture);
}
