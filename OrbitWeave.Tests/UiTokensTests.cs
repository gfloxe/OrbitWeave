using System.Windows;
using System.Windows.Media;
using OrbitWeave.Ui;

namespace OrbitWeave.Tests;

public class UiTokensTests
{
    [Fact]
    public void Values_are_converted_to_the_type_of_the_original_resource()
    {
        Assert.Equal(80.5, UiTokens.Convert(76.0, "80.5"));
        Assert.Equal(Color.FromArgb(0xB3, 0x28, 0x28, 0x28), ((SolidColorBrush)UiTokens.Convert(Brushes.Black, "#B3282828")!).Color);
        Assert.Null(UiTokens.Convert(76.0, "abc"));
        Assert.Null(UiTokens.Convert(Brushes.Black, "pas une couleur"));
        Assert.Equal("#B3282828", UiTokens.Format(new SolidColorBrush(Color.FromArgb(0xB3, 0x28, 0x28, 0x28))));
        Assert.Equal("80.5", UiTokens.Format(80.5));
    }

    [Fact]
    public void The_profile_layer_wins_and_theme_values_win_over_profile_values()
    {
        var root = new ResourceDictionary();
        var baseLayer = new ResourceDictionary { ["HubDiameter"] = 76.0, ["GlassBaseBrush"] = Brushes.Black };
        var layer = new ResourceDictionary();
        root.MergedDictionaries.Add(baseLayer);
        root.MergedDictionaries.Add(layer);
        var profile = new UiProfile();
        profile.Tokens["HubDiameter"] = "90";
        profile.Tokens["Inconnu"] = "1";
        profile.Themes["Sombre"] = new() { ["HubDiameter"] = "100" };

        UiTokens.Apply(root, layer, profile, "Clair");
        Assert.Equal(90.0, root["HubDiameter"]);
        Assert.False(layer.Contains("Inconnu"));

        UiTokens.Apply(root, layer, profile, "Sombre");
        Assert.Equal(100.0, root["HubDiameter"]);

        UiTokens.Apply(root, layer, new UiProfile(), "Sombre");
        Assert.Equal(76.0, root["HubDiameter"]);
    }
}
