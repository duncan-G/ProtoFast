namespace ProtoFast.Storage.Abstractions;

public interface IMessageQueue
{
    string Name { get; }

    /// <summary>
    /// On a FIFO queue, messages with the same <paramref name="groupId"/> are delivered one at a
    /// time and in order; on a standard queue the group is ignored. The current trace context
    /// travels with the message.
    /// </summary>
    Task SendAsync<T>(T message, string groupId, CancellationToken ct = default);

    /// <summary>Long-polls. An empty list means nothing arrived within the wait.</summary>
    Task<IReadOnlyList<QueueMessage<T>>> ReceiveAsync<T>(CancellationToken ct = default);

    /// <summary>As <see cref="ReceiveAsync{T}(CancellationToken)"/>, taking at most <paramref name="maxMessages"/>.</summary>
    Task<IReadOnlyList<QueueMessage<T>>> ReceiveAsync<T>(int maxMessages, CancellationToken ct = default);

    /// <summary>
    /// Acknowledges a message. One that is never deleted is delivered again after its visibility
    /// timeout, and moves to the dead-letter queue after the queue's receive limit.
    /// </summary>
    Task DeleteAsync(string receiptHandle, CancellationToken ct = default);

    /// <summary>
    /// Restarts a received message's visibility timeout at <paramref name="timeout"/> from now, so
    /// work that outlasts the queue's timeout can keep the message from being delivered again.
    /// Fails once the message has lapsed back onto the queue or has been invisible for the
    /// queue's hard limit (twelve hours on SQS).
    /// </summary>
    Task ExtendVisibilityAsync(string receiptHandle, TimeSpan timeout, CancellationToken ct = default);
}
