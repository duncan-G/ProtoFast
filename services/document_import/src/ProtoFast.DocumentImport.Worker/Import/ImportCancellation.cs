using ProtoFast.DocumentImport.Engine.Storage;

namespace ProtoFast.DocumentImport.Worker.Import;

/// <summary>
/// Polls the ledger while an import runs and cancels <see cref="Token"/> once its owner has
/// cancelled it. A poll that fails is tried again on the next tick.
/// </summary>
public sealed class ImportCancellation : IAsyncDisposable
{
    private readonly IRunLedger _ledger;
    private readonly string _uploadId;
    private readonly TimeSpan _interval;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cancelled;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _watch;

    private ImportCancellation(
        IRunLedger ledger, string uploadId, TimeSpan interval, ILogger logger, TimeProvider time, CancellationToken ct)
    {
        _ledger = ledger;
        _uploadId = uploadId;
        _interval = interval;
        _time = time;
        _logger = logger;
        _cancelled = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _watch = WatchAsync();
    }

    /// <summary>Cancelled when the owner cancels the import, or when the caller's own token is.</summary>
    public CancellationToken Token => _cancelled.Token;

    public bool IsCancelled { get; private set; }

    public static ImportCancellation Watch(
        IRunLedger ledger, string uploadId, TimeSpan interval, ILogger logger, CancellationToken ct, TimeProvider? time = null) =>
        new(ledger, uploadId, interval, logger, time ?? TimeProvider.System, ct);

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
                var progress = await _ledger.ProgressAsync([_uploadId], _stop.Token);
                if (progress.GetValueOrDefault(_uploadId)?.Phase == RunPhase.Cancelled)
                {
                    IsCancelled = true;
                    await _cancelled.CancelAsync();
                    return;
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _logger.LogWarning(e, "Checking whether upload {UploadId} was cancelled failed; retrying", _uploadId);
            }
        }
    }
}
