using ProtoFast.Storage.Abstractions;

namespace ProtoFast.Api.IntegrationTests;

/// <summary>A queue that keeps what was sent to it, for asserting on; nothing is ever received.</summary>
public sealed class RecordingQueue(string name) : IMessageQueue
{
    public List<object> Sent { get; } = [];

    public bool Refuse { get; set; }

    public string Name => name;

    public Task SendAsync<T>(T message, string groupId, CancellationToken ct = default)
    {
        if (Refuse)
        {
            throw new InvalidOperationException("The queue is down.");
        }

        lock (Sent)
        {
            Sent.Add(message!);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<QueueMessage<T>>> ReceiveAsync<T>(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<QueueMessage<T>>>([]);

    public Task<IReadOnlyList<QueueMessage<T>>> ReceiveAsync<T>(int maxMessages, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<QueueMessage<T>>>([]);

    public Task DeleteAsync(string receiptHandle, CancellationToken ct = default) => Task.CompletedTask;

    public Task ExtendVisibilityAsync(string receiptHandle, TimeSpan timeout, CancellationToken ct = default) => Task.CompletedTask;
}
