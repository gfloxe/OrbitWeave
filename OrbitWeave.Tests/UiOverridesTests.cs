using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OrbitWeave.Ui;
using Button = System.Windows.Controls.Button;

namespace OrbitWeave.Tests;

public class UiOverridesTests
{
    [Fact]
    public void Subtitle_attachment_is_relative_and_can_be_reset() => Sta(() =>
    {
        var profile = new UiProfile();
        profile.Layout.Attachments["wheel.label.subtitle:Social"] = new Orbit.WheelPoint(3, -2);
        var text = new TextBlock { Text = "Sous-titre" }.With("wheel.label.subtitle:Social");
        UiOverrides.Apply(text, profile);
        Assert.Equal(3, text.RenderTransform.Value.OffsetX);
        Assert.Equal(-2, text.RenderTransform.Value.OffsetY);
        profile.Layout.Attachments.Clear();
        UiOverrides.Apply(text, profile);
        Assert.True(text.RenderTransform.Value.IsIdentity);
    });
    internal static void Sta(Action body)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { body(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Xunit.Sdk.XunitException(error.ToString());
    }

    private static UiProfile With(string id, string property, string value)
    {
        var profile = new UiProfile();
        profile.Elements[id] = new() { [property] = value };
        return profile;
    }

    [Fact]
    public void Template_values_apply_then_instance_values_win() => Sta(() =>
    {
        var profile = new UiProfile();
        profile.Elements["wheel.label.title"] = new() { ["FontSize"] = "20", ["Text"] = "Modèle" };
        profile.Elements["wheel.label.title:Web"] = new() { ["Text"] = "Internet" };
        var text = new TextBlock { Text = "Web", FontSize = 13 }.With("wheel.label.title:Web");

        UiOverrides.Apply(text, profile);

        Assert.Equal(("Internet", 20.0), (text.Text, text.FontSize));
    });

    [Fact]
    public void State_values_override_normal_values_and_instance_wins()
    {
        var profile = new UiProfile();
        profile.Elements["wheel.section"] = new() { ["Scale"] = "1.1" };
        profile.States["wheel.section"] = new() { ["Hover"] = new() { ["Scale"] = "1.2", ["Opacity"] = "0.8" } };
        profile.States["wheel.section:Web"] = new() { ["Hover"] = new() { ["Scale"] = "1.3" } };
        var values = UiOverrides.Resolve(profile, "wheel.section:Web", "Hover");
        Assert.Equal("1.3", values["Scale"]);
        Assert.Equal("0.8", values["Opacity"]);
        Assert.Equal("1.1", UiOverrides.Resolve(profile, "wheel.section:Web")["Scale"]);
    }

    [Fact]
    public void Animation_settings_inherit_from_template_and_can_be_cleared() => Sta(() =>
    {
        var profile = new UiProfile();
        profile.Animations["wheel.section"] = new() { ["Type"] = "Scale", ["DurationMs"] = "220" };
        profile.Animations["wheel.section:Web"] = new() { ["DurationMs"] = "120" };
        var settings = UiMotion.Resolve(profile, "wheel.section:Web");
        Assert.Equal("Scale", settings["Type"]);
        Assert.Equal("120", settings["DurationMs"]);

        var element = new Border();
        var original = element.RenderTransform;
        UiMotion.Play(element, profile, "wheel.section:Web", 1);
        Assert.IsType<TransformGroup>(element.RenderTransform);
        UiMotion.Clear(element);
        Assert.Same(original, element.RenderTransform);
    });

    [Fact]
    public void Four_custom_element_kinds_are_created_with_stable_ids() => Sta(() =>
    {
        var profile = new UiProfile();
        foreach (var kind in new[] { "Text", "Image", "Shape", "Separator" })
        {
            var id = $"wheel.custom:{kind}";
            profile.CustomElements[id] = new UiCustomElement
            { Id = id, Parent = "wheel", Kind = kind, Content = kind, X = 30, Y = 40 };
        }
        var canvas = new Canvas();
        UiCustomElements.AddTo(canvas, profile, "wheel");
        Assert.Equal(4, canvas.Children.Count);
        foreach (FrameworkElement element in canvas.Children)
        {
            Assert.StartsWith("wheel.custom:", UiId.Get(element));
            Assert.Equal(30, Canvas.GetLeft(element));
            Assert.Equal(40, Canvas.GetTop(element));
        }
    });

    [Fact]
    public void Removing_an_override_restores_the_original_local_value() => Sta(() =>
    {
        var border = new Border { Background = Brushes.Red }.With("settings/x");
        UiOverrides.Apply(border, With("settings/x", "Background", "#FF00FF00"));
        Assert.Equal(Colors.Lime, ((SolidColorBrush)border.Background).Color);

        UiOverrides.Apply(border, new UiProfile());

        Assert.Same(Brushes.Red, border.Background);
    });

    [Fact]
    public void Offsets_move_placed_elements_and_are_reversible() => Sta(() =>
    {
        var canvas = new Canvas();
        var node = new Button().With("wheel.section:Web");
        canvas.Children.Add(node);
        Canvas.SetLeft(node, 100);

        UiOverrides.Apply(node, With("wheel.section", "OffsetX", "12"));
        Assert.Equal(112, Canvas.GetLeft(node));
        UiOverrides.Apply(node, With("wheel.section", "OffsetX", "-4"));
        Assert.Equal(96, Canvas.GetLeft(node));
        UiOverrides.Apply(node, new UiProfile());
        Assert.Equal(100, Canvas.GetLeft(node));
    });

    [Fact]
    public void Unplaced_elements_move_with_a_render_transform() => Sta(() =>
    {
        var text = new TextBlock().With("a");
        UiOverrides.Apply(text, With("a", "OffsetY", "5"));
        Assert.Equal(5, text.RenderTransform.Value.OffsetY);
        UiOverrides.Apply(text, new UiProfile());
        Assert.True(text.RenderTransform.Value.IsIdentity);
    });

    [Fact]
    public void Unsupported_or_invalid_values_are_ignored() => Sta(() =>
    {
        var line = new System.Windows.Shapes.Line { StrokeThickness = 1 }.With("wheel.spoke:Web");
        Assert.False(UiOverrides.Supports(line, "Text"));
        UiOverrides.Apply(line, With("wheel.spoke", "Text", "x"));
        UiOverrides.Apply(line, With("wheel.spoke", "BorderThickness", "abc"));
        Assert.Equal(1, line.StrokeThickness);
        UiOverrides.Apply(line, With("wheel.spoke", "BorderThickness", "3"));
        Assert.Equal(3, line.StrokeThickness);
    });

    [Fact]
    public void Visible_and_opacity_targets() => Sta(() =>
    {
        var text = new TextBlock().With("a");
        UiOverrides.Apply(text, With("a", "Visible", "false"));
        Assert.Equal(Visibility.Collapsed, text.Visibility);
        Assert.Equal(0.4, UiOverrides.Opacity(text, With("a", "Opacity", "0.4")));
        Assert.Equal(1, UiOverrides.Opacity(text, new UiProfile()));
    });

    [Fact]
    public void Hub_can_be_masked_without_removing_its_click_target() => Sta(() =>
    {
        var hub = new Button().With("wheel.hub", isProtected: true);
        var hidden = With("wheel.hub", "Visible", "false");
        UiOverrides.Apply(hub, hidden);
        Assert.Equal(Visibility.Visible, hub.Visibility);
        Assert.Equal(0.01, hub.Opacity);
        Assert.True(hub.IsHitTestVisible);
        Assert.Equal(0.01, UiOverrides.Opacity(hub, hidden));
        Assert.Equal(1, UiOverrides.Opacity(hub, With("wheel.hub", "Opacity", "0")));
        var reset = new Button().With("wheel.menu.reset", isProtected: true);
        UiOverrides.Apply(reset, With("wheel.menu.reset", "Visible", "false"));
        Assert.Equal(Visibility.Visible, reset.Visibility);
    });

    [Fact]
    public void Apply_tree_reaches_children() => Sta(() =>
    {
        var panel = new StackPanel().With("p");
        var child = new TextBlock { Text = "a" }.With("p.child");
        panel.Children.Add(child);
        UiOverrides.ApplyTree(panel, With("p.child", "Text", "b"));
        Assert.Equal("b", child.Text);
    });

    [Fact]
    public void Current_values_are_described_like_overrides() => Sta(() =>
    {
        var border = new Border { Background = new SolidColorBrush(Color.FromArgb(0xB3, 0x28, 0x28, 0x28)), CornerRadius = new CornerRadius(8) };
        var text = new TextBlock { Text = "Web", FontSize = 13, FontWeight = FontWeights.SemiBold };
        Assert.Equal("#B3282828", UiOverrides.Describe(border, "Background"));
        Assert.Equal("8", UiOverrides.Describe(border, "CornerRadius"));
        Assert.Equal("true", UiOverrides.Describe(border, "Visible"));
        Assert.Equal("Web", UiOverrides.Describe(text, "Text"));
        Assert.Equal("13", UiOverrides.Describe(text, "FontSize"));
        Assert.Equal("SemiBold", UiOverrides.Describe(text, "FontWeight"));
        Assert.Equal("0", UiOverrides.Describe(text, "OffsetX"));
    });
}
