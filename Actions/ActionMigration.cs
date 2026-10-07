using System.Text.Json;
using System.Text.Json.Nodes;

namespace OrbitWeave.Actions;

public static class ActionMigration
{
    // Lit actions.json ; Changed indique qu'il faut réécrire le fichier au nouveau format.
    public static (List<ActionItem> Actions, bool Changed) Read(string json)
    {
        var array = JsonNode.Parse(json) as JsonArray
            ?? throw new JsonException("Le fichier d'actions n'est pas une liste.");
        var actions = new List<ActionItem>();
        var changed = false;
        foreach (var node in array)
        {
            if (node is not JsonObject item) { changed = true; continue; }
            var action = item.Deserialize<ActionItem>(ActionJson.Options) ?? new ActionItem();
            if (!item.ContainsKey("Steps"))
            {
                action.Steps = [new OpenStep { Target = (string?)item["Target"] ?? "" }];
                changed = true;
            }
            if (!item.ContainsKey("Id")) changed = true;
            if (!item.ContainsKey("GroupType"))
            {
                action.GroupType = InferGroupType(action.Group);
                changed = true;
            }
            action.Type = (string?)item["Type"] ?? InferType(action.Target);
            actions.Add(action);
        }
        return (actions, changed);
    }

    public static string InferType(string target)
    {
        var value = target.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && !uri.IsFile) return "Site";
        if (value.Length > 0 && Directory.Exists(Environment.ExpandEnvironmentVariables(value))) return "Dossier";
        return "Application";
    }

    public static string InferGroupType(string group) => group switch
    {
        "Fichiers" => "Fichiers",
        "Windows" or "Système" => "Système",
        _ => "Ordinaire"
    };
}
