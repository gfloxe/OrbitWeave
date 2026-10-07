using System.Text.Json;

namespace OrbitWeave.Ui;

public sealed class UiProfileException(string message) : Exception(message);

// Profils enregistrés dans un dossier, un fichier JSON par profil. « Origine » est intégré et en lecture seule.
public sealed class UiProfileStore(string folder)
{
    public const string OriginName = "Origine";
    private const string Invalid = "Ce fichier n'est pas un profil OrbitWeave valide.";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static UiProfileStore Default => new(Path.Combine(ActionStore.DirectoryPath, "ui", "profiles"));
    public string Folder { get; } = folder;

    public IReadOnlyList<string> Names()
    {
        var saved = Directory.Exists(Folder)
            ? Directory.GetFiles(Folder, "*.json")
                .Select(Path.GetFileNameWithoutExtension).OfType<string>()
                .Where(name => !name.Contains(".broken-") && !name.Equals(OriginName, StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.CurrentCultureIgnoreCase).ToList()
            : [];
        return [OriginName, .. saved];
    }

    public UiProfile Load(string name, out string? problem)
    {
        problem = null;
        if (name == OriginName) return new UiProfile();
        var path = PathFor(name);
        if (!File.Exists(path))
        {
            problem = $"Le profil « {name} » est introuvable ; l'interface d'origine est utilisée.";
            return new UiProfile();
        }
        try
        {
            var profile = Parse(File.ReadAllText(path));
            profile.Name = name;
            return profile;
        }
        catch (Exception)
        {
            // Un profil illisible est mis de côté, jamais écrasé.
            try { File.Copy(path, Path.Combine(Folder, $"{name}.broken-{DateTime.Now:yyyyMMdd-HHmmss}.json"), true); }
            catch (IOException) { }
            problem = $"Le profil « {name} » est illisible ; l'interface d'origine est utilisée.";
            return new UiProfile();
        }
    }

    public static UiProfile Parse(string json)
    {
        UiProfile? profile;
        try { profile = JsonSerializer.Deserialize<UiProfile>(json, Json); }
        catch (JsonException) { throw new UiProfileException(Invalid); }
        if (profile is null || profile.Schema < 1 || profile.Schema > UiProfile.CurrentSchema ||
            profile.Tokens is null || profile.Themes is null || profile.Elements is null ||
            profile.States is null || profile.Animations is null || profile.CustomElements is null || profile.Resolutions is null ||
            profile.Layout is null || profile.Layout.Sections is null || profile.Layout.Attachments is null || profile.Layout.Stacks is null)
            throw new UiProfileException(Invalid);
        profile.Layout = profile.Layout.Clone();
        if (profile.Layout.Sections.Values.Concat(profile.Layout.Attachments.Values).Append(profile.Layout.Hub ?? new()).Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)) ||
            profile.Layout.Stacks.Values.Any(stack => !double.IsFinite(stack.X) || !double.IsFinite(stack.Y) || stack.Side is < -1 or > 1) ||
            profile.CustomElements.Values.Any(item => item is null || item.AttachTo is null)) throw new UiProfileException(Invalid);
        return profile;
    }

    public void Save(UiProfile profile)
    {
        if (profile.Name.Equals(OriginName, StringComparison.OrdinalIgnoreCase))
            throw new UiProfileException("Le profil « Origine » ne peut pas être modifié.");
        profile.Schema = UiProfile.CurrentSchema;
        Directory.CreateDirectory(Folder);
        var path = PathFor(profile.Name);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(profile, Json));
        File.Move(temp, path, true);
    }

    public void RenameSection(string oldName, string newName, UiProfile active)
    {
        var activeCopy = active.Clone(active.Name);
        UiSectionRename.Apply(activeCopy, oldName, newName);
        RenameSection(oldName, newName, active.Name, active.Name != OriginName ? activeCopy : null);
        UiSectionRename.Apply(active, oldName, newName);
    }

    // Renommage depuis la roue : le profil actif passe par l'historique de l'éditeur, les autres sont réécrits ici.
    public void RenameSectionInOtherProfiles(string oldName, string newName, string activeName) =>
        RenameSection(oldName, newName, activeName, null);

    private void RenameSection(string oldName, string newName, string activeName, UiProfile? activeCopy)
    {
        var updates = new List<UiProfile>();
        foreach (var name in Names().Where(name => name != OriginName && name != activeName))
        {
            var profile = Load(name, out var problem);
            if (problem is not null) continue;
            UiSectionRename.Apply(profile, oldName, newName);
            updates.Add(profile);
        }
        if (activeCopy is not null) updates.Add(activeCopy);
        var originals = updates.ToDictionary(profile => profile.Name, profile =>
            File.Exists(PathFor(profile.Name)) ? File.ReadAllText(PathFor(profile.Name)) : null);
        var written = new List<string>();
        try
        {
            foreach (var profile in updates) { Save(profile); written.Add(profile.Name); }
        }
        catch
        {
            // Un échec d'écriture ne doit pas laisser les profils avec des noms différents des actions.
            foreach (var name in written)
                if (originals[name] is { } json) File.WriteAllText(PathFor(name), json);
                else File.Delete(PathFor(name));
            throw;
        }
    }

    public void Delete(string name)
    {
        if (!name.Equals(OriginName, StringComparison.OrdinalIgnoreCase)) File.Delete(PathFor(name));
    }

    public void Export(UiProfile profile, string path) => File.WriteAllText(path, JsonSerializer.Serialize(profile, Json));

    public UiProfile Import(string path)
    {
        var profile = Parse(File.ReadAllText(path));
        profile.Name = UniqueName(string.IsNullOrWhiteSpace(profile.Name) ? Path.GetFileNameWithoutExtension(path) : profile.Name);
        Save(profile);
        return profile;
    }

    public string UniqueName(string wanted)
    {
        var name = CleanName(wanted);
        var existing = Names();
        var candidate = name;
        for (var index = 2; existing.Contains(candidate, StringComparer.OrdinalIgnoreCase); index++)
            candidate = $"{name} ({index})";
        return candidate;
    }

    public static string CleanName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var kept = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        var cleaned = string.Join(" ", kept.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim('.');
        return cleaned.Length == 0 ? "Profil" : cleaned[..Math.Min(40, cleaned.Length)];
    }

    private string PathFor(string name) => Path.Combine(Folder, CleanName(name) + ".json");
}
