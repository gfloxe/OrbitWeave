using OrbitWeave.Updates;

namespace OrbitWeave.Tests;

public class ReleaseInfoTests
{
    private const string Latest = """
        {
          "tag_name": "v1.2.0",
          "assets": [
            { "name": "notes.txt", "browser_download_url": "https://example.com/notes.txt" },
            { "name": "OrbitWeave-1.2.0-Setup.exe", "browser_download_url": "https://example.com/OrbitWeave-1.2.0-Setup.exe",
              "digest": "sha256:ABCDEF" }
          ]
        }
        """;

    [Fact]
    public void The_installer_and_its_version_are_read()
    {
        var release = ReleaseInfo.Parse(Latest);
        Assert.NotNull(release);
        Assert.Equal(new Version(1, 2, 0), release.Version);
        Assert.Equal("https://example.com/OrbitWeave-1.2.0-Setup.exe", release.SetupUrl);
        Assert.Equal("ABCDEF", release.Sha256);
    }

    [Theory]
    [InlineData("""{ "tag_name": "v1.0.0", "assets": [] }""")]
    [InlineData("""{ "tag_name": "pas-une-version", "assets": [] }""")]
    [InlineData("""{ "message": "Not Found" }""")]
    [InlineData("pas du json")]
    public void A_release_without_installer_is_ignored(string json) => Assert.Null(ReleaseInfo.Parse(json));

    [Theory]
    [InlineData("1.0.1", "1.0.0", true)]
    [InlineData("1.1", "1.0.9", true)]
    [InlineData("1.0.0", "1.0.0.0", false)]
    [InlineData("1.0.0", "1.2.0", false)]
    public void Only_a_higher_version_is_newer(string latest, string current, bool expected) =>
        Assert.Equal(expected, ReleaseInfo.IsNewer(Version.Parse(latest), Version.Parse(current)));
}
