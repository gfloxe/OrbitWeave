using OrbitWeave.Actions;
using OrbitWeave.Orbit;
namespace OrbitWeave.Tests;

public class AppIconsTests
{
    [Fact]
    public void Cache_key_changes_with_the_file_date_and_ignores_case()
    {
        var a = AppIcons.CacheKey(new IconTarget(IconKind.Shell, @"C:\A.exe"), new DateTime(2026, 1, 1));
        Assert.Equal(a, AppIcons.CacheKey(new IconTarget(IconKind.Shell, @"c:\a.EXE"), new DateTime(2026, 1, 1)));
        Assert.NotEqual(a, AppIcons.CacheKey(new IconTarget(IconKind.Shell, @"C:\A.exe"), new DateTime(2026, 2, 1)));
        Assert.Matches("^[0-9a-f]{40}$", a);
    }

    [Fact]
    public async Task A_real_folder_gets_an_icon_and_a_missing_target_gets_none()
    {
        var folder = await AppIcons.LoadAsync(new IconTarget(IconKind.Shell, Environment.GetFolderPath(Environment.SpecialFolder.Windows)));
        Assert.NotNull(folder);
        Assert.True(folder!.IsFrozen);
        Assert.Null(await AppIcons.LoadAsync(new IconTarget(IconKind.Shell, @"C:\N'existe\pas.exe")));
        Assert.Null(await AppIcons.LoadAsync(new IconTarget(IconKind.None, "")));
    }
}
