using ProtoFast.DocumentImport.Engine.Briefing;
using ProtoFast.DocumentImport.Screenplay.Briefing;

namespace ProtoFast.DocumentImport.Worker.Briefing;

/// <summary>
/// Briefs runs after they end, one at a time and off the import path: a briefing that fails is
/// tried again on a later pass and never touches the run or its source's progress.
/// </summary>
public sealed class RunBriefingService(
    IRunBriefs briefs,
    RunBriefer briefer,
    RunBriefingOptions options,
    TimeProvider time,
    ILogger<RunBriefingService> logger) : BackgroundService
{
    private const int Batch = 10;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Run briefing is disabled");
            return;
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    while (await BriefNextAsync(stoppingToken))
                    {
                    }
                }
                catch (Exception e) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(e, "Looking for runs to brief failed");
                }

                await Task.Delay(options.PollInterval, time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>True when a run was briefed, so there may be more to do at once; a failure waits for the next pass.</summary>
    internal async Task<bool> BriefNextAsync(CancellationToken ct)
    {
        foreach (var run in await briefs.PendingAsync(options.MaxAttempts, options.StaleAfter, Batch, ct))
        {
            if (!await briefs.ClaimAsync(run.RunId, options.MaxAttempts, options.StaleAfter, ct))
            {
                continue;
            }

            try
            {
                await briefs.CompleteAsync(run.RunId, await briefer.BriefAsync(run, ct), ct);
                return true;
            }
            catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(e, "Briefing run {RunId} failed", run.RunId);
                await briefs.FailAsync(run.RunId, $"{e.GetType().Name}: {e.Message}", ct);
                return false;
            }
        }

        return false;
    }
}
