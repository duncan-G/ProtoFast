using Microsoft.Extensions.Options;
using ProtoFast.DocumentImport.Core;
using ProtoFast.DocumentImport.History.Runs;

namespace ProtoFast.DocumentImport.Worker.HistoricalStories;

/// <summary>
/// Takes queued story runs from the history database, up to <see cref="HistoricalStoryOptions.MaxConcurrentRuns"/>
/// at once. A failed run is queued again until it has used its attempts; one whose worker died is
/// taken over once its heartbeat is stale.
/// </summary>
public sealed class HistoricalStoryConsumer(
    StoryRunLedger ledger,
    IServiceScopeFactory scopes,
    IOptions<HistoricalStoryOptions> options,
    ILogger<HistoricalStoryConsumer> logger) : BackgroundService
{
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var maxRuns = Math.Max(1, settings.MaxConcurrentRuns);
        using var slots = new SemaphoreSlim(maxRuns, maxRuns);
        var running = new List<Task>();
        logger.LogInformation("Waiting for historical story runs, up to {MaxRuns} at once", maxRuns);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                running.RemoveAll(run => run.IsCompleted);
                await slots.WaitAsync(stoppingToken);
                StoryRun? run;
                try
                {
                    run = await ledger.ClaimAsync(settings.StaleAfter, settings.MaxAttempts, stoppingToken);
                }
                catch (Exception e) when (!stoppingToken.IsCancellationRequested)
                {
                    slots.Release();
                    logger.LogError(e, "Claiming a historical story run failed");
                    await Task.Delay(ErrorDelay, stoppingToken);
                    continue;
                }

                if (run is null)
                {
                    slots.Release();
                    await Task.Delay(settings.PollInterval, stoppingToken);
                    continue;
                }

                running.Add(Task.Run(() => RunInSlotAsync(run, slots, stoppingToken), CancellationToken.None));
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            await Task.WhenAll(running);
        }
    }

    private async Task RunInSlotAsync(StoryRun run, SemaphoreSlim slots, CancellationToken ct)
    {
        try
        {
            await RunAsync(run, ct);
        }
        finally
        {
            slots.Release();
        }
    }

    private async Task RunAsync(StoryRun run, CancellationToken ct)
    {
        using var activity = DocumentImportTelemetry.Source.StartActivity($"history story {run.Id}");
        activity?.SetTag("history.run_id", run.Id);
        activity?.SetTag("history.attempt", run.Attempts);
        await using var watch = RunWatch.Start(ledger, run.Id, options.Value.HeartbeatInterval, logger, ct);
        try
        {
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<HistoricalStoryRunner>().RunAsync(run, watch.Token);
            logger.LogInformation("Story run {RunId} is written", run.Id);
        }
        catch (OperationCanceledException) when (watch.IsCancelled)
        {
            logger.LogInformation("Story run {RunId} was cancelled", run.Id);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutting down: the run's heartbeat goes stale and the next worker takes it over.
        }
        catch (Exception e)
        {
            activity.Fail(e);
            var final = run.Attempts >= options.Value.MaxAttempts;
            logger.LogError(e, "Story run {RunId} failed on attempt {Attempt}", run.Id, run.Attempts);
            try
            {
                await ledger.AddEventAsync(run.Id, "error", e.Message, e.ToString(), 0, CancellationToken.None);
                await ledger.FailAsync(run.Id, e.Message, final, CancellationToken.None);
            }
            catch (Exception failure)
            {
                logger.LogWarning(failure, "The failure of story run {RunId} could not be recorded", run.Id);
            }
        }
    }
}
