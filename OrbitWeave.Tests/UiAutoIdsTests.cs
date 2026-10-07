using System.Windows.Controls;
using OrbitWeave.Ui;
using Button = System.Windows.Controls.Button;

namespace OrbitWeave.Tests;

public class UiAutoIdsTests
{
    [Fact]
    public void Ids_follow_names_then_type_rank_and_keep_explicit_ids() => UiOverridesTests.Sta(() =>
    {
        var root = new StackPanel();
        var named = new Border { Name = "Surface", Child = new Button() };
        root.Children.Add(named);
        root.Children.Add(new TextBlock());
        root.Children.Add(new TextBlock().With("settings.custom"));

        UiAutoIds.Assign(root, "settings");

        Assert.Equal(new[] { "settings", "settings/Surface", "settings/Surface/Button#1", "settings/TextBlock#1", "settings.custom" },
            UiAutoIds.Collect(root));
    });

    [Fact]
    public void Assigning_twice_keeps_the_same_ids() => UiOverridesTests.Sta(() =>
    {
        var root = new StackPanel();
        root.Children.Add(new TextBlock());
        root.Children.Add(new TextBlock());
        UiAutoIds.Assign(root, "settings");
        var first = UiAutoIds.Collect(root);
        UiAutoIds.Assign(root, "settings");
        Assert.Equal(first, UiAutoIds.Collect(root));
        Assert.Equal(first.Count, first.Distinct().Count());
    });
}
