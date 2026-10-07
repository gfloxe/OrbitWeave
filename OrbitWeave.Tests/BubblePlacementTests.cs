using System.Windows;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using OrbitWeave.Ui.Editor;
namespace OrbitWeave.Tests;

public class BubblePlacementTests
{
    private static readonly Rect Work = new(0, 0, 1920, 1040);

    [Fact]
    public void Goes_right_of_the_card_when_there_is_room() =>
        Assert.Equal(new Point(912, 400), BubblePlacement.Place(new Rect(700, 400, 200, 60), new Size(320, 300), Work));

    [Fact]
    public void Goes_left_when_the_right_side_is_too_narrow() =>
        Assert.Equal(new Point(1348, 400), BubblePlacement.Place(new Rect(1680, 400, 200, 60), new Size(320, 300), Work));

    [Fact]
    public void Stays_inside_the_work_area_vertically() =>
        Assert.Equal(new Point(912, 740), BubblePlacement.Place(new Rect(700, 1000, 200, 60), new Size(320, 300), Work));

    [Fact]
    public void Goes_left_when_the_right_side_would_cover_the_editor() =>
        Assert.Equal(new Point(368, 400), BubblePlacement.Place(new Rect(700, 400, 200, 60), new Size(320, 300), Work,
            avoid: new Rect(1080, 0, 840, 600)));

    [Fact]
    public void Covers_the_editor_rather_than_leaving_the_screen() =>
        Assert.Equal(new Point(312, 400), BubblePlacement.Place(new Rect(100, 400, 200, 60), new Size(320, 300), Work,
            avoid: new Rect(300, 0, 400, 1000)));
}
