using OrbitWeave.Orbit;
using OrbitWeave.Ui;

namespace OrbitWeave.Tests;

public class UiLayoutTests
{
    private static readonly WheelMetrics Metrics = new(150, 254, 59, 76, 96, 62, 27);

    [Fact]
    public void Added_section_avoids_manual_position_without_moving_it()
    {
        var inputs = new WheelSectionInput[] { new("Social", false), new("Nouvelle", false) };
        var auto = WheelLayout.Compute(inputs, Metrics);
        var layout = new WheelLayoutData();
        layout.Sections["Social"] = auto.Section("Nouvelle").Center;
        layout.Sections["Supprimée"] = new WheelPoint(12, 34);
        var result = WheelLayout.Compute(inputs, Metrics, layout, new HashSet<string> { "Nouvelle" });
        Assert.Equal(layout.Sections["Social"], result.Section("Social").Center);
        Assert.NotEqual(auto.Section("Nouvelle").Center, result.Section("Nouvelle").Center);
        var gap = result.Section("Nouvelle").Center - result.Section("Social").Center;
        Assert.True(Math.Sqrt(gap.X * gap.X + gap.Y * gap.Y) >= Metrics.SectionDiameter + 12);
        Assert.Equal(2, result.Sections.Count);
    }

    [Fact]
    public void Rename_moves_layout_styles_and_custom_parent_in_every_profile()
    {
        var store = new UiProfileStore(Directory.CreateTempSubdirectory("orbitweave-rename-").FullName);
        var profile = new UiProfile { Name = "Actif" };
        profile.Layout.Sections["Social"] = new WheelPoint(310, 320);
        profile.Layout.Attachments["wheel.label:Social"] = new WheelPoint(10, 20);
        profile.Layout.Stacks["Social"] = new WheelStackPlacement(-252, -30, -1);
        profile.CustomElements["wheel.custom:test"] = new UiCustomElement { Id = "wheel.custom:test", AttachTo = "wheel.section:Social" };
        profile.Elements["wheel.spoke:Social"] = new() { ["Opacity"] = "0.5" };
        store.Save(profile.Clone("Autre"));
        store.RenameSection("Social", "Amis", profile);
        foreach (var item in new[] { profile, store.Load("Autre", out _) })
        {
            Assert.Equal(new WheelPoint(310, 320), item.Layout.Sections["Amis"]);
            Assert.False(item.Layout.Sections.ContainsKey("Social"));
            Assert.Equal(new WheelPoint(10, 20), item.Layout.Attachments["wheel.label:Amis"]);
            Assert.Equal(-1, item.Layout.Stacks["Amis"].Side);
            Assert.Equal("wheel.section:Amis", item.CustomElements["wheel.custom:test"].AttachTo);
            Assert.Equal("0.5", item.Elements["wheel.spoke:Amis"]["Opacity"]);
        }
        var session = new UiEditSession(store, profile);
        session.ResetElement("wheel.section:Amis");
        Assert.False(session.Profile.Layout.Sections.ContainsKey("Amis"));
        session.Undo();
        Assert.Equal(new WheelPoint(310, 320), session.Profile.Layout.Sections["Amis"]);
    }

    [Fact]
    public void Moving_hub_alone_and_whole_wheel_have_distinct_geometry()
    {
        var original = new WheelLayoutData();
        var before = WheelLayout.Compute([new("Social", false), new("Web", true)], Metrics, original);
        var points = before.Sections.ToDictionary(item => item.Name, item => item.Center);
        var delta = new WheelPoint(80, 60);
        var alone = WheelLayout.Compute([new("Social", false), new("Web", true)], Metrics,
            WheelLayout.MoveHub(original, before.HubCenter, points, delta, false));
        Assert.Equal(before.Section("Social").Center, alone.Section("Social").Center);
        Assert.Equal(before.HubCenter + delta, alone.Section("Social").SpokeStart);
        Assert.Equal(before.SearchTopLeft + delta, alone.SearchTopLeft);
        var whole = WheelLayout.Compute([new("Social", false), new("Web", true)], Metrics,
            WheelLayout.MoveHub(original, before.HubCenter, points, delta, true));
        Assert.Equal(before.Section("Social").Center + delta, whole.Section("Social").Center);
        Assert.Equal(before.Section("Social").LabelAnchor + delta, whole.Section("Social").LabelAnchor);
        var clamped = WheelLayout.MoveHub(original, before.HubCenter, points, new WheelPoint(9999, 9999), true);
        Assert.All(clamped.Sections.Values, point => { Assert.InRange(point.X, 74, 1046); Assert.InRange(point.Y, 164, 956); });
    }

