using System.Windows;
using OrbitWeave.Orbit;

namespace OrbitWeave.Tests;

public class NextColumnTests
{
    private static readonly Rect[] Columns = [new Rect(100, 0, 160, 300), new Rect(284, 50, 160, 100)];

    [Fact]
    public void Right_branch_gets_its_new_column_after_the_outermost() =>
        Assert.Equal(444 + 24, CardGroups.NextColumnLeft(Columns, 1, 160, 24));

    [Fact]
    public void Left_branch_gets_its_new_column_before_the_outermost() =>
        Assert.Equal(100 - 24 - 160, CardGroups.NextColumnLeft(Columns, -1, 160, 24));

    [Fact]
    public void Vertical_branch_grows_to_the_right() =>
        Assert.Equal(444 + 24, CardGroups.NextColumnLeft(Columns, 0, 160, 24));
}
