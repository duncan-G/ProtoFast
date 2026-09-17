using System.Security.Claims;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProtoFast.Api;
using ProtoFast.Segmentation.Data;
using ProtoFast.Segmentation.Storage;
using ProtoFast.ServiceDefaults.InternalAuth;

namespace ProtoFast.Segmentation.IntegrationTests;

/// <summary>
/// The real <see cref="SegmentationService"/> over an in-memory database, artifact store and
/// queue.
///
/// <para>These tests are about the rules the service enforces — ownership from the token,
/// submission idempotency, artifact-key scoping — and those are pure logic that a container would
/// only slow down. What a container would add (Npgsql's SQL generation, real presigning) is
/// covered by running the stack under <c>aspire run</c>.</para>
/// </summary>
public sealed class SegmentationServiceFixture : IDisposable
{
    public SegmentationDbContext Db { get; }

    public InMemoryArtifactStore Artifacts { get; } = new();

    public RecordingRunQueue Queue { get; } = new();

    public SegmentationService Service { get; }

    public SegmentationServiceFixture()
    {
        Db = new SegmentationDbContext(
            new DbContextOptionsBuilder<SegmentationDbContext>()
                .UseInMemoryDatabase($"segmentation-{Guid.NewGuid():N}")
                // The in-memory provider cannot honour the jsonb column types or the unique index
                // the way Postgres does; the warning is expected and the tests assert the
                // behaviour the service itself enforces rather than what the index would.
                .ConfigureWarnings(w => w.Ignore(
                    Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        Service = new SegmentationService(
            Db,
            Artifacts,
            Artifacts,
            Queue,
            Options.Create(new SegmentationApiOptions { AllowedAugmentations = ["key-points"] }),
            Options.Create(new StorageOptions { Bucket = "test-bucket" }),
            NullLogger<SegmentationService>.Instance);
    }

    /// <summary>
    /// A call context carrying a validated principal, exactly as
    /// <see cref="InternalJwtAuthInterceptor"/> leaves one. The service reads the subject from
    /// here and from nowhere else, which is the property these tests exist to pin.
    /// </summary>
    public static ServerCallContext CallerContext(string subject, params string[] roles)
    {
        var identity = new ClaimsIdentity(
            [new Claim("sub", subject), .. roles.Select(r => new Claim("roles", r))],
            authenticationType: "internal",
            nameType: "sub",
            roleType: "roles");

        var context = TestServerCallContext.Create();
        context.UserState[InternalJwtAuthInterceptor.PrincipalKey] = new ClaimsPrincipal(identity);
        return context;
    }

    public void Dispose() => Db.Dispose();
}

/// <summary>
/// A minimal <see cref="ServerCallContext"/>. Grpc.Core ships no test double for this, and the
/// service only ever reads <c>UserState</c> and <c>CancellationToken</c> from it.
/// </summary>
public sealed class TestServerCallContext : ServerCallContext
{
    private readonly Dictionary<object, object> _userState = [];

    private TestServerCallContext()
    {
    }

    public static TestServerCallContext Create() => new();

    protected override string MethodCore => "/segmentation.Segmentation/Test";

    protected override string HostCore => "localhost";

    protected override string PeerCore => "ipv4:127.0.0.1:0";

    protected override DateTime DeadlineCore => DateTime.UtcNow.AddMinutes(5);

    protected override Metadata RequestHeadersCore { get; } = [];

    protected override CancellationToken CancellationTokenCore => CancellationToken.None;

    protected override Metadata ResponseTrailersCore { get; } = [];

    protected override Status StatusCore { get; set; }

    protected override WriteOptions? WriteOptionsCore { get; set; }

    protected override AuthContext AuthContextCore { get; } = new(null, new Dictionary<string, List<AuthProperty>>());

    protected override IDictionary<object, object> UserStateCore => _userState;

    protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) =>
        throw new NotSupportedException();

    protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
}
