using System.Text.Json;
using OrbitWeave.Ui;

namespace OrbitWeave.Tests;

public class UiEditSessionTests
{
    private static UiProfileStore NewStore() => new(Directory.CreateTempSubdirectory("orbitweave-edit-").FullName);

    private static UiProfile Sample()
    {
        var profile = new UiProfile { Name = "Snapse" };
        profile.Tokens["HubDiameter"] = "80";
        profile.Themes["Sombre"] = new() { ["GlassBaseBrush"] = "#FF101010" };
        profile.Elements["wheel.hub"] = new() { ["OffsetX"] = "4" };
        return profile;
    }

    private static string Snapshot(UiProfile profile) =>
        JsonSerializer.Serialize(new { profile.Tokens, profile.Themes, profile.Elements });

    [Fact]
    public void A_gesture_is_one_history_entry()
    {
        var session = new UiEditSession(NewStore(), Sample());
        for (var x = 1; x <= 30; x++) session.Set("wheel.section:Web", "OffsetX", x.ToString(), "drag-1");
        session.Set("wheel.section:Web", "OffsetY", "7", "drag-1");

        Assert.Equal(1, session.History.Count);
        session.Undo();
        Assert.Null(session.ValueOf("wheel.section:Web", "OffsetX"));
        Assert.False(session.IsModified("wheel.section:Web"));
        session.Redo();
        Assert.Equal(("30", "7"), (session.ValueOf("wheel.section:Web", "OffsetX"), session.ValueOf("wheel.section:Web", "OffsetY")));
    }

    [Fact]
    public void Undoing_every_change_restores_the_exact_profile()
    {
        var session = new UiEditSession(NewStore(), Sample());
        var before = Snapshot(session.Profile);
        for (var i = 0; i < EditHistory.Capacity; i++)
        {
            switch (i % 4)
            {
                case 0: session.Set($"wheel.card:{i % 7}", "Background", $"#FF{i % 256:X2}0000"); break;
                case 1: session.SetToken("HubDiameter", (70 + i % 20).ToString(), null); break;
                case 2: session.SetToken("GlassBaseBrush", $"#FF00{i % 256:X2}00", "Sombre"); break;
                default: session.Set("wheel.hub", "OffsetX", i % 3 == 0 ? null : i.ToString()); break;
            }
        }
        while (session.Undo()) { }

        Assert.Equal(before, Snapshot(session.Profile));
    }

    [Fact]
    public void Resets_are_undoable()
    {
        var session = new UiEditSession(NewStore(), Sample());
        var before = Snapshot(session.Profile);
        session.ResetElement("wheel.hub");
        Assert.False(session.IsModified("wheel.hub"));
        session.ResetAll();
        Assert.Empty(session.Profile.Tokens);
        Assert.Empty(session.Profile.Themes);
        session.Undo();
        session.Undo();
        Assert.Equal(before, Snapshot(session.Profile));
    }

    [Fact]
    public void Editing_origin_never_creates_a_profile_or_changes_the_active_session()
    {
        var store = NewStore();
        store.Save(new UiProfile { Name = "Mon interface" });
        var originalFile = File.ReadAllText(Path.Combine(store.Folder, "Mon interface.json"));
        var rejected = 0;
        var session = new UiEditSession(store, new UiProfile());
        session.OriginEditRejected += () => rejected++;

        session.Set("wheel.search", "Visible", "false");
        session.Save();

        Assert.Equal(1, rejected);
        Assert.Equal(UiProfileStore.OriginName, session.Profile.Name);
        Assert.False(session.IsDirty);
        Assert.False(session.History.CanUndo);
        Assert.Equal(new[] { "Origine", "Mon interface" }, store.Names());
        Assert.Equal(originalFile, File.ReadAllText(Path.Combine(store.Folder, "Mon interface.json")));
    }

    [Fact]
    public void An_explicitly_created_profile_is_editable_without_another_automatic_copy()
    {
        var store = NewStore();
        var explicitProfile = new UiProfile { Name = "Choix explicite" };
        store.Save(explicitProfile);
        var session = new UiEditSession(store, explicitProfile);
        session.SetSectionPosition("Social", new Orbit.WheelPoint(302, 380));
        session.Save();
        Assert.Equal("Choix explicite", session.Profile.Name);
        Assert.Equal(302, store.Load("Choix explicite", out _).Layout.Sections["Social"].X);
        Assert.Equal(new[] { "Origine", "Choix explicite" }, store.Names());
    }

    [Fact]
    public void Unchanged_values_do_not_create_history()
    {
        var session = new UiEditSession(NewStore(), Sample());
        session.Set("wheel.hub", "OffsetX", "4");
        session.Set("wheel.hub", "OffsetY", "  ");
        Assert.False(session.History.CanUndo);
    }

