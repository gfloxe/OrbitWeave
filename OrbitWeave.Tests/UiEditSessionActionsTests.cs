using OrbitWeave.Actions;
using OrbitWeave.Orbit;
using OrbitWeave.Ui;

namespace OrbitWeave.Tests;

public class UiEditSessionActionsTests
{
    private static UiProfileStore NewStore() => new(Directory.CreateTempSubdirectory("orbitweave-actions-").FullName);
    private static string NewActionsPath() => Path.Combine(Directory.CreateTempSubdirectory("orbitweave-actions-file-").FullName, "actions.json");

    private static ActionItem Item(string name, string target) =>
        new() { Group = "Fichiers", Name = name, Steps = [new OpenStep { Target = target }] };

    private static UiEditSession NewSession(string profileName = "Test")
    {
        var session = new UiEditSession(NewStore(), new UiProfile { Name = profileName });
        session.AttachActions([Item("Explorateur", "explorer.exe"), Item("Documents", "C:\\Docs")], NewActionsPath());
        return session;
    }

    private static List<ActionItem> Renamed(UiEditSession session, int index, string name)
    {
        var copy = session.Actions.Select(Clone).ToList();
        copy[index].Name = name;
        return copy;
    }

    private static ActionItem Clone(ActionItem item) =>
        System.Text.Json.JsonSerializer.Deserialize<ActionItem>(
            System.Text.Json.JsonSerializer.Serialize(item, ActionJson.Options), ActionJson.Options)!;

    [Fact]
    public void Attaching_actions_has_no_history_and_nothing_to_save()
    {
        var session = NewSession();
        Assert.Equal(["Explorateur", "Documents"], session.Actions.Select(a => a.Name));
        Assert.False(session.History.CanUndo);
        Assert.False(session.IsDirty);
        Assert.False(session.ActionsDirty);
    }

    [Fact]
    public void Action_change_is_undone_and_redone()
    {
        var session = NewSession();
        session.SetActions(Renamed(session, 0, "Fichiers"));
        Assert.Equal("Fichiers", session.Actions[0].Name);
        Assert.True(session.LastChangeIsActions);
        Assert.True(session.ActionsDirty);

        Assert.True(session.Undo());
        Assert.Equal("Explorateur", session.Actions[0].Name);
        Assert.True(session.Redo());
        Assert.Equal("Fichiers", session.Actions[0].Name);
        Assert.Equal("explorer.exe", Assert.IsType<OpenStep>(session.Actions[0].Steps[0]).Target);
    }

    [Fact]
    public void Layout_and_actions_share_one_history_in_order()
    {
        var session = NewSession();
        session.SetActions(Renamed(session, 0, "A"));
        var layout = session.Profile.Layout.Clone();
        layout.Sections["Fichiers"] = new WheelPoint(100, 200);
        session.SetLayout(layout);
        session.SetActions(Renamed(session, 1, "B"));

        session.Undo();
        Assert.Equal(("A", "Documents"), (session.Actions[0].Name, session.Actions[1].Name));
        Assert.True(session.Profile.Layout.Sections.ContainsKey("Fichiers"));
        session.Undo();
        Assert.False(session.Profile.Layout.Sections.ContainsKey("Fichiers"));
        Assert.Equal("A", session.Actions[0].Name);
        session.Undo();
        Assert.Equal("Explorateur", session.Actions[0].Name);
        Assert.False(session.History.CanUndo);
    }

    [Fact]
    public void A_typing_gesture_is_one_entry()
    {
        var session = NewSession();
        foreach (var name in new[] { "D", "Do", "Doc", "Docs" })
            session.SetActions(Renamed(session, 1, name), "rename-1");
        Assert.Equal(1, session.History.Count);
        session.Undo();
        Assert.Equal("Documents", session.Actions[1].Name);
    }

    [Fact]
    public void Save_writes_actions_atomically_and_raises_the_event()
    {
        var path = NewActionsPath();
        var session = new UiEditSession(NewStore(), new UiProfile { Name = "Test" });
        session.AttachActions([Item("Explorateur", "explorer.exe")], path);
        var saved = 0;
        session.ActionsSaved += () => saved++;
        session.SetActions(Renamed(session, 0, "Fichiers"));
        session.Save();

        Assert.Equal(1, saved);
        Assert.False(session.IsDirty);
        Assert.False(File.Exists(path + ".tmp"));
        Assert.Equal("Fichiers", Assert.Single(ActionStore.Load(path)).Name);
    }

