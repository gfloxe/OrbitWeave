using OrbitWeave.Actions;
namespace OrbitWeave.Tests;

public class InstalledAppsTests
{
    [Fact]
    public void Shortcuts_are_listed_by_name_without_duplicates_or_uninstallers()
    {
        var root = Directory.CreateTempSubdirectory("orbitweave-apps-").FullName;
        var sub = Directory.CreateDirectory(Path.Combine(root, "Discord Inc")).FullName;
        File.WriteAllText(Path.Combine(sub, "Discord.lnk"), "");
        File.WriteAllText(Path.Combine(root, "calculatrice.lnk"), "");
        File.WriteAllText(Path.Combine(root, "Désinstaller Truc.lnk"), "");
        File.WriteAllText(Path.Combine(root, "Uninstall Machin.lnk"), "");
        File.WriteAllText(Path.Combine(root, "notes.txt"), "");
        var other = Directory.CreateTempSubdirectory("orbitweave-apps2-").FullName;
        File.WriteAllText(Path.Combine(other, "Discord.lnk"), "");

        var apps = InstalledApps.Scan([root, other, Path.Combine(root, "absent")]);

        Assert.Equal(["calculatrice", "Discord"], apps.Select(a => a.Name));
        Assert.Equal(Path.Combine(sub, "Discord.lnk"), apps[1].Path);
    }

    [Fact]
    public void Store_apps_are_merged_without_duplicating_shortcuts()
    {
        var merged = InstalledApps.Merge([new InstalledApp("Discord", @"C:\Discord.lnk")],
            [("Calculatrice", "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"), ("Discord", "Discord!App"), ("Bloc-notes", @"C:\Windows\notepad.exe")]);
        Assert.Equal(["Calculatrice", "Discord"], merged.Select(a => a.Name));
        Assert.Equal(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", merged[0].Path);
    }
}
