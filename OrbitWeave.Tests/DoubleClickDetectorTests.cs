using OrbitWeave.Orbit;

namespace OrbitWeave.Tests;

public class DoubleClickDetectorTests
{
    [Fact]
    public void Two_quick_presses_on_the_spot_make_a_double_click()
    {
        var detector = new DoubleClickDetector(500, 4, 4);
        Assert.False(detector.Press(1000, 100, 100));
        Assert.True(detector.Press(1300, 101, 99));
    }

    [Fact]
    public void Too_slow_or_too_far_is_not_a_double_click()
    {
        var detector = new DoubleClickDetector(500, 4, 4);
        Assert.False(detector.Press(1000, 100, 100));
        Assert.False(detector.Press(1600, 100, 100)); // trop lent
        Assert.False(detector.Press(1700, 110, 100)); // trop loin
    }

    [Fact]
    public void A_third_press_does_not_make_a_second_double_click()
    {
        var detector = new DoubleClickDetector(500, 4, 4);
        detector.Press(1000, 100, 100);
        Assert.True(detector.Press(1100, 100, 100));
        Assert.False(detector.Press(1200, 100, 100));
        Assert.True(detector.Press(1300, 100, 100));
    }

    [Fact]
    public void Tick_count_wrap_is_handled()
    {
        var detector = new DoubleClickDetector(500, 4, 4);
        detector.Press(uint.MaxValue - 100, 5, 5);
        Assert.True(detector.Press(200, 5, 5));
    }
}
