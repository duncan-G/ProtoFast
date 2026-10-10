using Grpc.Core;

namespace ProtoFast.Api.Services.Jobs;

/// <summary>What a job step hands the Direction service in place of a live call: only the cancellation is real.</summary>
public sealed class JobCallContext(CancellationToken cancellationToken) : ServerCallContext
{
    protected override string MethodCore => "director-job";

    protected override string HostCore => "localhost";

    protected override string PeerCore => "director-job";

    protected override DateTime DeadlineCore => DateTime.MaxValue;

    protected override Metadata RequestHeadersCore { get; } = new();

    protected override CancellationToken CancellationTokenCore => cancellationToken;

    protected override Metadata ResponseTrailersCore { get; } = new();

    protected override Status StatusCore { get; set; }

    protected override WriteOptions? WriteOptionsCore { get; set; }

    protected override AuthContext AuthContextCore { get; } = new(null, new Dictionary<string, List<AuthProperty>>());

    protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) =>
        throw new NotSupportedException();

    protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
}
