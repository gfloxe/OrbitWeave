using OrbitWeave.Ui;

namespace OrbitWeave.Tests;

public class UiResolutionTests
{
    [Theory]
    [InlineData(1365, UiResolutionBands.Small)]
    [InlineData(1366, UiResolutionBands.Medium)]
    [InlineData(1919, UiResolutionBands.Medium)]
    [InlineData(1920, UiResolutionBands.Large)]
    [InlineData(2559, UiResolutionBands.Large)]
    [InlineData(2560, UiResolutionBands.ExtraLarge)]
    public void Width_matches_expected_band(int width, string band) =>
        Assert.Equal(band, UiResolutionBands.ForWidth(width));

    [Fact]
    public void Resolution_override_only_applies_in_its_band_and_is_undoable()
    {
        var folder = Directory.CreateTempSubdirectory("orbitweave-resolution-").FullName;
        var store = new UiProfileStore(folder);
        var profile = new UiProfile { Name = "Test" };
        profile.Elements["wheel.card"] = new() { ["Width"] = "120" };
        var session = new UiEditSession(store, profile);
        session.SetResolution(UiResolutionBands.Large, "wheel.card", "Width", "150");
        Assert.Equal("120", UiOverrides.Resolve(profile, "wheel.card:one", screenWidth: 1600)["Width"]);
        Assert.Equal("150", UiOverrides.Resolve(profile, "wheel.card:one", screenWidth: 1920)["Width"]);
        session.Save();
        Assert.Equal("150", store.Load("Test", out _).Resolutions[UiResolutionBands.Large]["wheel.card"]["Width"]);
        session.Undo();
        Assert.Equal("120", UiOverrides.Resolve(profile, "wheel.card:one", screenWidth: 1920)["Width"]);
    }
}
