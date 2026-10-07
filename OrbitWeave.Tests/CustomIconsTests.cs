using OrbitWeave.Actions;

namespace OrbitWeave.Tests;

public class CustomIconsTests
{
    [Fact]
    public void An_image_is_copied_into_the_data_folder()
    {
        var data = Directory.CreateTempSubdirectory("ow-icons").FullName;
        var source = Path.Combine(data, "Logo.PNG");
        File.WriteAllBytes(source, [1, 2, 3]);
        var relative = CustomIcons.Import(source, data);
        Assert.StartsWith("icons", relative);
        Assert.EndsWith(".png", relative);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(data, relative)));
        Assert.Equal(new IconTarget(IconKind.Image, Path.Combine(data, relative)), IconTarget.Custom(relative, data, File.Exists));
    }

    [Fact]
    public void The_library_lists_finds_users_and_deletes()
    {
        var data = Directory.CreateTempSubdirectory("ow-icons").FullName;
        var source = Path.Combine(data, "a.png");
        File.WriteAllBytes(source, [1]);
        var first = CustomIcons.Import(source, data);
        var second = CustomIcons.Import(source, data);
        File.WriteAllText(Path.Combine(data, CustomIcons.Folder, "notes.txt"), "pas une image");
        Assert.Equal(2, CustomIcons.List(data).Count);

        var actions = new List<ActionItem> { new() { Name = "Jeu", IconFile = first }, new() { Name = "Autre", IconFile = second } };
        Assert.Equal(["Jeu"], CustomIcons.UsedBy(actions, first));

        var after = CustomIcons.Delete(actions, data, first);
        Assert.Null(after[0].IconFile);
        Assert.Equal(second, after[1].IconFile);
        Assert.False(File.Exists(Path.Combine(data, first)));
        Assert.Equal([second], CustomIcons.List(data));
    }

    [Fact]
    public void A_non_image_is_refused()
    {
        var data = Directory.CreateTempSubdirectory("ow-icons").FullName;
        Assert.Throws<ArgumentException>(() => CustomIcons.Import(Path.Combine(data, "a.exe"), data));
    }

    [Fact]
    public void A_missing_image_falls_back_to_nothing() =>
        Assert.Equal(IconKind.None, IconTarget.Custom(@"icons\absent.png", @"C:\Data", _ => false).Kind);

    [Fact]
    public void Another_file_gives_its_own_icon()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var explorer = Path.Combine(windows, "explorer.exe");
        Assert.Equal(new IconTarget(IconKind.Shell, explorer), IconTarget.Custom(explorer, @"C:\Data", File.Exists));
    }

    [Fact]
    public void The_chosen_icon_wins_over_the_target_until_a_symbol_or_auto_is_chosen()
    {
        var actions = new List<ActionItem> { new() { Steps = [new OpenStep { Target = "https://a.fr" }] } };
        var id = actions[0].Id;
        var chosen = ActionEdits.SetIconFile(actions, id, @"C:\Windows\explorer.exe");
        Assert.Equal(IconKind.Shell, IconTarget.For(chosen[0]).Kind);
        Assert.Null(ActionEdits.UseAutomaticIcon(chosen, id)[0].IconFile);
        Assert.Null(ActionEdits.SetIcon(chosen, id, "★")[0].IconFile);
    }
}
