using Microsoft.Extensions.DependencyInjection;
using ProtoFast.DocumentImport.Engine.Learning;
using ProtoFast.Storage.Abstractions;

namespace ProtoFast.DocumentImport.Data.Sqs;

/// <summary>
/// Groups outcomes by document family on a FIFO queue, so SQS delivers a family's outcomes one at
/// a time and the updater stays its single writer across every worker.
/// </summary>
public sealed class SqsOutcomeQueue([FromKeyedServices(SqsOutcomeQueue.QueueKey)] IMessageQueue queue) : IOutcomeQueue
{
    public const string QueueKey = "workflow-outcomes";

    public Task PublishAsync(Outcome outcome, CancellationToken ct) => queue.SendAsync(outcome, outcome.Family, ct);
}
