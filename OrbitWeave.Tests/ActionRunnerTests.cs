using OrbitWeave;
using OrbitWeave.Actions;

namespace OrbitWeave.Tests;

public class ActionRunnerTests
{
    private sealed class FakeExecutor : IStepExecutor
    {
        public List<ActionStep> Executed { get; } = [];
        public Func<ActionStep, Task>? Behavior { get; init; }
        public async Task ExecuteAsync(ActionStep step, CancellationToken cancellationToken)
        {
            Executed.Add(step);
            if (Behavior is not null) await Behavior(step);
        }
    }

    [Fact]
    public async Task Steps_run_in_order_and_disabled_steps_are_skipped()
    {
        var a = new WaitStep(); var b = new WaitStep { Enabled = false }; var c = new OpenStep();
        var executor = new FakeExecutor();
        var progress = new List<(int, int)>();
        var runner = new ActionRunner(executor);
        runner.StepStarted += (_, index, total) => progress.Add((index, total));

        var result = await runner.RunAsync(new ActionItem { Steps = [a, b, c] });

        Assert.True(result!.Success);
        Assert.Equal(new ActionStep[] { a, c }, executor.Executed);
        Assert.Equal(new[] { (0, 2), (1, 2) }, progress);
    }

    [Fact]
    public async Task First_failure_stops_the_action_with_a_numbered_message()
    {
        var failing = new OpenStep();
        var executor = new FakeExecutor { Behavior = s => s == failing ? throw new StepFailedException("« x » est introuvable.") : Task.CompletedTask };
        var action = new ActionItem { Steps = [new WaitStep(), failing, new WaitStep()] };

        var result = await new ActionRunner(executor).RunAsync(action);

        Assert.False(result!.Success);
        Assert.Equal(1, result.FailedStep);
        Assert.Equal("Étape 2 · « x » est introuvable.", result.Message);
        Assert.Equal(2, executor.Executed.Count);
    }

    [Fact]
    public async Task A_single_step_message_has_no_prefix()
    {
        var executor = new FakeExecutor { Behavior = _ => throw new StepFailedException("La commande est vide.") };
        var result = await new ActionRunner(executor).RunAsync(new ActionItem { Steps = [new ScriptStep()] });
        Assert.Equal("La commande est vide.", result!.Message);
    }

    [Fact]
    public async Task Unexpected_errors_get_a_generic_message()
    {
        var executor = new FakeExecutor { Behavior = _ => throw new InvalidOperationException("détail technique") };
        var result = await new ActionRunner(executor).RunAsync(new ActionItem { Steps = [new OpenStep()] });
        Assert.Equal("Windows n'a pas pu exécuter cette étape.", result!.Message);
    }

    [Fact]
    public async Task An_action_without_steps_reports_it()
    {
        var result = await new ActionRunner(new FakeExecutor()).RunAsync(new ActionItem());
        Assert.False(result!.Success);
        Assert.Equal("Cette action n'a aucune étape. Ajoutez-en en mode Modifier (clic droit sur le rond du milieu).", result.Message);
    }

    [Fact]
    public async Task A_second_click_while_running_is_ignored()
    {
        var gate = new TaskCompletionSource();
        var runner = new ActionRunner(new FakeExecutor { Behavior = _ => gate.Task });
        var action = new ActionItem { Steps = [new WaitStep()] };

        var first = runner.RunAsync(action);
        Assert.True(runner.IsRunning(action));
        Assert.Null(await runner.RunAsync(action));
        gate.SetResult();
        Assert.True((await first)!.Success);
        Assert.False(runner.IsRunning(action));
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        var runner = new ActionRunner(new FakeExecutor { Behavior = _ => throw new OperationCanceledException() });
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            runner.RunAsync(new ActionItem { Steps = [new WaitStep()] }));
    }
}
