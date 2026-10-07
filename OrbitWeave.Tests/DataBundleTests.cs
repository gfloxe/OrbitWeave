using OrbitWeave.Data;
using OrbitWeave.Ui;

namespace OrbitWeave.Tests;

public class DataBundleTests
{
    private static string NewDataDir() => Directory.CreateTempSubdirectory("orbitweave-data-").FullName;

    private static string Seeded(string theme, string section, string profileName)
    {
        var dir = NewDataDir();
        ActionStore.Save([new ActionItem { Group = section, GroupIcon = "✦", Name = "Bloc-notes", Icon = "≡", Type = "Application", Target = "notepad.exe" }],
            Path.Combine(dir, "actions.json"));
        var profile = new UiProfile { Name = profileName };
        profile.Tokens["HubDiameter"] = "88";
        new UiProfileStore(Path.Combine(dir, "ui", "profiles")).Save(profile);
        var settings = HotkeySettings.Defaults();
        settings.Theme = theme;
        settings.SetGlassOpacity(0.7);
        settings.InterfaceProfile = profileName;
        settings.Save(Path.Combine(dir, "settings.json"));
        return dir;
    }

    [Fact]
    public void Export_then_import_restores_buttons_interface_and_settings_with_a_backup()
    {
        var source = Seeded("Sombre", "Travail", "Mon interface");
        var file = Path.Combine(NewDataDir(), "export" + DataBundle.Extension);
        DataBundle.Export(source, file);

        var target = Seeded("Clair", "Jeux", "Autre");
        var backup = DataBundle.Import(target, file);

        var actions = ActionStore.Load(Path.Combine(target, "actions.json"));
        Assert.Equal("Travail", Assert.Single(actions).Group);
        var settings = HotkeySettings.Load(Path.Combine(target, "settings.json"));
        Assert.Equal("Sombre", settings.Theme);
        Assert.Equal(0.7, settings.GlassOpacity, 3);
        Assert.Equal("Mon interface", settings.InterfaceProfile);
        var profile = new UiProfileStore(Path.Combine(target, "ui", "profiles")).Load("Mon interface", out var problem);
        Assert.Null(problem);
        Assert.Equal("88", profile.Tokens["HubDiameter"]);

        // L'existant est copié avant d'être remplacé.
        Assert.StartsWith(Path.Combine(target, "Sauvegardes", "avant-import-"), backup);
        Assert.Contains("Jeux", File.ReadAllText(Path.Combine(backup, "actions.json")));
        Assert.Contains("Clair", File.ReadAllText(Path.Combine(backup, "settings.json")));
        Assert.True(File.Exists(Path.Combine(backup, "ui", "profiles", "Autre.json")));
    }

    [Fact]
    public void Origin_interface_is_exported_without_a_profile()
    {
        var source = Seeded("Verre", "Travail", "Mon interface");
        var settings = HotkeySettings.Load(Path.Combine(source, "settings.json"));
        settings.InterfaceProfile = UiProfileStore.OriginName;
        settings.Save(Path.Combine(source, "settings.json"));
        var file = Path.Combine(NewDataDir(), "export" + DataBundle.Extension);
        DataBundle.Export(source, file);

        var target = Seeded("Clair", "Jeux", "Autre");
        DataBundle.Import(target, file);

        Assert.Equal(UiProfileStore.OriginName, HotkeySettings.Load(Path.Combine(target, "settings.json")).InterfaceProfile);
    }

    [Theory]
    [InlineData("pas du json")]
    [InlineData("""{"Kind":"Autre","Actions":[],"Settings":{}}""")]
    [InlineData("""{"Kind":"OrbitWeave","Actions":[],"Settings":{},"Interface":{"Schema":999}}""")]
    public void Unreadable_file_changes_nothing(string content)
    {
        var target = Seeded("Clair", "Jeux", "Autre");
        var before = Directory.GetFiles(target, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllText);
        var file = Path.Combine(NewDataDir(), "abime" + DataBundle.Extension);
        File.WriteAllText(file, content);

        Assert.Throws<DataBundleException>(() => DataBundle.Import(target, file));

        var after = Directory.GetFiles(target, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllText);
        Assert.Equal(before, after);
    }
}
