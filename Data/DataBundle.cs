using System.Text.Json;
using System.Text.Json.Nodes;
using OrbitWeave.Actions;
using OrbitWeave.Ui;

namespace OrbitWeave.Data;

public sealed class DataBundleException(string message) : Exception(message);

// Fichier .orbitweave : les boutons (actions.json), le profil d'interface actif et les réglages, en un seul JSON.
// Tout est vérifié avant d'écrire quoi que ce soit ; l'existant est copié dans Sauvegardes\avant-import-<date>.
public static class DataBundle
{
    public const string Extension = ".orbitweave";
    private const string Kind = "OrbitWeave";
    private const string Unreadable = "Ce fichier n'est pas une sauvegarde OrbitWeave lisible. Rien n'a été modifié.";
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    // dataDir : dossier des données (celui d'actions.json et settings.json).
    public static void Export(string dataDir, string path)
    {
        var settings = HotkeySettings.Load(Path.Combine(dataDir, "settings.json"));
        var actions = ActionStore.Load(Path.Combine(dataDir, "actions.json"));
        var store = new UiProfileStore(Path.Combine(dataDir, "ui", "profiles"));
        var profileName = settings.InterfaceProfile;
        JsonNode? profile = null;
        if (profileName != UiProfileStore.OriginName)
        {
            var loaded = store.Load(profileName, out var problem);
            if (problem is null) profile = JsonSerializer.SerializeToNode(loaded);
            else profileName = UiProfileStore.OriginName;
        }
        var bundle = new JsonObject
        {
            ["Kind"] = Kind,
            ["Format"] = 1,
            ["Exported"] = DateTime.Now.ToString("s"),
            ["Actions"] = JsonNode.Parse(JsonSerializer.Serialize(actions, ActionJson.Options)),
            ["InterfaceName"] = profileName,
            ["Interface"] = profile,
            ["Settings"] = JsonSerializer.SerializeToNode(settings)
        };
        var temp = path + ".tmp";
        File.WriteAllText(temp, bundle.ToJsonString(Indented));
        File.Move(temp, path, true);
    }

    // Retourne le dossier de la copie de sécurité. Lève DataBundleException si le fichier est illisible (rien n'est touché).
    public static string Import(string dataDir, string path)
    {
        var (actions, settings, profile) = Read(path);
        var backup = Backup(dataDir);
        if (profile is not null)
        {
            var store = new UiProfileStore(Path.Combine(dataDir, "ui", "profiles"));
            store.Save(profile);
            settings.InterfaceProfile = profile.Name;
        }
        else settings.InterfaceProfile = UiProfileStore.OriginName;
        ActionStore.Save(actions, Path.Combine(dataDir, "actions.json"));
        settings.Save(Path.Combine(dataDir, "settings.json"));
        return backup;
    }

    // Lit et vérifie le fichier sans rien écrire.
    public static void Check(string path) => Read(path);

    internal static (List<ActionItem> Actions, HotkeySettings Settings, UiProfile? Profile) Read(string path)
    {
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new DataBundleException(Unreadable);
            if ((string?)root["Kind"] != Kind || root["Actions"] is not JsonArray actionsNode || root["Settings"] is not JsonObject settingsNode)
                throw new DataBundleException(Unreadable);
            var (actions, _) = ActionMigration.Read(actionsNode.ToJsonString());
            ActionTree.Repair(actions);
            var settings = settingsNode.Deserialize<HotkeySettings>() ?? throw new DataBundleException(Unreadable);
            settings.Normalize();
            UiProfile? profile = null;
            if (root["Interface"] is JsonObject profileNode)
            {
                profile = UiProfileStore.Parse(profileNode.ToJsonString());
                var name = (string?)root["InterfaceName"];
                profile.Name = UiProfileStore.CleanName(string.IsNullOrWhiteSpace(name) || name == UiProfileStore.OriginName ? "Importé" : name);
            }
            return (actions, settings, profile);
        }
        catch (DataBundleException) { throw; }
        catch (Exception) { throw new DataBundleException(Unreadable); }
    }

    private static string Backup(string dataDir)
    {
        var stamp = Path.Combine(dataDir, "Sauvegardes", $"avant-import-{DateTime.Now:yyyyMMdd-HHmmss}");
        var folder = stamp;
        for (var index = 2; Directory.Exists(folder); index++) folder = $"{stamp}-{index}";
        Directory.CreateDirectory(folder);
        foreach (var name in new[] { "actions.json", "settings.json" })
            if (File.Exists(Path.Combine(dataDir, name))) File.Copy(Path.Combine(dataDir, name), Path.Combine(folder, name));
        var profiles = Path.Combine(dataDir, "ui", "profiles");
        if (Directory.Exists(profiles))
        {
            var target = Path.Combine(folder, "ui", "profiles");
            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(profiles, "*.json")) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }
        return folder;
    }
}
