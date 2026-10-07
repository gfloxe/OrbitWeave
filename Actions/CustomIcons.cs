namespace OrbitWeave.Actions;

// Images d'icônes choisies dans la bulle : copiées dans le dossier des données (sous « icons »),
// pour que le bouton garde son image même si l'original est déplacé ou supprimé.
public static class CustomIcons
{
    public const string Folder = "icons";

    // Rend le chemin relatif enregistré dans le bouton (ex. « icons\3f…a1.png »).
    public static string Import(string source, string dataDirectory)
    {
        if (!IconTarget.IsImage(source)) throw new ArgumentException("Ce fichier n'est pas une image (PNG, JPG, ICO, BMP ou GIF).");
        var folder = Path.Combine(dataDirectory, Folder);
        Directory.CreateDirectory(folder);
        var name = Guid.NewGuid().ToString("N") + Path.GetExtension(source).ToLowerInvariant();
        File.Copy(source, Path.Combine(folder, name));
        return Path.Combine(Folder, name);
    }

    // « Mes icônes » : les images du dossier, de la plus ancienne à la plus récente (chemins relatifs).
    public static IReadOnlyList<string> List(string dataDirectory)
    {
        var folder = Path.Combine(dataDirectory, Folder);
        if (!Directory.Exists(folder)) return [];
        return new DirectoryInfo(folder).GetFiles()
            .Where(file => IconTarget.IsImage(file.Name))
            .OrderBy(file => file.CreationTimeUtc).ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .Select(file => Path.Combine(Folder, file.Name)).ToList();
    }

    public static bool Same(string? a, string? b) =>
        a is not null && b is not null && Path.GetFullPath(a, "C:\\").Equals(Path.GetFullPath(b, "C:\\"), StringComparison.OrdinalIgnoreCase);

    // Noms des boutons qui portent cette image.
    public static IReadOnlyList<string> UsedBy(IEnumerable<ActionItem> actions, string relative) =>
        actions.Where(action => Same(action.IconFile, relative)).Select(action => action.Name).ToList();

    // Supprime l'image ; les boutons qui la portaient reprennent leur icône automatique (à enregistrer par l'appelant).
    public static List<ActionItem> Delete(IEnumerable<ActionItem> actions, string dataDirectory, string relative)
    {
        var copy = ActionEdits.Clone(actions);
        foreach (var action in copy.Where(action => Same(action.IconFile, relative))) action.IconFile = null;
        var path = Path.Combine(dataDirectory, relative);
        if (File.Exists(path)) File.Delete(path);
        return copy;
    }
}
