namespace OrbitWeave.Tests;

public class LaunchPolicyTests
{
    [Theory]
    [InlineData("C:/OrbitWeave/bin/Release/net10.0-windows/OrbitWeave.exe", true)]
    [InlineData("C:/OrbitWeave/build-ui/OrbitWeave.exe", false)]
    [InlineData("C:/OrbitWeave/bin/Debug/net10.0-windows/OrbitWeave.exe", false)]
    public void Only_the_release_install_is_a_user_executable(string path, bool expected) =>
        Assert.Equal(expected, LaunchPolicy.CanLaunch(path, false, null));

    [Fact]
    public void The_installed_copy_is_a_user_executable()
    {
        var folder = Directory.CreateTempSubdirectory("orbitweave-install-").FullName;
        try
        {
            var exe = Path.Combine(folder, "OrbitWeave.exe");
            Assert.False(LaunchPolicy.CanLaunch(exe, false, null));
            File.WriteAllText(Path.Combine(folder, "unins000.exe"), "");
            Assert.True(LaunchPolicy.CanLaunch(exe, false, null));
            Assert.True(LaunchPolicy.IsInstalledCopy(exe));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void Test_builds_require_isolated_data()
    {
        var exe = "C:/OrbitWeave/build-ui/OrbitWeave.exe";
        Assert.False(LaunchPolicy.CanLaunch(exe, true, null));
        Assert.False(LaunchPolicy.CanLaunch(exe, true, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)));
        Assert.True(LaunchPolicy.CanLaunch(exe, true, Path.Combine(Path.GetTempPath(), "orbitweave-launch-test")));
    }
}
