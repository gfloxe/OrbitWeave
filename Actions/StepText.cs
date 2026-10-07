using System.Globalization;

namespace OrbitWeave.Actions;

// Une ligne lisible par étape, pour la bulle : « Discord », « Attendre 2 s », « youtube.com ».
public static class StepText
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    public static string Describe(ActionStep step) => step switch
    {
        WaitStep wait => $"Attendre {wait.Seconds.ToString("0.##", French)} s",
        ScriptStep { Source: ScriptSource.Commande } script => "Commande · " + FirstLine(script.Code),
        ScriptStep script => "Script · " + FileName(script.Path),
        OpenStep open => Target(open.Target),
        _ => "(étape inconnue)"
    };

    private static string Target(string target)
    {
        target = target.Trim();
        if (target.Length == 0) return "(vide)";
        // Appli du Store : « shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App » → « WindowsCalculator ».
        if (target.StartsWith(@"shell:AppsFolder\", StringComparison.OrdinalIgnoreCase))
        {
            var id = target[@"shell:AppsFolder\".Length..].Split('!')[0].Split('_')[0];
            return id[(id.LastIndexOf('.') + 1)..];
        }
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            return uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        if (target.Contains(':') && !target.Contains('\\') && !target.Contains('/')) return target; // ms-settings:, mailto:…
        var trimmed = target.TrimEnd('\\', '/');
        if (trimmed.Length <= 2) return target; // « C:\ »
        var name = Path.GetFileName(trimmed);
        // « chatgpt.exe.lnk » → « chatgpt » : toutes les extensions de lanceur, l'une après l'autre.
        while (Path.GetExtension(name).ToLowerInvariant() is ".exe" or ".lnk" or ".url" or ".bat" or ".cmd" or ".appref-ms" &&
               Path.GetFileNameWithoutExtension(name).Length > 0)
            name = Path.GetFileNameWithoutExtension(name);
        return name;
    }

    private static string FileName(string path) => string.IsNullOrWhiteSpace(path) ? "(vide)" : Path.GetFileName(path.Trim());

    private static string FirstLine(string code)
    {
        var line = code.Split('\n')[0].Trim();
        return line.Length == 0 ? "(vide)" : line.Length > 40 ? line[..40] + "…" : line;
    }
}