    [Fact]
    public void Origin_profile_still_accepts_action_changes()
    {
        var session = NewSession(UiProfileStore.OriginName);
        var rejected = 0;
        session.OriginEditRejected += () => rejected++;
        session.SetActions(Renamed(session, 0, "Fichiers"));
        Assert.Equal(0, rejected);
        Assert.Equal("Fichiers", session.Actions[0].Name);
        session.Save();
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void Clearing_history_keeps_current_values()
    {
        var session = NewSession();
        session.SetActions(Renamed(session, 0, "Fichiers"));
        session.ClearHistory();
        Assert.False(session.History.CanUndo);
        Assert.False(session.Undo());
        Assert.Equal("Fichiers", session.Actions[0].Name);
    }

    [Fact]
    public void A_drag_that_ends_in_a_section_move_is_one_entry_without_its_offsets()
    {
        // Ce que fait l'éditeur quand une carte glissée est lâchée sur une autre section.
        var session = NewSession();
        var before = session.Profile.Layout.Clone();
        var moved = before.Clone();
        moved.Sections["Fichiers"] = new WheelPoint(40, 40);
        session.SetLayout(moved, "drag3");
        Assert.Equal("drag3", session.History.LastGesture);
        if (session.History.LastGesture == "drag3") session.Undo();
        session.SetActions(Renamed(session, 0, "Déplacé"), "drag3");
        Assert.Equal(1, session.History.Count);
        Assert.False(session.Profile.Layout.Sections.ContainsKey("Fichiers"));
        session.Undo();
        Assert.Equal("Explorateur", session.Actions[0].Name);
        Assert.Null(session.History.LastGesture);
    }

    [Fact]
    public void Renaming_a_section_moves_its_layout_and_overrides_in_one_undoable_step()
    {
        var session = NewSession();
        session.SetSectionPosition("Fichiers", new WheelPoint(300, 200));
        session.Set("wheel.section:Fichiers", "Width", "90");
        session.Set("wheel.label:Fichiers", "Opacity", "0.5");
        var (renamed, error) = ActionEdits.RenameSection(session.Actions, "Fichiers", "Dossiers");
        Assert.Null(error);

        session.RenameSection(renamed, "Fichiers", "Dossiers", "rename");
        Assert.All(session.Actions, a => Assert.Equal("Dossiers", a.Group));
        Assert.Equal(new WheelPoint(300, 200), session.Profile.Layout.Sections["Dossiers"]);
        Assert.False(session.Profile.Layout.Sections.ContainsKey("Fichiers"));
        Assert.Equal("90", session.ValueOf("wheel.section:Dossiers", "Width"));
        Assert.Equal("0.5", session.ValueOf("wheel.label:Dossiers", "Opacity"));
        Assert.Null(session.ValueOf("wheel.section:Fichiers", "Width"));
        Assert.True(session.LastChangeHasActions);
        Assert.False(session.LastChangeIsActions);

        Assert.True(session.Undo());
        Assert.All(session.Actions, a => Assert.Equal("Fichiers", a.Group));
        Assert.Equal(new WheelPoint(300, 200), session.Profile.Layout.Sections["Fichiers"]);
        Assert.Equal("90", session.ValueOf("wheel.section:Fichiers", "Width"));
        Assert.Null(session.ValueOf("wheel.section:Dossiers", "Width"));

        Assert.True(session.Redo());
        Assert.Equal("0.5", session.ValueOf("wheel.label:Dossiers", "Opacity"));
    }

    [Fact]
    public void A_new_section_and_its_place_on_the_ring_are_one_step()
    {
        var session = NewSession();
        var (actions, name) = ActionEdits.NewSection(session.Actions);
        var layout = session.Profile.Layout.Clone();
        layout.Sections[name] = new WheelPoint(700, 300);
        session.SetActionsAndLayout(actions, layout);
        Assert.Contains(session.Actions, a => a.Group == name);
        Assert.Equal(new WheelPoint(700, 300), session.Profile.Layout.Sections[name]);
        Assert.True(session.Undo());
        Assert.DoesNotContain(session.Actions, a => a.Group == name);
        Assert.False(session.Profile.Layout.Sections.ContainsKey(name));
        Assert.False(session.History.CanUndo);
    }
}
