using Microsoft.Extensions.Logging;
using ProtoFast.Storage.Abstractions;

namespace ProtoFast.Storage;

/// <summary>
/// Keeps a received message invisible for as long as it is being worked on, by renewing its
/// visibility timeout in the background until disposed. A renewal that fails is retried on the
/// next tick; once a whole timeout passes without one succeeding the message is back on the queue
/// for another consumer, and <see cref="Token"/> is cancelled so this one stops duplicating it.
/// </summary>
public sealed class MessageLease : IAsyncDisposable
{
    private readonly IMessageQueue _queue;
    private readonly string _receiptHandle;
    private readonly TimeSpan _timeout;
    private readonly DateTimeOffset _receivedAt;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _lost;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _renewal;

    private MessageLease(
        IMessageQueue queue,
        string receiptHandle,
        TimeSpan timeout,
        DateTimeOffset receivedAt,
        ILogger logger,
        TimeProvider time,
        CancellationToken ct)
    {
        _queue = queue;
        _receiptHandle = receiptHandle;
        _timeout = timeout;
        _receivedAt = receivedAt;
        _time = time;
        _logger = logger;
        _lost = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _renewal = RenewAsync();
    }

    /// <summary>
    /// Cancelled when the lease is lost, or when the caller's own token is. Work on the message
    /// should run under it.
    /// </summary>
    public CancellationToken Token => _lost.Token;

    /// <summary>True once the message has lapsed back onto the queue while this lease was held.</summary>
    public bool IsLost { get; private set; }

    /// <summary>
    /// Holds a message given <paramref name="timeout"/> of visibility at <paramref name="receivedAt"/>,
    /// which is the queue's own timeout unless the receive set another. A message held some time
    /// after its receive is renewed sooner, or at once.
    /// </summary>
    public static MessageLease Hold(
        IMessageQueue queue,
        string receiptHandle,
        TimeSpan timeout,
        DateTimeOffset receivedAt,
        ILogger logger,
        CancellationToken ct,
        TimeProvider? time = null) =>
        new(queue, receiptHandle, timeout, receivedAt, logger, time ?? TimeProvider.System, ct);

    public static MessageLease Hold<T>(
        IMessageQueue queue, QueueMessage<T> message, ILogger logger, CancellationToken ct, TimeProvider? time = null) =>
        Hold(queue, message.ReceiptHandle, message.Visibility, message.ReceivedAt, logger, ct, time);

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        try
        {
            await _renewal;
        }
        catch (OperationCanceledException)
        {
        }

        _stop.Dispose();
        _lost.Dispose();
    }

    // Renewing at a third of the timeout leaves two more tries before a failed one lets it lapse.
    private async Task RenewAsync()
    {
        var period = _timeout / 3;
        var visibleAt = _receivedAt + _timeout;
        var renewAt = _receivedAt + period;
        while (true)
        {
            var wait = renewAt - _time.GetUtcNow();
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, _time, _stop.Token);
            }

            _stop.Token.ThrowIfCancellationRequested();
            if (_lost.IsCancellationRequested)
            {
                return;
            }

            var renewedAt = _time.GetUtcNow();
            renewAt = renewedAt + period;
            try
            {
                await _queue.ExtendVisibilityAsync(_receiptHandle, _timeout, _stop.Token);
                visibleAt = renewedAt + _timeout;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                if (_time.GetUtcNow() < visibleAt)
                {
                    _logger.LogWarning(e, "Renewing a message's visibility on {Queue} failed; retrying", _queue.Name);
                    continue;
                }

                _logger.LogError(e, "A message on {Queue} has lapsed back onto the queue while still being worked", _queue.Name);
                IsLost = true;
                await _lost.CancelAsync();
                return;
            }
        }
    }
}
