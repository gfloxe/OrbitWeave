using OrbitWeave.Orbit;

namespace OrbitWeave.Tests;

public class SubBranchLayoutTests
{
    private static readonly BranchBox Screen = new(0, 0, 1000, 800);
    private static readonly BranchBox Parent = new(400, 300, 200, 50);

    [Fact]
    public void Goes_to_the_preferred_side_centered_on_the_parent()
    {
        var plan = SubBranchLayout.Place(Parent, 3, 200, 50, 60, 24, 1, Screen, []);
        Assert.Equal((624d, 265d, 1), (plan.Left, plan.FirstY, plan.Side));
    }

    [Fact]
    public void Flips_when_the_preferred_side_leaves_the_screen()
    {
        var plan = SubBranchLayout.Place(Parent with { Left = 760 }, 2, 200, 50, 60, 24, 1, Screen, []);
        Assert.Equal(-1, plan.Side);
        Assert.Equal(536d, plan.Left);
    }

    [Fact]
    public void Flips_when_the_preferred_side_covers_an_open_branch()
    {
        var plan = SubBranchLayout.Place(Parent, 1, 200, 50, 60, 24, 1, Screen, [new BranchBox(650, 300, 200, 50)]);
        Assert.Equal(-1, plan.Side);
    }

    [Fact]
    public void Is_pushed_back_inside_vertically()
    {
        var plan = SubBranchLayout.Place(Parent with { Top = 740 }, 5, 200, 50, 60, 24, 1, Screen, []);
        Assert.Equal(800 - 25 - 4 * 60, plan.FirstY);
    }
}
