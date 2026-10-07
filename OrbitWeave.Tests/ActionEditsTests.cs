using OrbitWeave.Actions;
namespace OrbitWeave.Tests;

public class ActionEditsTests
{
    private static List<ActionItem> Sample() =>
    [
        new() { Name = "Matin", Steps = [new OpenStep { Target = "discord.exe" }, new WaitStep { Seconds = 2 }, new OpenStep { Target = "https://www.youtube.com/" }] },
        new() { Name = "Autre" }
    ];

    [Fact]
    public void Edits_return_a_new_list_and_leave_the_original_untouched()
    {
        var list = Sample();
        var renamed = ActionEdits.Rename(list, list[0].Id, "Soir");
        Assert.Equal("Soir", renamed[0].Name);
        Assert.Equal("Matin", list[0].Name);
        Assert.NotSame(list[0], renamed[0]);
        Assert.Equal(list[0].Id, renamed[0].Id);
    }

    [Fact]
    public void Steps_are_added_moved_disabled_replaced_and_removed()
    {
        var list = Sample();
        var id = list[0].Id;
        var added = ActionEdits.AddStep(list, id, new OpenStep { Target = "notepad.exe" });
        Assert.Equal(4, added[0].Steps.Count);
        var moved = ActionEdits.MoveStep(added, id, 3, 0);
        Assert.Equal("notepad.exe", ((OpenStep)moved[0].Steps[0]).Target);
        var disabled = ActionEdits.SetStepEnabled(moved, id, 1, false);
        Assert.False(disabled[0].Steps[1].Enabled);
        var replaced = ActionEdits.ReplaceStep(disabled, id, 2, new WaitStep { Seconds = 5 });
        Assert.Equal(5, ((WaitStep)replaced[0].Steps[2]).Seconds);
        var removed = ActionEdits.RemoveStep(replaced, id, 0);
        Assert.Equal(3, removed[0].Steps.Count);
        Assert.Equal("discord.exe", ((OpenStep)removed[0].Steps[0]).Target);
    }

    [Fact]
    public void Unknown_id_or_index_changes_nothing()
    {
        var list = Sample();
        Assert.Equal(3, ActionEdits.RemoveStep(list, list[0].Id, 9)[0].Steps.Count);
        Assert.Equal(3, ActionEdits.MoveStep(list, list[0].Id, 0, 7)[0].Steps.Count);
        Assert.Equal("Matin", ActionEdits.Rename(list, Guid.NewGuid(), "X")[0].Name);
    }

    [Theory]
    [InlineData("C:\\Users\\a\\Desktop\\Discord.lnk", "Discord")]
    [InlineData("calc.exe", "calc")]
    [InlineData("https://www.youtube.com/watch?v=1", "youtube.com")]
    [InlineData("C:\\Users\\a\\Documents", "Documents")]
    [InlineData("C:\\", "C:\\")]
    [InlineData("ms-settings:", "ms-settings:")]
    [InlineData(" ", "(vide)")]
    public void Open_steps_read_as_their_target(string target, string expected) =>
        Assert.Equal(expected, StepText.Describe(new OpenStep { Target = target }));

    [Fact]
    public void Waits_and_scripts_read_naturally()
    {
        Assert.Equal("Attendre 2 s", StepText.Describe(new WaitStep { Seconds = 2 }));
        Assert.Equal("Attendre 0,5 s", StepText.Describe(new WaitStep { Seconds = 0.5 }));
        Assert.Equal("Script · demarrage.ps1", StepText.Describe(new ScriptStep { Source = ScriptSource.Fichier, Path = "C:\\x\\demarrage.ps1" }));
        Assert.Equal("Commande · echo bonjour", StepText.Describe(new ScriptStep { Source = ScriptSource.Commande, Code = "echo bonjour\r\nexit" }));
    }

    [Fact]
    public void Choosing_a_symbol_pins_it_and_automatic_releases_it()
    {
        var list = Sample();
        var pinned = ActionEdits.SetIcon(list, list[0].Id, "★");
        Assert.True(pinned[0].UseSymbol);
        Assert.Equal("★", pinned[0].Icon);
        var auto = ActionEdits.UseAutomaticIcon(pinned, list[0].Id);
        Assert.False(auto[0].UseSymbol);
        Assert.Equal("★", auto[0].Icon);
    }

    [Fact]
    public void UseSymbol_is_only_written_when_true()
    {
        var list = Sample();
        Assert.DoesNotContain("UseSymbol", System.Text.Json.JsonSerializer.Serialize(list, ActionJson.Options));
        var pinned = ActionEdits.SetIcon(list, list[0].Id, "★");
        Assert.Contains("\"UseSymbol\": true", System.Text.Json.JsonSerializer.Serialize(pinned, ActionJson.Options));
    }

    [Fact]
    public void Store_apps_read_as_their_short_name() =>
        Assert.Equal("WindowsCalculator", StepText.Describe(new OpenStep { Target = @"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App" }));
}
