using OrbitWeave.Ui;

namespace OrbitWeave.Tests;

public class UiProfileStoreTests
{
    private static UiProfileStore NewStore() => new(Directory.CreateTempSubdirectory("orbitweave-ui-").FullName);

    [Fact]
    public void Origin_comes_first_then_saved_profiles()
    {
        var store = NewStore();
        store.Save(new UiProfile { Name = "Snapse" });
        store.Save(new UiProfile { Name = "Alpha" });
        Assert.Equal(new[] { "Origine", "Alpha", "Snapse" }, store.Names());
    }

    [Fact]
    public void Profiles_round_trip()
    {
        var store = NewStore();
        var profile = new UiProfile { Name = "Snapse" };
        profile.Tokens["HubDiameter"] = "80";
        profile.Themes["Sombre"] = new() { ["GlassBaseBrush"] = "#FF101010" };
        profile.Elements["wheel.section:Web"] = new() { ["OffsetX"] = "12" };
        store.Save(profile);

        var loaded = store.Load("Snapse", out var problem);

        Assert.Null(problem);
        Assert.Equal("80", loaded.Tokens["HubDiameter"]);
        Assert.Equal("#FF101010", loaded.Themes["Sombre"]["GlassBaseBrush"]);
        Assert.Equal("12", loaded.Elements["wheel.section:Web"]["OffsetX"]);
    }

    [Fact]
    public void State_values_round_trip_and_legacy_profiles_still_load()
    {
        var store = NewStore();
        var profile = new UiProfile { Name = "États" };
        profile.States["wheel.section:Web"] = new() { ["Hover"] = new() { ["Scale"] = "1.2" } };
        store.Save(profile);
        Assert.Equal("1.2", store.Load("États", out _).States["wheel.section:Web"]["Hover"]["Scale"]);

        var legacy = UiProfileStore.Parse("{\"Schema\":1,\"Name\":\"Ancien\",\"Tokens\":{},\"Themes\":{},\"Elements\":{}}");
        Assert.Empty(legacy.States);
    }

    [Fact]
    public void Origin_is_read_only() =>
        Assert.Throws<UiProfileException>(() => NewStore().Save(new UiProfile()));

    [Fact]
    public void A_broken_profile_falls_back_to_origin_and_is_set_aside()
    {
        var store = NewStore();
        File.WriteAllText(Path.Combine(store.Folder, "Cassé.json"), "{ pas du json");

        var loaded = store.Load("Cassé", out var problem);

        Assert.Equal("Origine", loaded.Name);
        Assert.Equal("Le profil « Cassé » est illisible ; l'interface d'origine est utilisée.", problem);
        Assert.Single(Directory.GetFiles(store.Folder, "Cassé.broken-*.json"));
        Assert.DoesNotContain(store.Names(), n => n.Contains("broken"));
    }

    [Theory]
    [InlineData("pas du json")]
    [InlineData("{\"Schema\": 99}")]
    [InlineData("[]")]
    public void Import_rejects_invalid_files(string content)
    {
        var store = NewStore();
        var file = Path.Combine(Directory.CreateTempSubdirectory().FullName, "x.owui.json");
        File.WriteAllText(file, content);
        var error = Assert.Throws<UiProfileException>(() => store.Import(file));
        Assert.Equal("Ce fichier n'est pas un profil OrbitWeave valide.", error.Message);
    }

    [Fact]
    public void Import_never_overwrites_an_existing_profile()
    {
        var store = NewStore();
        store.Save(new UiProfile { Name = "Snapse" });
        var file = Path.Combine(Directory.CreateTempSubdirectory().FullName, "s.owui.json");
        store.Export(new UiProfile { Name = "Snapse" }, file);

        Assert.Equal("Snapse (2)", store.Import(file).Name);
        Assert.Equal("Origine (2)", store.UniqueName("Origine"));
    }

    [Fact]
    public void Names_are_cleaned_for_the_file_system() =>
        Assert.Equal("Mon profil", UiProfileStore.CleanName("  Mon/ profil?* "));
}
