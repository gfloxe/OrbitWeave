using OrbitWeave.Actions;

namespace OrbitWeave.Tests;

public class ButtonCatalogTests
{
    [Fact]
    public void Original_buttons_include_the_task_manager()
    {
        Assert.Contains(ButtonCatalog.Originals(), e => e.Target == "taskmgr.exe");
        Assert.Contains(ButtonCatalog.WindowsTools, e => e.Name == "Gestionnaire des tâches");
    }

    [Fact]
    public void Catalog_has_no_duplicate_target()
    {
        Assert.Equal(ButtonCatalog.WindowsTools.Count, ButtonCatalog.WindowsTools.Select(e => e.Target.ToLowerInvariant()).Distinct().Count());
        var originals = ButtonCatalog.Originals();
        Assert.Equal(originals.Count, originals.Select(e => e.Target.ToLowerInvariant()).Distinct().Count());
    }

    [Fact]
    public void Adding_an_entry_creates_a_button_at_the_end_of_the_section()
    {
        var actions = ActionStore.Defaults();
        var entry = new CatalogEntry("Gestionnaire des tâches", "▤", "taskmgr.exe");
        var (list, id, error) = ButtonCatalog.Add(actions, "Outils", entry);
        Assert.Null(error);
        var added = Assert.Single(list, a => a.Id == id);
        Assert.Equal("Outils", added.Group);
        Assert.Equal("Gestionnaire des tâches", added.Name);
        Assert.False(added.UseSymbol);
        Assert.Equal("taskmgr.exe", Assert.IsType<OpenStep>(Assert.Single(added.Steps)).Target);
        Assert.Same(added, list.Last(a => a.Group == "Outils"));
        Assert.Equal(actions.Count + 1, list.Count);
    }

    [Theory]
    [InlineData("Éditeur", "E")]
    [InlineData("discord", "D")]
    [InlineData("7-Zip", "#")]
    [InlineData("", "#")]
    public void Apps_are_grouped_by_plain_initial(string name, string expected) => Assert.Equal(expected, ButtonCatalog.Initial(name));

    [Fact]
    public void IsIn_matches_the_target_in_that_section_only()
    {
        var actions = ActionStore.Defaults();
        Assert.True(ButtonCatalog.IsIn(actions, "Windows", "TASKMGR.exe "));
        Assert.False(ButtonCatalog.IsIn(actions, "Outils", "taskmgr.exe"));
    }
}
