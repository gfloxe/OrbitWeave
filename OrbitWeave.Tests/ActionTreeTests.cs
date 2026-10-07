using OrbitWeave.Actions;

namespace OrbitWeave.Tests;

public class ActionTreeTests
{
    private static ActionItem Item(string name, string group = "Fichiers", ActionItem? parent = null) =>
        new() { Name = name, Group = group, ParentId = parent?.Id, Target = name.ToLowerInvariant() };

    [Fact]
    public void Roots_and_children_keep_list_order()
    {
        var docs = Item("Documents");
        var list = new List<ActionItem> { Item("Explorateur"), docs, Item("Photos", parent: docs), Item("Cours", parent: docs), Item("Web", "Web") };
        Assert.Equal(["Explorateur", "Documents"], ActionTree.Roots(list, "fichiers ").Select(a => a.Name));
        Assert.Equal(["Photos", "Cours"], ActionTree.Children(list, docs.Id).Select(a => a.Name));
        Assert.True(ActionTree.HasChildren(list, docs.Id));
    }

    [Fact]
    public void Flatten_orders_depth_first_and_sets_depth()
    {
        var docs = Item("Documents");
        var photos = Item("Photos", parent: docs);
        var list = new List<ActionItem> { Item("Vacances", parent: photos), docs, Item("Explorateur"), photos };
        var flat = ActionTree.Flatten(list, "Fichiers");
        Assert.Equal(["Documents", "Photos", "Vacances", "Explorateur"], flat.Select(a => a.Name));
        Assert.Equal([0, 1, 2, 0], flat.Select(a => a.TreeDepth));
        Assert.Equal(2, ActionTree.Depth(list, list[0]));
        Assert.Equal(["Documents", "Photos"], ActionTree.Ancestors(list, list[0].Id).Select(a => a.Name));
    }

    [Fact]
    public void AddChild_inherits_section_and_goes_after_last_descendant()
    {
        var docs = new ActionItem { Name = "Documents", Group = "Fichiers", GroupIcon = "▣", GroupVisible = false, GroupType = "Fichiers" };
        var photos = Item("Photos", parent: docs);
        var next = Item("Explorateur");
        var list = new List<ActionItem> { docs, photos, next };
        var child = ActionTree.AddChild(list, docs)!;
        Assert.Equal(docs.Id, child.ParentId);
        Assert.Equal(("Fichiers", "▣", false, "Fichiers"), (child.Group, child.GroupIcon, child.GroupVisible, child.GroupType));
        Assert.Equal("", child.Target);
        Assert.Equal(2, list.IndexOf(child));
        Assert.True(ActionTree.IsFolderOnly(child));
    }

    [Fact]
    public void AddChild_accepts_a_sixth_card()
    {
        var docs = Item("Documents");
        var list = new List<ActionItem> { docs };
        for (var i = 0; i < 6; i++) Assert.NotNull(ActionTree.AddChild(list, docs));
        Assert.Equal(6, ActionTree.Children(list, docs.Id).Count);
        Assert.Equal(7, list.Count);
    }

    [Fact]
    public void Remove_takes_the_whole_subtree()
    {
        var docs = Item("Documents");
        var photos = Item("Photos", parent: docs);
        var list = new List<ActionItem> { docs, photos, Item("Vacances", parent: photos), Item("Explorateur") };
        Assert.Equal(3, ActionTree.Remove(list, docs));
        Assert.Equal(["Explorateur"], list.Select(a => a.Name));
    }

    [Fact]
    public void Repair_lifts_orphans_self_references_and_loops()
    {
        var a = Item("A");
        var b = Item("B", parent: a);
        a.ParentId = b.Id; // boucle A → B → A
        var self = Item("Seul");
        self.ParentId = self.Id;
        var orphan = Item("Orphelin");
        orphan.ParentId = Guid.NewGuid();
        var list = new List<ActionItem> { a, b, self, orphan };
        Assert.True(ActionTree.Repair(list));
        Assert.Null(self.ParentId);
        Assert.Null(orphan.ParentId);
        Assert.True(a.ParentId is null || b.ParentId is null);
        Assert.False(ActionTree.Repair(list));
    }

    [Fact]
    public void Repair_aligns_a_child_on_its_root_section()
    {
        var docs = new ActionItem { Name = "Documents", Group = "Fichiers", GroupIcon = "▣" };
        var stray = new ActionItem { Name = "Photos", Group = "Autre", GroupIcon = "✦", ParentId = docs.Id };
        var list = new List<ActionItem> { docs, stray };
        Assert.True(ActionTree.Repair(list));
        Assert.Equal(("Fichiers", "▣"), (stray.Group, stray.GroupIcon));
    }

    [Fact]
    public void ParentId_survives_a_save_and_old_files_have_none()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "actions.json");
        var docs = Item("Documents");
        ActionStore.Save([docs, Item("Photos", parent: docs)], path);
        var loaded = ActionStore.Load(path);
        Assert.Equal(docs.Id, loaded[1].ParentId);
        Assert.Null(loaded[0].ParentId);
    }
}
