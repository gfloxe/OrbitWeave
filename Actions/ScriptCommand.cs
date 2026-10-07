using System.Diagnostics;
using System.Text;

namespace OrbitWeave.Actions;

public static class ScriptCommand
{
    private const string PowerShellFlags = "-NoProfile -ExecutionPolicy Bypass";

    // Une fenêtre visible qu'on n'attend pas reste ouverte pour qu'on puisse lire le résultat.
    public static ProcessStartInfo Build(ScriptStep step)
    {
        var keepOpen = !step.Hidden && !step.WaitForExit;
        ProcessStartInfo info;
        if (step.Source == ScriptSource.Fichier)
        {
            var path = Environment.ExpandEnvironmentVariables(step.Path.Trim());
            if (path.Length == 0) throw new StepFailedException("Aucun script n'est choisi.");
            if (!File.Exists(path)) throw new StepFailedException($"Le script « {path} » est introuvable.");
            info = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".ps1" => new ProcessStartInfo("powershell.exe", $"{PowerShellFlags}{(keepOpen ? " -NoExit" : "")} -File \"{path}\""),
                ".bat" or ".cmd" => new ProcessStartInfo("cmd.exe", $"{(keepOpen ? "/k" : "/c")} \"\"{path}\"\""),
                _ => new ProcessStartInfo(path)
            };
            info.WorkingDirectory = Path.GetDirectoryName(path)!;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(step.Code)) throw new StepFailedException("La commande est vide.");
            info = step.Shell == ScriptShell.Cmd
                ? new ProcessStartInfo("cmd.exe", $"{(keepOpen ? "/k" : "/c")} {step.Code}")
                : new ProcessStartInfo("powershell.exe",
                    $"{PowerShellFlags}{(keepOpen ? " -NoExit" : "")} -EncodedCommand {Convert.ToBase64String(Encoding.Unicode.GetBytes(step.Code))}");
        }
        // UseShellExecute est nécessaire pour « runas » ; la fenêtre se masque par WindowStyle.
        info.UseShellExecute = true;
        if (step.Admin) info.Verb = "runas";
        if (step.Hidden) info.WindowStyle = ProcessWindowStyle.Hidden;
        return info;
    }
}
