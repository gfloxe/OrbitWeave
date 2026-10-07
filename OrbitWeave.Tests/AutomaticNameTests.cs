using OrbitWeave.Actions;

namespace OrbitWeave.Tests;

public class AutomaticNameTests
{
    private static List<ActionItem> One(string name, params ActionStep[] steps) =>
        [new ActionItem { Name = name, Steps = [.. steps] }];

    [Theory]
    [InlineData(@"C:\Users\a\Desktop\chatgpt.exe.lnk", "chatgpt")]
    [InlineData(@"C:\Jeux\WarThunder.lnk", "WarThunder")]
    [InlineData(@"C:\Jeux\Hades2.exe.lnk", "Hades2")]
    [InlineData(@"C:\Docs\rapport.pdf", "rapport.pdf")]
    public void A_new_button_takes_the_name_of_what_it_opens(string target, string expected)
    {
        var list = One(ActionEdits.NewButtonName);
        Assert.Equal(expected, ActionEdits.AddStep(list, list[0].Id, new OpenStep { Target = target })[0].Name);
    }

    [Fact]
    public void A_chosen_name_is_kept()
    {
        var list = One("Mon jeu");
        Assert.Equal("Mon jeu", ActionEdits.AddStep(list, list[0].Id, new OpenStep { Target = @"C:\a\b.exe" })[0].Name);
    }

    [Fact]
    public void The_installed_app_name_wins_when_given()
    {
        var list = One(ActionEdits.NewButtonName);
        var named = ActionEdits.AddStep(list, list[0].Id, new OpenStep { Target = @"shell:AppsFolder\OpenAI.ChatGPT_xyz!App" }, "ChatGPT");
        Assert.Equal("ChatGPT", named[0].Name);
    }

    [Fact]
    public void A_site_being_typed_waits_then_names_the_button()
    {
        var list = One(ActionEdits.NewButtonName);
        var typing = ActionEdits.AddStep(list, list[0].Id, new OpenStep { Target = "https://" });
        Assert.Equal(ActionEdits.NewButtonName, typing[0].Name);
        var done = ActionEdits.ReplaceStep(typing, list[0].Id, 0, new OpenStep { Target = "https://www.youtube.com/" });
        Assert.Equal("youtube.com", done[0].Name);
    }

    [Fact]
    public void A_file_dropped_on_a_new_button_names_it()
    {
        var list = One(ActionEdits.NewButtonName, new OpenStep { Target = "" });
        var dropped = new ActionStep[] { new OpenStep { Target = @"C:\Users\a\Desktop\Euro Truck Simulator 2.url" } };
        Assert.Equal("Euro Truck Simulator 2", DropEdits.Replace(list, list[0].Id, dropped)[0].Name);
        Assert.Equal("Euro Truck Simulator 2", DropEdits.Add(list, list[0].Id, dropped)[0].Name);
        var mine = One("Camion", new OpenStep { Target = @"C:\a\b.exe" });
        Assert.Equal("Camion", DropEdits.Replace(mine, mine[0].Id, dropped)[0].Name);
        Assert.Equal("Camion", DropEdits.Add(mine, mine[0].Id, dropped)[0].Name);
    }

    [Fact]
    public void Changing_the_target_follows_an_automatic_name_only()
    {
        var list = One("WarThunder", new OpenStep { Target = @"C:\Jeux\WarThunder.lnk" });
        Assert.Equal("Hades2", ActionEdits.ReplaceStep(list, list[0].Id, 0, new OpenStep { Target = @"C:\Jeux\Hades2.exe.lnk" })[0].Name);
        var mine = One("Avion", new OpenStep { Target = @"C:\Jeux\WarThunder.lnk" });
        Assert.Equal("Avion", ActionEdits.ReplaceStep(mine, mine[0].Id, 0, new OpenStep { Target = @"C:\Jeux\Hades2.exe.lnk" })[0].Name);
    }
}
