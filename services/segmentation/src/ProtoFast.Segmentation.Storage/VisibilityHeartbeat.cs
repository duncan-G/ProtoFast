using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;

namespace ProtoFast.Segmentation.Storage;

/// <summary>
/// Extends an in-flight message's visibility while its run is alive (plan §13.4).
///
/// <para>Without it the queue's timeout would be a ceiling on run duration: a long document would
/// be redelivered while the first worker was still working on it, and two workers would race on
/// the same run. With it, a healthy worker's run is never redelivered, and a dead worker's run
/// returns to the queue within one timeout — which is exactly the behaviour that makes a mid-run
/// deploy safe.</para>
/// </summary>
public sealed class VisibilityHeartbeat : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop;
    private readonly Task _loop;

    public VisibilityHeartbeat(
        IAmazonSQS sqs,
        string queueUrl,
        string receiptHandle,
        int visibilityTimeoutSeconds,
        ILogger logger,
        CancellationToken ct)
    {
        _stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loop = RunAsync(sqs, queueUrl, receiptHandle, visibilityTimeoutSeconds, logger, _stop.Token);
    }

    private static async Task RunAsync(
        IAmazonSQS sqs,
        string queueUrl,
        string receiptHandle,
        int visibilityTimeoutSeconds,
        ILogger logger,
        CancellationToken ct)
    {
        // Renew at a third of the timeout: two renewals may fail transiently before the message
        // becomes visible again, which is enough slack for a provider blip without making the
        // renewal itself a meaningful load on SQS.
        var interval = TimeSpan.FromSeconds(Math.Max(5, visibilityTimeoutSeconds / 3.0));

        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(interval, ct);

                await sqs.ChangeMessageVisibilityAsync(new ChangeMessageVisibilityRequest
                {
                    QueueUrl = queueUrl,
                    ReceiptHandle = receiptHandle,
                    VisibilityTimeout = visibilityTimeoutSeconds,
                }, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown: the run finished, or the worker is stopping.
        }
        catch (Exception ex)
        {
            // A failed renewal is not fatal to the run — the message may be redelivered, and the
            // resume path is idempotent — but it is worth knowing about.
            logger.LogWarning(ex, "Visibility heartbeat failed; the run may be redelivered.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();

        try
        {
            await _loop;
        }
        catch (OperationCanceledException)
        {
            // Expected.
        }

        _stop.Dispose();
    }
}