    [Fact]
    public void Attachments_and_pinned_stack_follow_parent_and_round_trip()
    {
        var store = new UiProfileStore(Directory.CreateTempSubdirectory("orbitweave-attachments-").FullName);
        var session = new UiEditSession(store, new UiProfile { Name = "Test" });
        session.SetAttachment("wheel.label:Social", new WheelPoint(12, 24));
        session.SetStack("Social", new WheelStackPlacement(-252, -30, -1));
        session.SetCustom(new UiCustomElement { Id = "wheel.custom:test", AttachTo = "wheel.section:Social", X = 50, Y = 70 });
        session.MoveGroup("wheel.custom:test", 5, 7, "drag");
        session.MoveGroup("wheel.custom:test", 5, 7, "drag");
        Assert.Equal(new WheelPoint(10, 14), session.Profile.Layout.Attachments["wheel.custom:test"]);
        Assert.False(session.Profile.Elements.ContainsKey("wheel.custom:test"));
        session.Undo();
        Assert.False(session.Profile.Layout.Attachments.ContainsKey("wheel.custom:test"));
        session.Redo();
        session.Save();
        var loaded = store.Load("Test", out _);
        Assert.Equal("wheel.section:Social", loaded.Clone("Copie").CustomElements["wheel.custom:test"].AttachTo);
        Assert.Equal(new WheelStackPlacement(-252, -30, -1), loaded.Layout.Stacks["Social"]);
        var before = WheelLayout.Compute([new("Social", false)], Metrics, loaded.Layout);
        loaded.Layout.Sections["Social"] = before.Section("Social").Center + new WheelPoint(80, 50);
        var after = WheelLayout.Compute([new("Social", false)], Metrics, loaded.Layout);
        Assert.Equal(new WheelPoint(80, 50), after.Section("Social").LabelAnchor - before.Section("Social").LabelAnchor);
    }

    [Fact]
    public void Legacy_offsets_migrate_once_and_keep_other_styles()
    {
        var profile = UiProfileStore.Parse("""
            {"Schema":5,"Name":"Ancien","Elements":{
                "wheel.section:Social":{"OffsetX":"-80","OffsetY":"-90","Opacity":"0.7"},
                "wheel.label:Social":{"OffsetX":"4","OffsetY":"8"}}}
            """);
        UiLayoutMigration.Apply(profile, [new("Social", false)], Metrics);
        Assert.Equal(new WheelPoint(480, 320), profile.Layout.Sections["Social"]);
        Assert.Equal(new WheelPoint(4, 8), profile.Layout.Attachments["wheel.label:Social"]);
        Assert.Equal("0.7", profile.Elements["wheel.section:Social"]["Opacity"]);
        Assert.False(profile.Elements["wheel.section:Social"].ContainsKey("OffsetX"));
        Assert.False(UiLayoutMigration.Apply(profile, [new("Social", false)], Metrics));
        Assert.Equal(new WheelPoint(480, 320), profile.Layout.Sections["Social"]);
        Assert.Equal(6, profile.Schema);
    }

    [Fact]
    public void Layout_drag_is_one_undo_step_and_round_trips()
    {
        var store = new UiProfileStore(Directory.CreateTempSubdirectory("orbitweave-layout-").FullName);
        var session = new UiEditSession(store, new UiProfile { Name = "Test" });
        for (var x = 300; x < 350; x++) session.SetSectionPosition("Social", new WheelPoint(x, 280), "drag");
        Assert.Equal(1, session.History.Count);
        session.Save();
        Assert.Equal(new WheelPoint(349, 280), store.Load("Test", out _).Layout.Sections["Social"]);
        session.Undo();
        Assert.Empty(session.Profile.Layout.Sections);
        session.Redo();
        Assert.Equal(new WheelPoint(349, 280), session.Profile.Layout.Sections["Social"]);
        session.ResetAll();
        Assert.True(session.Profile.Layout.IsEmpty);
        session.Undo();
        Assert.Equal(new WheelPoint(349, 280), session.Profile.Layout.Sections["Social"]);
    }
}
