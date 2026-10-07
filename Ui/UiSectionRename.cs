namespace OrbitWeave.Ui;

// Les identifiants de cartes sont stables ; seuls les identifiants liés au nom de section changent.
public static class UiSectionRename
{
    public static void Apply(UiProfile profile, string oldName, string newName)
    {
        Rename(profile.Layout.Sections, oldName, newName);
        Rename(profile.Layout.Stacks, oldName, newName);
        foreach (var group in profile.Layout.CardGroups.Values)
            if (group.Section.Equals(oldName, StringComparison.OrdinalIgnoreCase)) group.Section = newName;
        RenameIds(profile.Layout.Attachments, oldName, newName);
        RenameIds(profile.Elements, oldName, newName);
        RenameIds(profile.States, oldName, newName);
        RenameIds(profile.Animations, oldName, newName);
        foreach (var band in profile.Resolutions.Values) RenameIds(band, oldName, newName);
        foreach (var item in profile.CustomElements.Values)
            if (item.AttachTo.Equals($"wheel.section:{oldName}", StringComparison.OrdinalIgnoreCase)) item.AttachTo = $"wheel.section:{newName}";
    }

    private static void Rename<T>(Dictionary<string, T> values, string oldKey, string newKey)
    {
        var key = values.Keys.FirstOrDefault(key => key.Equals(oldKey, StringComparison.OrdinalIgnoreCase));
        if (key is null) return;
        var value = values[key]; values.Remove(key); values[newKey] = value;
    }

    private static void RenameIds<T>(Dictionary<string, T> values, string oldName, string newName)
    {
        foreach (var key in values.Keys.Where(key => (key.StartsWith("wheel.section") || key.StartsWith("wheel.label") || key.StartsWith("wheel.badge:") || key.StartsWith("wheel.spoke:") || key.StartsWith("wheel.link:")) && key.EndsWith($":{oldName}", StringComparison.OrdinalIgnoreCase)).ToArray())
            Rename(values, key, key[..^oldName.Length] + newName);
    }
}
