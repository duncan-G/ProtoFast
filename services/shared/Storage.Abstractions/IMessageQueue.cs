namespace ProtoFast.Storage.Abstractions;

public interface IMessageQueue
{
    /// <summary>
    /// On a FIFO queue, messages with the same <paramref name="groupId"/> are delivered one at a
    /// time and in order; on a standard queue the group is ignored.
    /// </summary>
    Task SendAsync<T>(T message, string groupId, CancellationToken ct = default);

    /// <summary>Long-polls. An empty list means nothing arrived within the wait.</summary>
    Task<IReadOnlyList<QueueMessage<T>>> ReceiveAsync<T>(CancellationToken ct = default);

    /// <summary>
    /// Acknowledges a message. One that is never deleted is delivered again after its visibility
    /// timeout, and moves to the dead-letter queue after the queue's receive limit.
    /// </summary>
    Task DeleteAsync(string receiptHandle, CancellationToken ct = default);
}
