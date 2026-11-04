using Grpc.Core;

namespace ProtoFast.Api.IntegrationTests;

public sealed class RecordingStreamWriter<T> : IServerStreamWriter<T>
{
    public List<T> Messages { get; } = [];

    public WriteOptions? WriteOptions { get; set; }

    public Task WriteAsync(T message) => WriteAsync(message, CancellationToken.None);

    public Task WriteAsync(T message, CancellationToken cancellationToken)
    {
        Messages.Add(message);
        return Task.CompletedTask;
    }
}
