using System.Text.Json;

namespace OrbitWeave.Tests;

public class HotkeySettingsTests
{
    [Fact]
    public void Legacy_preferences_are_normalized_without_losing_compatibility_fields()
    {
        var settings = JsonSerializer.Deserialize<HotkeySettings>("""{"Theme":"Graphite","Idle":"Semi-transparente","Language":"English","Position":"Centre","SettingsTheme":"Sombre","Key":"F12"}""")!;
        settings.Normalize();
        Assert.Equal("Verre", settings.Theme);
        Assert.Equal("SemiTransparent", settings.Idle);
        Assert.Equal("English", settings.Language);
        Assert.Equal("Centre", settings.Position);
        Assert.Equal("Sombre", settings.SettingsTheme);
        Assert.Equal("F12", settings.Key);
        Assert.Equal(2, settings.Version);
    }

    [Fact]
    public void Missing_file_uses_new_installation_defaults()
    {
        var settings = HotkeySettings.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"));
        Assert.Equal("Verre liquide", settings.Theme);
        Assert.Equal("Comme Windows", settings.SettingsTheme);
    }

    [Fact]
    public void Unreadable_file_is_set_aside_before_defaults_are_used()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "settings.json");
        File.WriteAllText(path, "{\"Theme\":\"Sombre\",");
        var settings = HotkeySettings.Load(path);
        Assert.Equal("Verre liquide", settings.Theme);
        var broken = Assert.Single(Directory.GetFiles(dir, "settings.broken-*.json"));
        Assert.Equal("{\"Theme\":\"Sombre\",", File.ReadAllText(broken));
    }

    [Fact]
    public void Save_replaces_the_file_without_leaving_a_temporary_copy()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "settings.json");
        File.WriteAllText(path, "{}");
        new HotkeySettings { Key = "F12" }.Save(path);
        Assert.Equal("F12", HotkeySettings.Load(path).Key);
        Assert.Equal(["settings.json"], Directory.GetFiles(dir).Select(Path.GetFileName));
    }

    [Fact]
    public void Wheel_reset_preserves_keyboard_profiles_and_compatibility_values()
    {
        var settings = new HotkeySettings { Key = "F12", InterfaceProfile = "Personnel", Language = "English", Theme = "Clair" };
        settings.ResetWheel(HotkeySettings.Defaults());
        Assert.Equal("F12", settings.Key);
        Assert.Equal("Personnel", settings.InterfaceProfile);
        Assert.Equal("English", settings.Language);
        Assert.Equal("Verre liquide", settings.Theme);
    }

    [Fact]
    public void Keyboard_reset_preserves_wheel_and_profile()
    {
        var settings = new HotkeySettings { Theme = "Sombre", HoldSelect = false, InterfaceProfile = "Personnel", Key = "F12" };
        settings.ResetKeyboardAndWindow(HotkeySettings.Defaults());
        Assert.Equal("Sombre", settings.Theme);
        Assert.False(settings.HoldSelect);
        Assert.Equal("Personnel", settings.InterfaceProfile);
        Assert.Equal("Space", settings.Key);
        Assert.Equal("Comme Windows", settings.SettingsTheme);
    }

    [Fact]
    public void Old_files_without_sliders_take_them_from_the_three_choices()
    {
        var settings = JsonSerializer.Deserialize<HotkeySettings>("""{"Animation":"Lent","Idle":"PresqueInvisible"}""")!;
        settings.Normalize();
        Assert.Equal(1.55, settings.AnimationScale);
        Assert.Equal(0.15, settings.IdleOpacity);
        Assert.Equal(1, settings.GlassOpacity);
    }

    [Fact]
    public void Sliders_are_kept_within_bounds_and_pick_the_nearest_choice()
    {
        var settings = new HotkeySettings();
        settings.SetAnimationScale(0.7);
        Assert.Equal(("Rapide", 0.7), (settings.Animation, settings.AnimationScale!.Value));
        settings.SetAnimationScale(99);
        Assert.Equal(("Lent", HotkeySettings.MaxAnimationScale), (settings.Animation, settings.AnimationScale!.Value));
        settings.SetIdleOpacity(0.4);
        Assert.Equal(("SemiTransparent", 0.4), (settings.Idle, settings.IdleOpacity!.Value));
        settings.SetIdleOpacity(0);
        Assert.Equal(("PresqueInvisible", HotkeySettings.MinIdleOpacity), (settings.Idle, settings.IdleOpacity!.Value));
        settings.SetGlassOpacity(5);
        Assert.Equal(HotkeySettings.MaxGlassOpacity, settings.GlassOpacity);
    }

    [Fact]
    public void Sliders_survive_a_save_and_reload()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory().FullName, "settings.json");
        var settings = new HotkeySettings();
        settings.SetAnimationScale(1.2);
        settings.SetIdleOpacity(0.33);
        settings.SetGlassOpacity(0.7);
        settings.Save(path);
        var loaded = HotkeySettings.Load(path);
        Assert.Equal((1.2, 0.33, 0.7), (loaded.AnimationScale!.Value, loaded.IdleOpacity!.Value, loaded.GlassOpacity));
    }

    [Fact]
    public void Resetting_the_wheel_page_resets_the_sliders()
    {
        var settings = new HotkeySettings();
        settings.SetAnimationScale(2);
        settings.SetIdleOpacity(0.2);
        settings.SetGlassOpacity(0.5);
        settings.ResetWheel(HotkeySettings.Defaults());
        Assert.Equal((1.0, 1.0, 1.0), (settings.AnimationScale!.Value, settings.IdleOpacity!.Value, settings.GlassOpacity));
    }

    [Fact]
    public void Branch_close_delay_defaults_to_four_seconds_and_stays_in_bounds()
    {
        var old = JsonSerializer.Deserialize<HotkeySettings>("""{"Theme":"Sombre"}""")!;
        old.Normalize();
        Assert.Equal(4, old.BranchCloseSeconds);
        old.SetBranchCloseSeconds(0.1);
        Assert.Equal(HotkeySettings.MinBranchCloseSeconds, old.BranchCloseSeconds);
        old.SetBranchCloseSeconds(99);
        Assert.Equal(HotkeySettings.MaxBranchCloseSeconds, old.BranchCloseSeconds);
        old.SetBranchCloseSeconds(2.3);
        Assert.Equal(2.5, old.BranchCloseSeconds);
        old.ResetWheel(HotkeySettings.Defaults());
        Assert.Equal(4, old.BranchCloseSeconds);
    }
}
