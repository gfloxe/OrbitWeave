using OrbitWeave.Settings;

namespace OrbitWeave.Tests;

public class StartupRegistrationTests
{
    private const string Exe = @"C:\Users\a\AppData\Local\Programs\OrbitWeave\OrbitWeave.exe";

    [Fact]
    public void The_command_written_launches_this_copy() =>
        Assert.True(StartupRegistration.Launches(StartupRegistration.Command(Exe), Exe));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"""C:\Users\a\Desktop\OrbitWeave\bin\Release\net10.0-windows\OrbitWeave.exe""")]
    public void Another_or_no_command_is_not_this_copy(string? value) => Assert.False(StartupRegistration.Launches(value, Exe));

    [Fact]
    public void Case_and_quotes_do_not_matter() =>
        Assert.True(StartupRegistration.Launches(Exe.ToUpperInvariant(), Exe));
}
