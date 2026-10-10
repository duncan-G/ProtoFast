using OpenTelemetry;

namespace ProtoFast.Api.Services.Jobs;

/// <summary>
/// Claims queued director jobs and runs up to <see cref="DirectorJobOptions.Concurrency"/> at once.
/// A job queued on this instance wakes it at once; jobs queued elsewhere, or left by an instance
/// that died, are found by polling.
/// </summary>
public sealed class DirectorJobWorker(
    DirectorJobStore store,
    DirectorJobRunner runner,
    DirectorJobSignal signal,
    DirectorJobOptions options,
    ILogger<DirectorJobWorker> logger) : BackgroundService
{
    private static readonly TimeSpan SweepEvery = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        List<Task> running = [];
        var nextSweep = DateTime.UtcNow;
        while (!stoppingToken.IsCancellationRequested)
        {
            running.RemoveAll(t => t.IsCompleted);
            try
            {
                while (running.Count < options.Concurrency && await ClaimAsync(stoppingToken) is { } job)
                {
                    running.Add(Task.Run(() => runner.RunAsync(job, stoppingToken), CancellationToken.None)
                        .ContinueWith(_ => signal.Wake(), TaskScheduler.Default));
                }

                if (DateTime.UtcNow >= nextSweep)
                {
                    nextSweep = DateTime.UtcNow + SweepEvery;
                    await store.SweepAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Director jobs could not be claimed");
            }

            try
            {
                await signal.WaitAsync(options.PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        // Each run hands its job back to the queue as it stops.
        await Task.WhenAll(running);
    }

    /// <summary>Polls that find nothing would each be a root trace, so claims are not traced.</summary>
    private async Task<ClaimedJob?> ClaimAsync(CancellationToken ct)
    {
        using var quiet = SuppressInstrumentationScope.Begin();
        return await store.ClaimAsync(ct);
    }
}
