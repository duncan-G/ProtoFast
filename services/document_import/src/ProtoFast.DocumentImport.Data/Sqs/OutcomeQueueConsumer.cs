using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProtoFast.DocumentImport.Core;
using ProtoFast.DocumentImport.Engine.Learning;
using ProtoFast.Storage;
using ProtoFast.Storage.Abstractions;

namespace ProtoFast.DocumentImport.Data.Sqs;

/// <summary>
/// Applies queued outcomes to policy, one at a time: the FIFO queue holds back a family's next
/// outcome until this one is deleted, so each family's are applied in order. A message is deleted
/// only once applied; one that fails is redelivered after its visibility timeout and
/// dead-lettered after the queue's receive limit.
/// </summary>
public sealed class OutcomeQueueConsumer(
    [FromKeyedServices(SqsOutcomeQueue.QueueKey)] IMessageQueue queue,
    IPolicyUpdater updater,
    IHostApplicationLifetime lifetime,
    ILogger<OutcomeQueueConsumer> logger) : QueueConsumer<Outcome>(queue, lifetime, logger)
{
    protected override async Task HandleAsync(QueueMessage<Outcome> message, MessageLease lease)
    {
        var outcome = message.Body;
        using var activity = DocumentImportTelemetry.StartProcess(Queue.Name, message.Parent);
        activity?.SetTag("document_import.family", outcome.Family);
        activity?.SetTag("document_import.outcome", outcome.Kind.ToString());
        try
        {
            await updater.ApplyAsync(outcome, lease.Token);
            await Queue.DeleteAsync(message.ReceiptHandle, lease.Token);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            activity.Fail(e);
            logger.LogError(e, "Policy update failed for {Kind} on {Family}/{StageId}",
                outcome.Kind, outcome.Family, outcome.StageId);
        }
    }
}
