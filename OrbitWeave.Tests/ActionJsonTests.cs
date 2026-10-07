using System.Text.Json;
using OrbitWeave;
using OrbitWeave.Actions;

namespace OrbitWeave.Tests;

public class ActionJsonTests
{
    private static ActionItem RoundTrip(ActionItem item) =>
        JsonSerializer.Deserialize<ActionItem>(JsonSerializer.Serialize(item, ActionJson.Options), ActionJson.Options)!;

    [Fact]
    public void Each_step_kind_survives_a_round_trip()
    {
        var item = new ActionItem
        {
            Name = "Mode travail",
            Steps =
            [
                new OpenStep { Target = "code.exe", Arguments = "--new-window", ReuseExisting = false },
                new WaitStep { Seconds = 1.5 },
                new ScriptStep { Source = ScriptSource.Commande, Code = "Get-Date", Shell = ScriptShell.PowerShell, Hidden = true, Admin = true, WaitForExit = true, Enabled = false }
            ]
        };

        var copy = RoundTrip(item);

        Assert.Equal(item.Id, copy.Id);
        var open = Assert.IsType<OpenStep>(copy.Steps[0]);
        Assert.Equal(("code.exe", "--new-window", false), (open.Target, open.Arguments, open.ReuseExisting));
        Assert.Equal(1.5, Assert.IsType<WaitStep>(copy.Steps[1]).Seconds);
        var script = Assert.IsType<ScriptStep>(copy.Steps[2]);
        Assert.Equal((ScriptSource.Commande, "Get-Date", ScriptShell.PowerShell), (script.Source, script.Code, script.Shell));
        Assert.True(script.Hidden && script.Admin && script.WaitForExit);
        Assert.False(script.Enabled);
        Assert.Equal(item.Steps[2].Id, script.Id);
    }

    [Fact]
    public void Steps_are_written_with_a_readable_kind()
    {
        var json = JsonSerializer.Serialize(new ActionItem { Steps = [new WaitStep { Seconds = 2 }] }, ActionJson.Options);
        Assert.Contains("\"Kind\": \"Attendre\"", json);
        Assert.DoesNotContain("\"Target\"", json);
        Assert.DoesNotContain("\"Type\"", json);
    }

    [Fact]
    public void Target_edits_the_first_open_step()
    {
        var item = new ActionItem { Steps = [new WaitStep(), new OpenStep { Target = "a.exe" }] };
        string? changed = null;
        item.PropertyChanged += (_, e) => changed = e.PropertyName;

        item.Target = "b.exe";

        Assert.Equal("b.exe", ((OpenStep)item.Steps[1]).Target);
        Assert.Equal("Target", changed);
    }

    [Fact]
    public void Target_on_an_empty_action_creates_an_open_step()
    {
        var item = new ActionItem { Target = "" };
        Assert.IsType<OpenStep>(Assert.Single(item.Steps));
    }

    [Fact]
    public void Wait_seconds_are_clamped()
    {
        Assert.Equal(0.1, new WaitStep { Seconds = 0 }.Seconds);
        Assert.Equal(3600, new WaitStep { Seconds = 99999 }.Seconds);
    }
}
