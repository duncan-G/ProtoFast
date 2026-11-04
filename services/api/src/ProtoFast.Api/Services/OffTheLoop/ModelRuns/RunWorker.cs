namespace ProtoFast.Api.Services.OffTheLoop.ModelRuns;

/// <summary>
/// Claims queued runs and works on up to <see cref="RunOptions.Concurrency"/> at once. A run queued
/// on this instance wakes it at once; runs queued elsewhere, or left by an instance that died, are
/// found by polling.
/// </summary>
public sealed class RunWorker(
    RunStore store,
    RunExecutor executor,
    RunSignal signal,
    RunOptions options,
    ILogger<RunWorker> logger) : BackgroundService
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
                while (running.Count < options.Concurrency && await store.ClaimAsync(stoppingToken) is { } run)
                {
                    running.Add(Task.Run(() => executor.RunAsync(run, stoppingToken), CancellationToken.None)
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
                logger.LogWarning(e, "Loop runs could not be claimed");
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

        // Each run hands itself back to the queue as it stops.
        await Task.WhenAll(running);
    }
}
