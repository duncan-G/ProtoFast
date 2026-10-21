using ProtoFast.DocumentImport.History.Runs;

namespace ProtoFast.DocumentImport.Worker.HistoricalStories;

/// <summary>
/// Beats the run's heartbeat while it works, so no other worker takes it over, and cancels
/// <see cref="Token"/> once the run is cancelled. A beat that fails is tried again on the next tick.
/// </summary>
public sealed class RunWatch : IAsyncDisposable
{
    private readonly StoryRunLedger _ledger;
    private readonly Guid _runId;
    private readonly TimeSpan _interval;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cancelled;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _watch;

    private RunWatch(StoryRunLedger ledger, Guid runId, TimeSpan interval, ILogger logger, CancellationToken ct)
    {
        _ledger = ledger;
        _runId = runId;
        _interval = interval;
        _logger = logger;
        _cancelled = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _watch = WatchAsync();
    }

    public CancellationToken Token => _cancelled.Token;

    public bool IsCancelled { get; private set; }

    public static RunWatch Start(StoryRunLedger ledger, Guid runId, TimeSpan interval, ILogger logger, CancellationToken ct) =>
        new(ledger, runId, interval, logger, ct);

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        try
        {
            await _watch;
        }
        catch (OperationCanceledException)
        {
        }

        _stop.Dispose();
        _cancelled.Dispose();
    }

    private async Task WatchAsync()
    {
        while (!_cancelled.IsCancellationRequested)
        {
            await Task.Delay(_interval, _stop.Token);
            try
            {
                if (await _ledger.HeartbeatAsync(_runId, _stop.Token) is null or StoryRunStatus.Cancelled)
                {
                    IsCancelled = true;
                    await _cancelled.CancelAsync();
                    return;
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _logger.LogWarning(e, "The heartbeat of story run {RunId} failed; retrying", _runId);
            }
        }
    }
}
