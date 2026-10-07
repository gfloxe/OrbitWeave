using OrbitWeave.Orbit;

namespace OrbitWeave.Tests;

public class WheelLayoutTests
{
    private static readonly WheelMetrics Metrics = new(150, 254, 59, 76, 96, 62, 27);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void Origin_reproduces_the_current_orbits_and_anchors(int count)
    {
        var names = Enumerable.Range(0, count).Select(i => new WheelSectionInput($"S{i}", i % 2 == 0)).ToArray();
        var result = WheelLayout.Compute(names, Metrics);
        Assert.Equal(new WheelPoint(560, 560), result.HubCenter);
        Assert.Equal(new WheelPoint(522, 522), result.HubTopLeft);
        Assert.Equal(new WheelPoint(512, 512), result.HaloTopLeft);
        Assert.Equal(new WheelPoint(546, 608), result.SearchTopLeft);
        foreach (var section in result.Sections)
        {
            var angle = -Math.PI / 2 + section.Index * Math.PI * 2 / Math.Max(1, count);
            var outer = section.Index % 2 == 1 || count > 1 && count % 2 == 1 && section.Index == count - 1;
            var radius = outer ? 254 : 150;
            var expected = new WheelPoint(560 + Math.Cos(angle) * radius, 560 + Math.Sin(angle) * radius);
            Assert.Equal(expected, section.Center);
            Assert.Equal(new WheelPoint(560, 560), section.SpokeStart);
            Assert.Equal(expected, section.SpokeEnd);
            Assert.Equal(new WheelPoint(expected.X, expected.Y + 59.0 / 2 + 4), section.LabelAnchor);
            Assert.Equal(new WheelPoint(expected.X + 22, expected.Y - 30), section.BadgeAnchor);
        }
    }

    [Fact]
    public void A_saved_section_changes_all_of_its_geometry_without_moving_others()
    {
        var data = new WheelLayoutData();
        data.Sections["Social"] = new WheelPoint(310, 250);
        var result = WheelLayout.Compute([new("Social", true), new("Web", false)], Metrics, data);
        Assert.Equal(new WheelPoint(310, 250), result.Section("Social").SpokeEnd);
        Assert.Equal(new WheelPoint(310, 283.5), result.Section("Social").LabelAnchor);
        Assert.Equal(new WheelPoint(332, 220), result.Section("Social").BadgeAnchor);
        Assert.Equal(new WheelPoint(560 + Math.Cos(Math.PI / 2) * 254, 560 + Math.Sin(Math.PI / 2) * 254), result.Section("Web").Center);
    }

    [Fact]
    public void Drag_constraints_preserve_radius_or_angle_and_exclude_the_hub()
    {
        var hub = new WheelPoint(560, 560);
        var origin = new WheelPoint(710, 560);
        var orbit = WheelLayout.ProjectDrag(new WheelPoint(730, 650), origin, hub, true, false, 8);
        Assert.Equal(150, Math.Sqrt(Math.Pow(orbit.X - hub.X, 2) + Math.Pow(orbit.Y - hub.Y, 2)), 8);
        var radial = WheelLayout.ProjectDrag(new WheelPoint(730, 650), origin, hub, false, true, 8);
        Assert.Equal(560, radial.Y, 8);
        var limited = WheelLayout.LimitSection(hub, hub, 59, 76);
        Assert.True(Math.Sqrt(Math.Pow(limited.X - hub.X, 2) + Math.Pow(limited.Y - hub.Y, 2)) >= 75.5);
        var edge = WheelLayout.LimitSection(new WheelPoint(-100, 5000), hub, 59, 76);
        Assert.Equal(new WheelPoint(63.5, 966.5), edge);
    }
}
