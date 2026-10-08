using System.Diagnostics;

namespace ProtoFast.Storage.Abstractions;

/// <param name="Visibility">
/// How long after the receive the message stays off the queue; work that may run longer has to
/// extend it, or the message is delivered again meanwhile.
/// </param>
/// <param name="ReceivedAt">
/// No later than when <see cref="Visibility"/> started to run, so a message that waits before it is
/// worked has less of it left.
/// </param>
/// <param name="Parent">The sender's trace context, or <c>default</c> when it sent none.</param>
/// <param name="IsLastDelivery">
/// True when the queue will not deliver it again: left undeleted, it moves to the dead-letter queue.
/// </param>
public sealed record QueueMessage<T>(
    T Body,
    string ReceiptHandle,
    TimeSpan Visibility,
    DateTimeOffset ReceivedAt,
    ActivityContext Parent = default,
    bool IsLastDelivery = false);
