using OrbitWeave.Actions;
namespace OrbitWeave.Tests;

public class DropEditsTests
{
    private static readonly HashSet<string> Disk = new(StringComparer.OrdinalIgnoreCase)
        { @"C:\Apps\Discord.lnk", @"C:\Jeux", @"C:\Scripts\matin.ps1", @"C:\Docs\notes.txt" };

    private static List<ActionItem> Sample() =>
    [
        new() { Group = "Fichiers", GroupIcon = "▣", Name = "Explorateur", Steps = [new OpenStep { Target = "explorer.exe" }] },
        new() { Group = "Fichiers", GroupIcon = "▣", Name = "Matin", Steps = [new OpenStep { Target = @"C:\Apps\Discord.lnk" }] },
        new() { Group = "Fichiers", GroupIcon = "▣", Name = "Vide" },
        new() { Group = "Web", GroupIcon = "◎", Name = "YouTube", Steps = [new OpenStep { Target = "https://www.youtube.com" }] }
    ];

    [Fact]
    public void Dropped_items_become_steps_and_missing_ones_are_reported()
    {
        var (steps, missing) = DropEdits.StepsFor([@"C:\Apps\Discord.lnk", @"C:\Scripts\matin.ps1", "https://exemple.fr/page", @"C:\Absent.exe", @"C:\Jeux"], Disk.Contains);
        Assert.Equal(4, steps.Count);
        Assert.Equal(@"C:\Apps\Discord.lnk", Assert.IsType<OpenStep>(steps[0]).Target);
        Assert.Equal(@"C:\Scripts\matin.ps1", Assert.IsType<ScriptStep>(steps[1]).Path);
        Assert.Equal("https://exemple.fr/page", Assert.IsType<OpenStep>(steps[2]).Target);
        Assert.Equal(@"C:\Jeux", Assert.IsType<OpenStep>(steps[3]).Target);
        Assert.Equal([@"C:\Absent.exe"], missing);
    }

    [Fact]
    public void Replace_takes_the_new_name_only_when_the_old_one_was_automatic()
    {
        var list = Sample();
        list[0].UseSymbol = true;
        var (steps, _) = DropEdits.StepsFor([@"C:\Docs\notes.txt"], Disk.Contains);
        var automatic = DropEdits.Replace(list, list[0].Id, steps); // « Explorateur » ≠ « explorer » : nom choisi, gardé
        Assert.Equal("Explorateur", automatic[0].Name);
        Assert.Equal(@"C:\Docs\notes.txt", ((OpenStep)Assert.Single(automatic[0].Steps)).Target);
        Assert.False(automatic[0].UseSymbol);

        list[0].Name = "explorer";
        Assert.Equal("notes.txt", DropEdits.Replace(list, list[0].Id, steps)[0].Name);
    }

    [Fact]
    public void Add_appends_and_an_empty_button_takes_the_file_as_its_target()
    {
        var list = Sample();
        var (steps, _) = DropEdits.StepsFor([@"C:\Jeux"], Disk.Contains);
        var added = DropEdits.Add(list, list[1].Id, steps);
        Assert.Equal(2, added[1].Steps.Count);
        Assert.Equal("Matin", added[1].Name);
        var filled = DropEdits.Add(list, list[2].Id, steps);
        Assert.Equal("Vide", filled[2].Name); // nom choisi : gardé
        Assert.Equal(@"C:\Jeux", ((OpenStep)Assert.Single(filled[2].Steps)).Target);
        list[2].Name = "Nouvelle action";
        Assert.Equal("Jeux", DropEdits.Add(list, list[2].Id, steps)[2].Name);
    }

    [Fact]
    public void Create_adds_a_button_at_the_end_of_its_section()
    {
        var list = Sample();
        var (steps, _) = DropEdits.StepsFor([@"C:\Apps\Discord.lnk"], Disk.Contains);
        var (created, id, error) = DropEdits.Create(list, "fichiers", steps);
        Assert.Null(error);
        Assert.Equal(5, created.Count);
        Assert.Equal(id, created[3].Id);
        Assert.Equal(("Fichiers", "▣", "Discord"), (created[3].Group, created[3].GroupIcon, created[3].Name));
        Assert.Null(created[3].ParentId);
    }

    [Fact]
    public void Create_has_no_limit_per_section()
    {
        var list = Sample();
        list.Add(new ActionItem { Group = "Fichiers", Name = "4" });
        list.Add(new ActionItem { Group = "Fichiers", Name = "5" });
        var (steps, _) = DropEdits.StepsFor([@"C:\Jeux"], Disk.Contains);
        var (created, id, error) = DropEdits.Create(list, "Fichiers", steps);
        Assert.NotNull(id);
        Assert.Null(error);
        Assert.Equal(list.Count + 1, created.Count);
    }
}
