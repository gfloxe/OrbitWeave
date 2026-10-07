using OrbitWeave.Actions;
namespace OrbitWeave.Tests;

public class IconTargetTests
{
    private static readonly HashSet<string> Disk = new(StringComparer.OrdinalIgnoreCase)
        { @"C:\Apps\Discord.lnk", @"C:\Users\a\Documents", @"C:\Windows\explorer.exe" };

    private static IconTarget Resolve(string target) => IconTarget.Resolve(target, Disk.Contains,
        name => name.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase) ? @"C:\Windows\explorer.exe" : null);

    [Theory]
    [InlineData(@"C:\Apps\Discord.lnk", IconKind.Shell, @"C:\Apps\Discord.lnk")]
    [InlineData(@"C:\Users\a\Documents\", IconKind.Shell, @"C:\Users\a\Documents")]
    [InlineData("explorer.exe", IconKind.Shell, @"C:\Windows\explorer.exe")]
    [InlineData("explorer", IconKind.Shell, @"C:\Windows\explorer.exe")]
    [InlineData("https://www.youtube.com/watch?v=1", IconKind.Site, "https://www.youtube.com/")]
    [InlineData("http://exemple.fr", IconKind.Site, "https://exemple.fr/")]
    [InlineData(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", IconKind.Shell, @"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App")]
    [InlineData("ms-settings:", IconKind.None, "")]
    [InlineData(@"C:\Absent\truc.exe", IconKind.None, "")]
    [InlineData("inconnu.exe", IconKind.None, "")]
    [InlineData("  ", IconKind.None, "")]
    public void Targets_resolve_to_what_should_be_drawn(string target, IconKind kind, string value) =>
        Assert.Equal(new IconTarget(kind, value), Resolve(target));

    [Fact]
    public void Environment_variables_are_expanded()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        Assert.Equal(new IconTarget(IconKind.Shell, windows), IconTarget.Resolve("%WINDIR%", Directory.Exists, _ => null));
    }

    [Fact]
    public void A_chosen_symbol_or_no_open_step_means_no_icon()
    {
        var chosen = new ActionItem { UseSymbol = true, Steps = [new OpenStep { Target = "https://a.fr" }] };
        var wait = new ActionItem { Steps = [new WaitStep()] };
        Assert.Equal(IconKind.None, IconTarget.For(chosen).Kind);
        Assert.Equal(IconKind.None, IconTarget.For(wait).Kind);
        Assert.Equal(IconKind.Site, IconTarget.For(new ActionItem { Steps = [new OpenStep { Target = "https://a.fr" }] }).Kind);
    }
}
