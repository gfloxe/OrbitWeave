namespace OrbitWeave.Actions;

public interface IStepExecutor
{
    Task ExecuteAsync(ActionStep step, CancellationToken cancellationToken);
}

// Erreur attendue, au message déjà lisible par l'utilisateur.
public sealed class StepFailedException(string message) : Exception(message);

public sealed record RunResult(bool Success, int FailedStep, string? Message)
{
    public static RunResult Ok { get; } = new(true, -1, null);
}

public sealed class ActionRunner(IStepExecutor executor)
{
    private readonly HashSet<Guid> _running = [];

    // Levé hors du fil de l'interface : (action, position parmi les étapes actives, nombre d'étapes actives).
    public event Action<ActionItem, int, int>? StepStarted;

    public bool IsRunning(ActionItem action)
    {
        lock (_running) return _running.Contains(action.Id);
    }

    // Renvoie null si l'action tourne déjà.
    public async Task<RunResult?> RunAsync(ActionItem action, CancellationToken cancellationToken = default)
    {
        lock (_running)
            if (!_running.Add(action.Id)) return null;
        try
        {
            if (action.Steps.Count == 0)
                return new RunResult(false, -1, "Cette action n'a aucune étape. Ajoutez-en en mode Modifier (clic droit sur le rond du milieu).");
            var active = action.Steps.Count(s => s.Enabled);
            var position = 0;
            for (var index = 0; index < action.Steps.Count; index++)
            {
                var step = action.Steps[index];
                if (!step.Enabled) continue;
                StepStarted?.Invoke(action, position++, active);
                try
                {
                    await executor.ExecuteAsync(step, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    var message = ex is StepFailedException ? ex.Message : "Windows n'a pas pu exécuter cette étape.";
                    return new RunResult(false, index, action.Steps.Count > 1 ? $"Étape {index + 1} · {message}" : message);
                }
            }
            return RunResult.Ok;
        }
        finally
        {
            lock (_running) _running.Remove(action.Id);
        }
    }
}
