using OrbitWeave.Ui.Editor;

namespace OrbitWeave.Tests;

public class WheelEditingRulesTests
{
    [Theory]
    [InlineData(false, "wheel.section:Web", false)]
    [InlineData(false, null, false)]
    [InlineData(true, null, false)]
    [InlineData(true, "wheel.section:Web", true)]
    public void Selection_is_only_drawn_while_editing(bool editing, string? selection, bool expected) =>
        Assert.Equal(expected, WheelEditingRules.DrawSelection(editing, selection));
}
