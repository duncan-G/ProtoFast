namespace ProtoFast.Storage.Abstractions;

public sealed record QueueMessage<T>(T Body, string ReceiptHandle);
