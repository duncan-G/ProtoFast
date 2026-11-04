using Grpc.Core;

namespace ProtoFast.Api.Services.OffTheLoop.ModelRuns;

/// <summary>What a run hands the Writer or Researcher in place of a live call: only the cancellation is real.</summary>
public sealed class RunCallContext(CancellationToken cancellationToken) : ServerCallContext
{
    protected override string MethodCore => "loop-run";

    protected override string HostCore => "localhost";

    protected override string PeerCore => "loop-run";

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
