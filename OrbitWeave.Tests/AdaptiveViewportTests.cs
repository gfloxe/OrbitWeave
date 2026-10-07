using System.Windows;
using OrbitWeave.Orbit;

namespace OrbitWeave.Tests;

public class AdaptiveViewportTests
{
    [Fact]
    public void Origin_keeps_exactly_the_old_viewport() =>
        Assert.Equal(new Rect(30, 120, 1060, 880), AdaptiveViewport.Fit(new Rect(-200, 100, 1500, 900),
            new Rect(-400, 20, 1920, 1080), new Rect(30, 120, 1060, 880), false));

    [Fact]
    public void Personalized_content_gets_margin_and_is_bounded_by_screen()
    {
        var screen = new Rect(-400, 20, 1920, 1080);
        var result = AdaptiveViewport.Fit(new Rect(-390, 30, 1250, 800), screen, new Rect(), true);
        Assert.True(screen.Contains(result));
        Assert.True(result.Contains(new Rect(-390, 30, 1250, 800)));
        Assert.Equal(-400, result.Left);
        Assert.Equal(20, result.Top);
    }

    [Theory]
    [InlineData(-1000, -1000, -20, -30)]
    [InlineData(1000, 1000, 188, 98)]
    [InlineData(12, 13, 12, 13)]
    public void Translation_keeps_the_complete_element_visible(double dx, double dy, double x, double y) =>
        Assert.Equal(new Vector(x, y), AdaptiveViewport.LimitTranslation(new Rect(20, 30, 160, 68),
            new Rect(0, 0, 368, 196), new Vector(dx, dy)));

    [Fact]
    public void A_small_custom_layout_is_not_shrunk_like_origin()
    {
        var content = new Rect(400, 300, 500, 350);
        Assert.Equal(1, AdaptiveViewport.ScaleToFit(content, new Rect(0, 0, 1366, 728), 0.79, true));
        Assert.Equal(0.79, AdaptiveViewport.ScaleToFit(content, new Rect(0, 0, 1366, 728), 0.79, false));
    }

    [Fact]
    public void An_oversized_custom_layout_is_shrunk() =>
        Assert.InRange(AdaptiveViewport.ScaleToFit(new Rect(-1000, -1000, 3000, 1800),
            new Rect(0, 0, 1366, 728), 0.79, true), 0.38, 0.40);
}
