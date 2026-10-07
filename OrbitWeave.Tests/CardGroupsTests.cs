using OrbitWeave.Orbit;

namespace OrbitWeave.Tests;

public class CardGroupsTests
{
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid(), D = Guid.NewGuid();
    private static readonly Guid[] Roots = [A, B, C, D];

    [Fact]
    public void Without_groups_every_card_is_in_the_main_stack()
    {
        var (main, extra) = CardGroups.Partition(new WheelLayoutData(), "Fichiers", Roots);
        Assert.Equal(Roots, main);
        Assert.Empty(extra);
    }

    [Fact]
    public void Detach_makes_a_free_card_and_closes_the_gap()
    {
        var layout = new WheelLayoutData();
        var id = CardGroups.Detach(layout, "Fichiers", B, 200, -40);
        var (main, extra) = CardGroups.Partition(layout, "Fichiers", Roots);
        Assert.Equal([A, C, D], main);
        var group = Assert.Single(extra);
        Assert.Equal((id, 200d, -40d), (group.Id, group.Group.X, group.Group.Y));
        Assert.Equal([B], group.Cards);
    }

    [Fact]
    public void Cut_in_the_main_stack_moves_the_following_cards()
    {
        var layout = new WheelLayoutData();
        var id = CardGroups.CutAfter(layout, "Fichiers", Roots, B, 10, 20)!;
        var (main, extra) = CardGroups.Partition(layout, "Fichiers", Roots);
        Assert.Equal([A, B], main);
        Assert.Equal([C, D], Assert.Single(extra, item => item.Id == id).Cards);
        Assert.Null(CardGroups.CutAfter(layout, "Fichiers", Roots, D, 0, 0));
    }

    [Fact]
    public void Cut_inside_a_group_splits_that_group()
    {
        var layout = new WheelLayoutData();
        var first = CardGroups.CutAfter(layout, "Fichiers", Roots, A, 0, 0)!; // [B, C, D]
        var second = CardGroups.CutAfter(layout, "Fichiers", Roots, C, 5, 5)!;
        var (_, extra) = CardGroups.Partition(layout, "Fichiers", Roots);
        Assert.Equal([B, C], extra.Single(item => item.Id == first).Cards);
        Assert.Equal([D], extra.Single(item => item.Id == second).Cards);
    }

    [Fact]
    public void Merge_appends_prepends_or_returns_to_the_main_stack()
    {
        var layout = new WheelLayoutData();
        var cd = CardGroups.CutAfter(layout, "Fichiers", Roots, B, 0, 0)!; // main [A, B], cd [C, D]
        var a = CardGroups.Detach(layout, "Fichiers", A, 0, 0);            // main [B]
        CardGroups.Merge(layout, a, cd, append: true);
        Assert.Equal([C, D, A], CardGroups.Partition(layout, "Fichiers", Roots).Extra.Single().Cards);
        CardGroups.Merge(layout, cd, null, append: true);
        var (main, extra) = CardGroups.Partition(layout, "Fichiers", Roots);
        Assert.Equal(Roots, main);
        Assert.Empty(extra);
        Assert.Empty(layout.CardGroups);
    }

    [Fact]
    public void Missing_cards_are_ignored_and_empty_groups_hidden()
    {
        var layout = new WheelLayoutData();
        CardGroups.Detach(layout, "Fichiers", Guid.NewGuid(), 0, 0);
        CardGroups.Detach(layout, "Web", A, 0, 0);
        var (main, extra) = CardGroups.Partition(layout, "Fichiers", Roots);
        Assert.Equal(Roots, main);
        Assert.Empty(extra);
    }

    [Fact]
    public void Groups_round_trip_follow_a_section_rename_and_undo()
    {
        var store = new OrbitWeave.Ui.UiProfileStore(Directory.CreateTempSubdirectory("orbitweave-groups-").FullName);
        var session = new OrbitWeave.Ui.UiEditSession(store, new OrbitWeave.Ui.UiProfile { Name = "Test" });
        var layout = session.Profile.Layout.Clone();
        var id = CardGroups.Detach(layout, "Social", A, -120, 40);
        session.SetLayout(layout);
        session.Save();
        var loaded = store.Load("Test", out var problem);
        Assert.Null(problem);
        Assert.Equal([A], loaded.Layout.CardGroups[id].Cards);
        Assert.Equal((-120d, 40d), (loaded.Layout.CardGroups[id].X, loaded.Layout.CardGroups[id].Y));
        store.RenameSection("Social", "Amis", loaded);
        Assert.Equal("Amis", loaded.Layout.CardGroups[id].Section);
        session.Undo();
        Assert.Empty(session.Profile.Layout.CardGroups);
    }

    [Fact]
    public void Clone_copies_groups_deeply()
    {
        var layout = new WheelLayoutData();
        var id = CardGroups.Detach(layout, "Fichiers", A, 1, 2);
        var copy = layout.Clone();
        copy.CardGroups[id].Cards.Add(B);
        copy.CardGroups[id].X = 99;
        Assert.Equal([A], layout.CardGroups[id].Cards);
        Assert.Equal(1, layout.CardGroups[id].X);
    }
}
