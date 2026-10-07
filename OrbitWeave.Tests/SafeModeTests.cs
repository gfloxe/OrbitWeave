using OrbitWeave.Ui;

namespace OrbitWeave.Tests;

public class SafeModeTests
{
    private static string Folder() => Directory.CreateTempSubdirectory("orbitweave-safe-").FullName;

    [Fact]
    public void Argument_or_shift_starts_in_safe_mode()
    {
        Assert.True(SafeMode.ShouldStart(["--safe-mode"], false, Folder()));
        Assert.True(SafeMode.ShouldStart([], true, Folder()));
        Assert.False(SafeMode.ShouldStart([], false, Folder()));
    }

    [Fact]
    public void Three_unfinished_starts_trigger_safe_mode()
    {
        var folder = Folder();
        SafeMode.MarkStarting(folder);
        SafeMode.MarkStarting(folder);
        Assert.False(SafeMode.ShouldStart([], false, folder));
        SafeMode.MarkStarting(folder);
        Assert.True(SafeMode.ShouldStart([], false, folder));
        SafeMode.MarkStarted(folder);
        Assert.False(SafeMode.ShouldStart([], false, folder));
    }

    [Fact]
    public void Marking_started_without_a_pending_start_is_harmless() => SafeMode.MarkStarted(Folder());
}
