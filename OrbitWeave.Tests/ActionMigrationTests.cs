using OrbitWeave;
using OrbitWeave.Actions;

namespace OrbitWeave.Tests;

public class ActionMigrationTests
{
    private const string Legacy = """
        [
          { "Group": "Fichiers", "GroupIcon": "▣", "Name": "Explorateur", "Icon": "▣", "Type": "Application", "Target": "explorer.exe" },
          { "Group": "Web", "GroupIcon": "◎", "Name": "YouTube", "Icon": "▶", "Type": "Site", "Target": "https://www.youtube.com",
            "BadgeCount": 0, "GroupVisible": true, "GroupType": "Ordinaire" }
        ]
        """;

    [Fact]
    public void Legacy_actions_become_one_open_step()
    {
        var (actions, changed) = ActionMigration.Read(Legacy);

        Assert.True(changed);
        Assert.Equal(2, actions.Count);
        Assert.Equal("explorer.exe", Assert.IsType<OpenStep>(Assert.Single(actions[0].Steps)).Target);
        Assert.Equal("https://www.youtube.com", actions[1].Target);
        Assert.Equal("Site", actions[1].Type);
        Assert.Equal("Fichiers", actions[0].GroupType);
        Assert.Equal("YouTube", actions[1].Name);
        Assert.NotEqual(actions[0].Id, actions[1].Id);
    }

    [Fact]
    public void Current_format_is_read_without_changes()
    {
        var (first, _) = ActionMigration.Read(Legacy);
        var json = System.Text.Json.JsonSerializer.Serialize(first, ActionJson.Options);

        var (again, changed) = ActionMigration.Read(json);

        Assert.False(changed);
        Assert.Equal(first.Select(a => a.Id), again.Select(a => a.Id));
        Assert.Equal("Application", again[0].Type);
    }

    [Theory]
    [InlineData("https://www.google.com", "Site")]
    [InlineData("ms-settings:", "Site")]
    [InlineData("calc.exe", "Application")]
    [InlineData(@"C:\Windows", "Dossier")]
    [InlineData(@"C:\nexistepas\app.lnk", "Application")]
    public void Type_is_inferred_from_the_target(string target, string expected) =>
        Assert.Equal(expected, ActionMigration.InferType(target));

    [Fact]
    public void Load_backs_up_once_and_rewrites_the_file()
    {
        var dir = Directory.CreateTempSubdirectory("orbitweave-").FullName;
        var path = Path.Combine(dir, "actions.json");
        File.WriteAllText(path, Legacy);

        var loaded = ActionStore.Load(path);

        Assert.Equal(Legacy, File.ReadAllText(Path.Combine(dir, "actions.backup.json")));
        Assert.Contains("\"Steps\"", File.ReadAllText(path));
        File.WriteAllText(path, Legacy);
        ActionStore.Load(path);
        Assert.Equal(Legacy, File.ReadAllText(Path.Combine(dir, "actions.backup.json")));
        Assert.Equal(2, loaded.Count);
    }

    [Fact]
    public void Broken_file_is_set_aside_and_defaults_are_used()
    {
        var dir = Directory.CreateTempSubdirectory("orbitweave-").FullName;
        var path = Path.Combine(dir, "actions.json");
        File.WriteAllText(path, "{ pas du json");

        var loaded = ActionStore.Load(path);

        Assert.NotEmpty(loaded);
        Assert.Single(Directory.GetFiles(dir, "actions.broken-*.json"));
        Assert.Equal("{ pas du json", File.ReadAllText(path));
    }
}
