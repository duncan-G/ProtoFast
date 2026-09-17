using Grpc.Core;
using ProtoFast.Api;
using ProtoFast.Segmentation.Storage;

namespace ProtoFast.Segmentation.IntegrationTests;

/// <summary>
/// The rules of plan §17: ownership comes from the token, submission is idempotent, and no caller
/// can presign an artifact outside its own run. Each of these is a security property, not a
/// convenience — so each gets a test that fails if the property is removed.
/// </summary>
public class SegmentationServiceTests
{
    private const string Alice = "alice-subject";
    private const string Bob = "bob-subject";

    [Fact]
    public async Task CreateUploadRecordsTheSlotAndPresignsIt()
    {
        using var fixture = new SegmentationServiceFixture();

        var reply = await fixture.Service.CreateUpload(
            new CreateUploadRequest { FileName = "paper.md", SizeBytes = 4096, WithLayout = true },
            SegmentationServiceFixture.CallerContext(Alice));

        Assert.NotEmpty(reply.UploadId);
        Assert.Contains("method=PUT", reply.MarkdownPutUrl, StringComparison.Ordinal);
        Assert.NotEmpty(reply.LayoutPutUrl);
        // The signature covers Content-Type, so the browser has to be told what to send back.
        Assert.Equal("text/markdown", reply.RequiredHeaders["content-type"]);

        var upload = Assert.Single(fixture.Db.Uploads);
        Assert.Equal(Alice, upload.OwnerSubject);
    }

    [Fact]
    public async Task CreateUploadRefusesAnImplausibleSize()
    {
        using var fixture = new SegmentationServiceFixture();

        var failure = await Assert.ThrowsAsync<RpcException>(() => fixture.Service.CreateUpload(
            new CreateUploadRequest { FileName = "huge.md", SizeBytes = 512L * 1024 * 1024 },
            SegmentationServiceFixture.CallerContext(Alice)));

        Assert.Equal(StatusCode.InvalidArgument, failure.StatusCode);
    }

    [Fact]
    public async Task SubmitRunEnqueuesExactlyOneMessage()
    {
        using var fixture = new SegmentationServiceFixture();
        var uploadId = await UploadAsync(fixture, Alice);

        var reply = await fixture.Service.SubmitRun(
            Submit(uploadId, "key-1"), SegmentationServiceFixture.CallerContext(Alice));

        Assert.NotEmpty(reply.RunId);
        var message = Assert.Single(fixture.Queue.Runs);
        Assert.Equal(reply.RunId, message.RunId);

        // The ladder is seeded so ThePlot can draw all thirteen phases immediately.
        Assert.Equal(13, fixture.Db.RunPhases.Count());
    }

    [Fact]
    public async Task TheSameIdempotencyKeyReturnsTheSameRunAndDoesNotEnqueueAgain()
    {
        using var fixture = new SegmentationServiceFixture();
        var uploadId = await UploadAsync(fixture, Alice);
        var context = SegmentationServiceFixture.CallerContext(Alice);

        var first = await fixture.Service.SubmitRun(Submit(uploadId, "same-key"), context);
        var second = await fixture.Service.SubmitRun(Submit(uploadId, "same-key"), context);

        Assert.Equal(first.RunId, second.RunId);
        // A retried submission must not be a second run and a second bill.
        Assert.Single(fixture.Queue.Runs);
    }

    [Fact]
    public async Task SubmitRunRequiresAnIdempotencyKey()
    {
        using var fixture = new SegmentationServiceFixture();
        var uploadId = await UploadAsync(fixture, Alice);

        var failure = await Assert.ThrowsAsync<RpcException>(() => fixture.Service.SubmitRun(
            Submit(uploadId, string.Empty), SegmentationServiceFixture.CallerContext(Alice)));

        Assert.Equal(StatusCode.InvalidArgument, failure.StatusCode);
    }

    [Fact]
    public async Task AnotherUsersUploadIsNotFound()
    {
        using var fixture = new SegmentationServiceFixture();
        var uploadId = await UploadAsync(fixture, Alice);

        // NotFound rather than PermissionDenied: the two cases must be indistinguishable, or the
        // RPC becomes a way to probe which upload ids exist.
        var failure = await Assert.ThrowsAsync<RpcException>(() => fixture.Service.SubmitRun(
            Submit(uploadId, "bobs-key"), SegmentationServiceFixture.CallerContext(Bob)));

        Assert.Equal(StatusCode.NotFound, failure.StatusCode);
    }

