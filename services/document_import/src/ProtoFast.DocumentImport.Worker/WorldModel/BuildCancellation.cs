using ProtoFast.Data.ThePlot.Entities;

namespace ProtoFast.DocumentImport.Worker.WorldModel;

/// <summary>
/// Polls the ledger while a build runs and cancels <see cref="Token"/> once its owner has
/// cancelled it. A poll that fails is tried again on the next tick.
/// </summary>
public sealed class BuildCancellation : IAsyncDisposable
{
    private readonly WorldModelLedger _ledger;
    private readonly Guid _buildId;
    private readonly string _userId;
    private readonly TimeSpan _interval;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cancelled;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _watch;

    private BuildCancellation(
        WorldModelLedger ledger, Guid buildId, string userId, TimeSpan interval, ILogger logger, TimeProvider time, CancellationToken ct)
    {
        _ledger = ledger;
        _buildId = buildId;
        _userId = userId;
        _interval = interval;
        _time = time;
        _logger = logger;
        _cancelled = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _watch = WatchAsync();
    }

    /// <summary>Cancelled when the owner cancels the build, or when the caller's own token is.</summary>
    public CancellationToken Token => _cancelled.Token;

    public bool IsCancelled { get; private set; }

    public static BuildCancellation Watch(
        WorldModelLedger ledger, Guid buildId, string userId, TimeSpan interval, ILogger logger, CancellationToken ct, TimeProvider? time = null) =>
        new(ledger, buildId, userId, interval, logger, time ?? TimeProvider.System, ct);

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
            await Task.Delay(_interval, _time, _stop.Token);
            try
            {
                if (await _ledger.StatusAsync(_buildId, _userId, _stop.Token) == WorldModelBuildStatus.Cancelled)
                {
                    IsCancelled = true;
                    await _cancelled.CancelAsync();
                    return;
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _logger.LogWarning(e, "Checking whether build {BuildId} was cancelled failed; retrying", _buildId);
            }
        }
    }
}
