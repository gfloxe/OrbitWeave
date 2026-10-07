using OrbitWeave.Actions;
namespace OrbitWeave.Tests;

public class SectionEditsTests
{
    private static List<ActionItem> Sample()
    {
        var discord = new ActionItem { Group = "Social", GroupIcon = "◌", Name = "Discord", Steps = [new OpenStep { Target = "discord.exe" }] };
        return
        [
            discord,
            new() { Group = "Social", GroupIcon = "◌", Name = "Serveur", ParentId = discord.Id },
            new() { Group = "Social", GroupIcon = "◌", Name = "Courriel" },
            new() { Group = "Web", GroupIcon = "◎", Name = "YouTube" }
        ];
    }

    [Fact]
    public void New_button_goes_at_the_end_of_its_section_or_under_its_parent()
    {
        var list = Sample();
        var (added, id, error) = ActionEdits.NewButton(list, "social");
        Assert.Null(error);
        Assert.Equal(id, added[3].Id);
        Assert.Equal(("Social", "◌", ActionEdits.NewButtonName, (Guid?)null), (added[3].Group, added[3].GroupIcon, added[3].Name, added[3].ParentId));

        var (child, childId, _) = ActionEdits.NewButton(list, "Social", list[0].Id);
        Assert.Equal(childId, child[2].Id);
        Assert.Equal(list[0].Id, child[2].ParentId);
    }

    [Fact]
    public void A_branch_takes_more_than_five_buttons()
    {
        var list = Sample();
        for (var i = 0; i < 3; i++) list.Add(new ActionItem { Group = "Social", Name = $"{i}" });
        var (more, id, error) = ActionEdits.NewButton(list, "Social");
        Assert.Null(error);
        Assert.NotNull(id);
        Assert.Null(ActionEdits.Duplicate(more, list[0].Id).Error);
        Assert.Equal(7, ActionTree.Roots(ActionEdits.Duplicate(more, list[0].Id).Actions, "Social").Count);
    }

    [Fact]
    public void Duplicate_copies_the_button_without_its_children_after_its_subtree()
    {
        var list = Sample();
        var (copy, id, error) = ActionEdits.Duplicate(list, list[0].Id);
        Assert.Null(error);
        Assert.Equal(id, copy[2].Id);
        Assert.Equal("Discord (copie)", copy[2].Name);
        Assert.NotEqual(list[0].Steps[0].Id, copy[2].Steps[0].Id);
        Assert.Equal(5, copy.Count);
    }

    [Fact]
    public void Remove_takes_the_children_and_the_last_button_leaves_an_empty_one()
    {
        var list = Sample();
        var removed = ActionEdits.Remove(list, list[0].Id);
        Assert.Equal(["Courriel", "YouTube"], removed.Select(a => a.Name));
        var empty = ActionEdits.Remove(list, list[3].Id);
        Assert.Equal(4, empty.Count);
        Assert.Equal(("Web", ActionEdits.NewButtonName), (empty[3].Group, empty[3].Name));
    }

    [Fact]
    public void Move_to_section_carries_the_children_and_takes_the_section_look()
    {
        var list = Sample();
        var (moved, error) = ActionEdits.MoveToSection(list, list[0].Id, "web");
        Assert.Null(error);
        Assert.Equal(["Courriel", "YouTube", "Discord", "Serveur"], moved.Select(a => a.Name));
        Assert.All(moved.Skip(1), a => Assert.Equal(("Web", "◎"), (a.Group, a.GroupIcon)));
        Assert.Equal(moved[2].Id, moved[3].ParentId);

        var (lastMoved, _) = ActionEdits.MoveToSection(list, list[3].Id, "Social");
        Assert.Contains(lastMoved, a => a.Group == "Web" && a.Name == ActionEdits.NewButtonName);
        var (subMoved, _) = ActionEdits.MoveToSection(list, list[1].Id, "Social");
        Assert.Null(subMoved.Single(a => a.Name == "Serveur").ParentId);
    }

    [Fact]
    public void Sections_are_renamed_and_removed_with_their_buttons()
    {
        var list = Sample();
        Assert.Equal("Une section « Web » existe déjà.", ActionEdits.RenameSection(list, "Social", "web").Error);
        var (renamed, error) = ActionEdits.RenameSection(list, "social", " Amis ");
        Assert.Null(error);
        Assert.Equal(3, renamed.Count(a => a.Group == "Amis"));
        Assert.Equal(["YouTube"], ActionEdits.RemoveSection(list, "SOCIAL").Select(a => a.Name));
    }

    [Fact]
    public void Section_symbol_changes_on_every_button_of_the_section()
    {
        var changed = ActionEdits.SetSectionIcon(Sample(), "social", "★");
        Assert.All(changed.Where(a => a.Group == "Social"), a => Assert.Equal("★", a.GroupIcon));
        Assert.Equal("◎", changed.Single(a => a.Group == "Web").GroupIcon);
    }

    [Fact]
    public void New_section_has_one_empty_button_a_free_name_and_sits_after_the_given_section()
    {
        var (list, name) = ActionEdits.NewSection(Sample(), "Social");
        Assert.Equal(ActionEdits.NewSectionName, name);
        Assert.Equal((name, "✦", ActionEdits.NewButtonName), (list[3].Group, list[3].GroupIcon, list[3].Name));
        Assert.Equal("Web", list[4].Group);

        var (again, second) = ActionEdits.NewSection(list);
        Assert.Equal($"{ActionEdits.NewSectionName} 2", second);
        Assert.Equal(second, again[^1].Group);
    }
}
