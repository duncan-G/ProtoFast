using Grpc.Core;

namespace ProtoFast.Api.Services.OffTheLoop.ModelRuns;

/// <summary>Takes a streaming call's replies for a run, in place of the caller's connection.</summary>
public sealed class RunStreamWriter<T>(Func<T, Task> onReply) : IServerStreamWriter<T>
{
    public WriteOptions? WriteOptions { get; set; }

    public Task WriteAsync(T message) => onReply(message);

    /// <summary>The interface's default refuses any token that can be cancelled.</summary>
    public Task WriteAsync(T message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return onReply(message);
    }
}
