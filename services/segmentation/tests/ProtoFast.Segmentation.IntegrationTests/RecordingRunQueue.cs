using ProtoFast.Segmentation.Storage;

namespace ProtoFast.Segmentation.IntegrationTests;

/// <summary>Records what would have been sent to SQS, so a test can assert on submissions.</summary>
public sealed class RecordingRunQueue : IRunQueue
{
    public List<RunMessage> Runs { get; } = [];

    public List<(BatchPollMessage Message, TimeSpan Delay)> BatchPolls { get; } = [];

    public Task SendRunAsync(RunMessage message, CancellationToken ct = default)
    {
        Runs.Add(message);
        return Task.CompletedTask;
    }

    public Task SendBatchPollAsync(BatchPollMessage message, TimeSpan delay, CancellationToken ct = default)
    {
        BatchPolls.Add((message, delay));
        return Task.CompletedTask;
    }

    public string QueueUrlFor(Core.Model.RunPriority priority) => $"https://sqs.test/{priority}";
}
