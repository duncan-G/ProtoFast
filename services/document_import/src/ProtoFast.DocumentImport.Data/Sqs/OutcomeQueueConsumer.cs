using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProtoFast.DocumentImport.Engine.Learning;
using ProtoFast.Storage.Abstractions;

namespace ProtoFast.DocumentImport.Data.Sqs;

/// <summary>
/// Applies queued outcomes to policy. A message is deleted only once applied; one that fails is
/// redelivered after its visibility timeout and dead-lettered after the queue's receive limit.
/// </summary>
public sealed class OutcomeQueueConsumer(
    [FromKeyedServices(SqsOutcomeQueue.QueueKey)] IMessageQueue queue,
    IPolicyUpdater updater,
    ILogger<OutcomeQueueConsumer> logger) : BackgroundService
{
    private static readonly TimeSpan ReceiveRetryDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ApplyBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Receiving outcomes failed");
                await Task.Delay(ReceiveRetryDelay, stoppingToken);
            }
        }
    }

    private async Task ApplyBatchAsync(CancellationToken ct)
    {
        var messages = await queue.ReceiveAsync<Outcome>(ct);

        // A batch can hold several outcomes for one family. After one fails, the rest of that
        // family's are left for redelivery so they are still applied in order.
        var failedFamilies = new HashSet<string>(StringComparer.Ordinal);
        foreach (var message in messages)
        {
            var outcome = message.Body;
            if (failedFamilies.Contains(outcome.Family))
            {
                continue;
            }

            try
            {
                await updater.ApplyAsync(outcome, ct);
                await queue.DeleteAsync(message.ReceiptHandle, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                failedFamilies.Add(outcome.Family);
                logger.LogError(e, "Policy update failed for {Kind} on {Family}/{StageId}",
                    outcome.Kind, outcome.Family, outcome.StageId);
            }
        }
    }
}
