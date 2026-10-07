using System.Globalization;
using OrbitWeave.Orbit;

namespace OrbitWeave.Ui;

// La liste des sections et les jetons effectifs sont connus au premier calcul de la roue.
// La migration est idempotente, y compris si une section absente réapparaît plus tard.
public static class UiLayoutMigration
{
    public static bool Apply(UiProfile profile, IReadOnlyList<WheelSectionInput> sections, WheelMetrics metrics)
    {
        var automatic = WheelLayout.Compute(sections, metrics);
        var changed = false;
        foreach (var section in automatic.Sections)
        {
            var id = $"wheel.section:{section.Name}";
            if (!TakeOffset(profile, id, out var delta)) continue;
            profile.Layout.Sections[section.Name] = profile.Layout.Sections.GetValueOrDefault(section.Name, section.Center) + delta;
            changed = true;
        }
        foreach (var id in profile.Elements.Keys.ToArray())
        {
            if (id != "wheel.hub" && id != "wheel.search" &&
                !id.StartsWith("wheel.label:", StringComparison.Ordinal) &&
                !id.StartsWith("wheel.label.title:", StringComparison.Ordinal) &&
                !id.StartsWith("wheel.label.subtitle:", StringComparison.Ordinal) &&
                !id.StartsWith("wheel.badge:", StringComparison.Ordinal)) continue;
            if (!TakeOffset(profile, id, out var delta)) continue;
            if (id == "wheel.hub") profile.Layout.Hub = (profile.Layout.Hub ?? WheelLayout.DefaultCenter) + delta;
            else profile.Layout.Attachments[id] = profile.Layout.Attachments.GetValueOrDefault(id) + delta;
            changed = true;
        }
        profile.Schema = UiProfile.CurrentSchema;
        return changed;
    }

    private static bool TakeOffset(UiProfile profile, string id, out WheelPoint delta)
    {
        delta = default;
        if (!profile.Elements.TryGetValue(id, out var values) ||
            !values.ContainsKey("OffsetX") && !values.ContainsKey("OffsetY")) return false;
        double Number(string key) => double.TryParse(values.GetValueOrDefault(key), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) ? number : 0;
        delta = new WheelPoint(Number("OffsetX"), Number("OffsetY"));
        values.Remove("OffsetX");
        values.Remove("OffsetY");
        if (values.Count == 0) profile.Elements.Remove(id);
        return true;
    }
}