    [Fact]
    public void History_keeps_the_last_500_entries()
    {
        var session = new UiEditSession(NewStore(), Sample());
        for (var i = 0; i < 600; i++) session.Set("wheel.hub", "OffsetY", i.ToString());
        Assert.Equal(EditHistory.Capacity, session.History.Count);
    }

    [Fact]
    public void State_edits_are_undoable_and_reset_with_the_element()
    {
        var session = new UiEditSession(NewStore(), Sample());
        session.SetState("wheel.section:Web", "Hover", "Scale", "1.2");
        session.SetState("wheel.section:Web", "Pressed", "Opacity", "0.5");
        Assert.True(session.IsModified("wheel.section:Web"));
        session.ResetElement("wheel.section:Web");
        Assert.False(session.IsModified("wheel.section:Web"));
        session.Undo();
        Assert.Equal("1.2", session.StateOf("wheel.section:Web", "Hover", "Scale"));
        Assert.Equal("0.5", session.StateOf("wheel.section:Web", "Pressed", "Opacity"));
    }

    [Fact]
    public void Animation_edits_survive_save_and_are_undoable()
    {
        var store = NewStore();
        var session = new UiEditSession(store, Sample());
        session.SetAnimation("wheel.section:Web", "Type", "Scale");
        session.SetAnimation("wheel.section:Web", "DurationMs", "180");
        session.Save();
        Assert.Equal("Scale", store.Load("Snapse", out _).Animations["wheel.section:Web"]["Type"]);
        session.ResetElement("wheel.section:Web");
        Assert.Null(session.AnimationOf("wheel.section:Web", "Type"));
        session.Undo();
        Assert.Equal("180", session.AnimationOf("wheel.section:Web", "DurationMs"));
    }

    [Fact]
    public void Adding_a_custom_element_is_undoable_and_saved()
    {
        var store = NewStore();
        var session = new UiEditSession(store, Sample());
        var item = new UiCustomElement { Id = "wheel.custom:test", Kind = "Text", Content = "Bonjour", X = 20 };
        session.SetCustom(item);
        session.Save();
        Assert.Equal("Bonjour", store.Load("Snapse", out _).CustomElements[item.Id].Content);
        session.Undo();
        Assert.Null(session.CustomOf(item.Id));
        session.Redo();
        Assert.Equal(20, session.CustomOf(item.Id)?.X);
    }

    [Fact]
    public void Removing_a_custom_element_cleans_its_styles_and_is_undoable()
    {
        var session = new UiEditSession(NewStore(), Sample());
        var item = new UiCustomElement { Id = "wheel.custom:test", Content = "Texte" };
        session.SetCustom(item);
        session.Set(item.Id, "Visible", "false");
        session.SetAnimation(item.Id, "Type", "Fade");
        session.RemoveCustom(item.Id);
        Assert.Null(session.CustomOf(item.Id));
        Assert.Null(session.ValueOf(item.Id, "Visible"));
        Assert.Null(session.AnimationOf(item.Id, "Type"));
        session.Undo();
        Assert.Equal("Texte", session.CustomOf(item.Id)?.Content);
        Assert.Equal("false", session.ValueOf(item.Id, "Visible"));
        Assert.Equal("Fade", session.AnimationOf(item.Id, "Type"));
    }

    [Fact]
    public void Grouped_custom_elements_move_together_in_one_undoable_edit()
    {
        var store = NewStore();
        var session = new UiEditSession(store, Sample());
        session.SetCustom(new UiCustomElement { Id = "wheel.custom:a", Group = "Titre" });
        session.SetCustom(new UiCustomElement { Id = "wheel.custom:b", Group = "Titre" });
        session.SetCustom(new UiCustomElement { Id = "wheel.custom:c", Group = "Autre" });
        var before = session.History.Count;
        session.MoveGroup("wheel.custom:a", 12, -4, "drag");
        Assert.Equal(before + 1, session.History.Count);
        Assert.Equal("12", session.ValueOf("wheel.custom:a", "OffsetX"));
        Assert.Equal("12", session.ValueOf("wheel.custom:b", "OffsetX"));
        Assert.Null(session.ValueOf("wheel.custom:c", "OffsetX"));
        session.Undo();
        Assert.Null(session.ValueOf("wheel.custom:a", "OffsetX"));
        Assert.Null(session.ValueOf("wheel.custom:b", "OffsetY"));
        session.Redo();
        session.Save();
        Assert.Equal("Titre", store.Load("Snapse", out _).CustomElements["wheel.custom:b"].Group);
    }
}