    [Fact]
    public async Task AnUploadThatNeverArrivedIsRefused()
    {
        using var fixture = new SegmentationServiceFixture();

        // The slot is recorded but the browser never PUT the file.
        var created = await fixture.Service.CreateUpload(
            new CreateUploadRequest { FileName = "paper.md", SizeBytes = 100 },
            SegmentationServiceFixture.CallerContext(Alice));

        var failure = await Assert.ThrowsAsync<RpcException>(() => fixture.Service.SubmitRun(
            Submit(created.UploadId, "key"), SegmentationServiceFixture.CallerContext(Alice)));

        Assert.Equal(StatusCode.FailedPrecondition, failure.StatusCode);
    }

    [Fact]
    public async Task AnUnknownAugmentationTypeIsRefusedWithTheAvailableOnes()
    {
        using var fixture = new SegmentationServiceFixture();
        var uploadId = await UploadAsync(fixture, Alice);

        var request = Submit(uploadId, "key");
        request.Augmentations.Add("summarise-everything");

        var failure = await Assert.ThrowsAsync<RpcException>(() => fixture.Service.SubmitRun(
            request, SegmentationServiceFixture.CallerContext(Alice)));

        Assert.Equal(StatusCode.InvalidArgument, failure.StatusCode);
        Assert.Contains("key-points", failure.Status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnotherUsersRunIsNotFound()
    {
        using var fixture = new SegmentationServiceFixture();
        var runId = await SubmitAsync(fixture, Alice);

        var failure = await Assert.ThrowsAsync<RpcException>(() => fixture.Service.GetRun(
            new GetRunRequest { RunId = runId }, SegmentationServiceFixture.CallerContext(Bob)));

        Assert.Equal(StatusCode.NotFound, failure.StatusCode);
    }

    [Fact]
    public async Task GetArtifactRefusesAKeyOutsideTheRun()
    {
        using var fixture = new SegmentationServiceFixture();
        var runId = await SubmitAsync(fixture, Alice);

        // The key is client-supplied. Without the prefix check, a caller who owns any run could
        // presign any object in the bucket — including another user's document.
        var failure = await Assert.ThrowsAsync<RpcException>(() => fixture.Service.GetArtifact(
            new GetArtifactRequest { RunId = runId, ArtifactKey = "runs/some-other-run/09_frozen.json" },
            SegmentationServiceFixture.CallerContext(Alice)));

        Assert.Equal(StatusCode.PermissionDenied, failure.StatusCode);
    }

    [Fact]
    public async Task GetArtifactRefusesPathTraversal()
    {
        using var fixture = new SegmentationServiceFixture();
        var runId = await SubmitAsync(fixture, Alice);

        var failure = await Assert.ThrowsAsync<RpcException>(() => fixture.Service.GetArtifact(
            new GetArtifactRequest { RunId = runId, ArtifactKey = $"runs/{runId}/../../uploads/{Bob}/x.md" },
            SegmentationServiceFixture.CallerContext(Alice)));

        Assert.Equal(StatusCode.PermissionDenied, failure.StatusCode);
    }

    [Fact]
    public async Task GetArtifactPresignsAKeyInsideTheRun()
    {
        using var fixture = new SegmentationServiceFixture();
        var runId = await SubmitAsync(fixture, Alice);

        var key = ArtifactKeys.Phase(runId, Core.Model.PipelinePhase.Triage);
        await fixture.Artifacts.WriteAsync(key, new { ok = true }, "test", TestContext.Current.CancellationToken);

        var reply = await fixture.Service.GetArtifact(
            new GetArtifactRequest { RunId = runId, ArtifactKey = key },
            SegmentationServiceFixture.CallerContext(Alice));

        Assert.Contains("method=GET", reply.GetUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelMarksTheRunForTheWorkerToNotice()
    {
        using var fixture = new SegmentationServiceFixture();
        var runId = await SubmitAsync(fixture, Alice);

        var run = await fixture.Service.CancelRun(
            new GetRunRequest { RunId = runId }, SegmentationServiceFixture.CallerContext(Alice));

        Assert.True(run.Cancelled);
        // The worker observes this at its next superstep, so the run stops between phases rather
        // than inside a provider call it has already paid for.
        Assert.Contains(fixture.Db.RunEvents, e => e.Message.Contains("cancelled", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RerunFromResetsTheLadderAndRequeuesWithThePhase()
    {
        using var fixture = new SegmentationServiceFixture();
        var runId = await SubmitAsync(fixture, Alice);

        foreach (var phase in fixture.Db.RunPhases)
        {
            phase.State = Core.Model.PhaseState.Done;
        }

        await fixture.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await fixture.Service.RerunFrom(
            new RerunFromRequest { RunId = runId, FromPhase = 5 },
            SegmentationServiceFixture.CallerContext(Alice));

        var phases = fixture.Db.RunPhases.ToList();
        Assert.All(
            phases.Where(p => (int)p.Phase >= 5),
            p => Assert.Equal(Core.Model.PhaseState.Pending, p.State));
        Assert.All(
            phases.Where(p => (int)p.Phase < 5),
            p => Assert.Equal(Core.Model.PhaseState.Done, p.State));

        Assert.Equal(5, fixture.Queue.Runs[^1].FromPhase);
    }

    [Fact]
    public async Task RerunFromRejectsAPhaseThatDoesNotExist()
    {
        using var fixture = new SegmentationServiceFixture();
        var runId = await SubmitAsync(fixture, Alice);

        var failure = await Assert.ThrowsAsync<RpcException>(() => fixture.Service.RerunFrom(
            new RerunFromRequest { RunId = runId, FromPhase = 99 },
            SegmentationServiceFixture.CallerContext(Alice)));

        Assert.Equal(StatusCode.InvalidArgument, failure.StatusCode);
    }

    [Fact]
    public async Task ListModelsRequiresTheAdminRole()
    {
        using var fixture = new SegmentationServiceFixture();

        var failure = await Assert.ThrowsAsync<RpcException>(() => fixture.Service.ListModels(
            new ListModelsRequest(), SegmentationServiceFixture.CallerContext(Alice)));

        Assert.Equal(StatusCode.PermissionDenied, failure.StatusCode);
        // The message must not name the missing role — that is free reconnaissance.
        Assert.DoesNotContain("segmentation-admin", failure.Status.Detail, StringComparison.Ordinal);

        var allowed = await fixture.Service.ListModels(
            new ListModelsRequest(),
            SegmentationServiceFixture.CallerContext(Alice, "segmentation-admin"));

        Assert.NotNull(allowed);
    }

    [Fact]
    public async Task ACallWithNoPrincipalIsUnauthenticated()
    {
        using var fixture = new SegmentationServiceFixture();

        // The interceptor would normally have rejected this already; the service refuses anyway,
        // because "the interceptor ran" is not something a method can verify.
        var failure = await Assert.ThrowsAsync<RpcException>(() => fixture.Service.GetRun(
            new GetRunRequest { RunId = "anything" }, TestServerCallContext.Create()));

        Assert.Equal(StatusCode.Unauthenticated, failure.StatusCode);
    }

    private static SubmitRunRequest Submit(string uploadId, string idempotencyKey) => new()
    {
        UploadId = uploadId,
        DocumentId = "paper.md",
        Sensitivity = Sensitivity.Internal,
        Priority = Priority.Realtime,
        IdempotencyKey = idempotencyKey,
    };

    private static async Task<string> UploadAsync(SegmentationServiceFixture fixture, string subject)
    {
        var created = await fixture.Service.CreateUpload(
            new CreateUploadRequest { FileName = "paper.md", SizeBytes = 4096 },
            SegmentationServiceFixture.CallerContext(subject));

        // Stand in for the browser's presigned PUT.
        await fixture.Artifacts.WriteTextAsync(
            ArtifactKeys.Upload(subject, created.UploadId), "# Title\n\nBody.\n", "upload");

        return created.UploadId;
    }

    private static async Task<string> SubmitAsync(SegmentationServiceFixture fixture, string subject)
    {
        var uploadId = await UploadAsync(fixture, subject);
        var reply = await fixture.Service.SubmitRun(
            Submit(uploadId, Guid.NewGuid().ToString("N")),
            SegmentationServiceFixture.CallerContext(subject));

        return reply.RunId;
    }
}
