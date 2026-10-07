using OrbitWeave.Settings;

namespace OrbitWeave.Tests;

public class HotkeyCaptureTests
{
    [Theory]
    [InlineData(true, true, false, false, "Space", true)]
    [InlineData(false, false, false, false, "A", true)]       // une seule touche : acceptée
    [InlineData(false, true, false, false, "F4", true)]
    [InlineData(false, false, false, true, "D1", true)]       // Win + 1
    [InlineData(false, false, false, false, "F13", true)]
    [InlineData(false, false, false, false, "NumPad5", true)]
    [InlineData(false, false, false, false, "Escape", false)] // Échap seul annule la saisie
    [InlineData(true, false, false, false, "Escape", true)]
    [InlineData(true, false, false, false, "LeftCtrl", false)] // touche de modification seule
    [InlineData(false, false, false, false, "LWin", false)]
    public void Any_key_or_combination_is_accepted(bool ctrl, bool alt, bool shift, bool win, string key, bool expected) =>
        Assert.Equal(expected, HotkeyCaptureRules.IsUsable(ctrl, alt, shift, win, key));

    [Theory]
    [InlineData(false, false, false, false, "A", true)]
    [InlineData(false, false, true, false, "A", true)]   // Maj + A : c'est un A majuscule
    [InlineData(false, false, false, false, "D5", true)]
    [InlineData(false, false, false, false, "Space", true)]
    [InlineData(false, false, false, false, "F9", false)]
    [InlineData(false, false, false, false, "MediaPlayPause", false)]
    [InlineData(true, false, false, false, "A", false)]
    [InlineData(false, false, false, true, "A", false)]
    public void A_typing_key_alone_is_flagged(bool ctrl, bool alt, bool shift, bool win, string key, bool expected) =>
        Assert.Equal(expected, HotkeyCaptureRules.BlocksTyping(ctrl, alt, shift, win, key));

    [Fact]
    public void Combination_is_displayed_in_french()
    {
        Assert.Equal("Ctrl + Alt + Espace", HotkeyCaptureRules.Display(new HotkeySettings()));
        Assert.Equal("Win + 5", HotkeyCaptureRules.Display(new HotkeySettings { Control = false, Alt = false, Win = true, Key = "D5" }));
        Assert.Equal("Pavé 0", HotkeyCaptureRules.Display(new HotkeySettings { Control = false, Alt = false, Key = "NumPad0" }));
    }
}
