using System.ComponentModel;
using System.Diagnostics;

namespace OrbitWeave.Actions;

public sealed class SystemStepExecutor : IStepExecutor
{
    public Task ExecuteAsync(ActionStep step, CancellationToken cancellationToken) => step switch
    {
        OpenStep open => Task.Run(() => Open(open), cancellationToken),
        ScriptStep script => RunScriptAsync(script, cancellationToken),
        WaitStep wait => Task.Delay(TimeSpan.FromSeconds(wait.Seconds), cancellationToken),
        _ => throw new StepFailedException("Cette étape n'est pas encore prise en charge.")
    };

    private static void Open(OpenStep step)
    {
        var target = Environment.ExpandEnvironmentVariables(step.Target.Trim());
        if (target.Length == 0)
            throw new StepFailedException("Aucune cible n'est définie. Ajoutez-la en mode Modifier (clic droit sur le rond du milieu).");
        using var _ = Start(new ProcessStartInfo
        {
            FileName = target,
            Arguments = Environment.ExpandEnvironmentVariables(step.Arguments),
            UseShellExecute = true
        }, target);
    }

    private static async Task RunScriptAsync(ScriptStep step, CancellationToken cancellationToken)
    {
        var info = ScriptCommand.Build(step);
        var label = step.Source == ScriptSource.Fichier ? Path.GetFileName(step.Path) : "la commande";
        using var process = Start(info, label);
        if (!step.WaitForExit || process is null) return;
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new StepFailedException($"Le script s'est terminé avec une erreur (code {process.ExitCode}).");
    }

    private static Process? Start(ProcessStartInfo info, string label)
    {
        try
        {
            return Process.Start(info);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // L'utilisateur a refusé l'élévation : l'action s'arrête sans message.
            throw new OperationCanceledException();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode is 2 or 3)
        {
            throw new StepFailedException($"« {label} » est introuvable.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1155)
        {
            throw new StepFailedException($"Aucune application n'est associée à « {label} ».");
        }
        catch (Exception)
        {
            throw new StepFailedException($"Windows n'a pas pu ouvrir « {label} ».");
        }
    }
}
