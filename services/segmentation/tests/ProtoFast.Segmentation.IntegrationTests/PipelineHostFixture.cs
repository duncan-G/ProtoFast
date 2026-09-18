using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ProtoFast.Segmentation.Data;
using ProtoFast.Segmentation.Data.Entities;
using ProtoFast.Segmentation.Pipeline;
using ProtoFast.Segmentation.Pipeline.Ingest;
using ProtoFast.Segmentation.Routing;
using ProtoFast.Segmentation.Storage;

namespace ProtoFast.Segmentation.IntegrationTests;

/// <summary>
/// The real MAF workflow and the real executors, over an in-memory artifact store and database.
///
/// <para>No provider is registered, which is the point: a clean Markdown document must reach
/// <c>publish</c> without one. Triage short-circuits labelling and the tree is built
/// deterministically from trusted headings, so the whole pipeline runs on code alone
/// (plan §9.4, §22.1). A test that needed an API key would not be a test anybody could run.</para>
/// </summary>
public sealed class PipelineHostFixture : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    public InMemoryArtifactStore Artifacts { get; } = new();

    /// <summary>
    /// The converter phase 0 calls. Its call count is the assertion behind "a Markdown upload
    /// never touches the converter" (ingest plan §24).
    /// </summary>
    public StubDocumentConverter Converter { get; } = new();

    public IWorkflowHost Host => _services.GetRequiredService<IWorkflowHost>();

    /// <summary>
    /// <paramref name="minWords"/> and <paramref name="maxWords"/> default to bounds no fixture
    /// can fall outside, so a test about phase wiring is not also a test about paragraph size.
    /// A test that is about the size bounds passes its own.
    /// </summary>
    public PipelineHostFixture(int minWords = 1, int maxWords = 1000)
    {
        var databaseName = $"segmentation-{Guid.NewGuid():N}";

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:Bucket"] = "test-bucket",
                ["Pipeline:Paragraphs:MinWords"] = minWords.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Pipeline:Paragraphs:MaxWords"] = maxWords.ToString(System.Globalization.CultureInfo.InvariantCulture),
                // No Routing:Models — the registry is empty, so any attempt to call a provider
                // fails loudly rather than silently degrading.
            })
            .Build();

        var services = new ServiceCollection();

        services.AddLogging(builder => builder
            .AddSimpleConsole()
            .SetMinimumLevel(Environment.GetEnvironmentVariable("PF_TEST_LOGS") is null ? LogLevel.Warning : LogLevel.Debug));
        services.AddSingleton<IConfiguration>(configuration);

        services.AddDbContext<SegmentationDbContext>(options => options
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)));

        services.AddSingleton<IArtifactStore>(Artifacts);
        services.AddSingleton<IPresignedUrlFactory>(Artifacts);
        services.AddSingleton<IRunQueue, RecordingRunQueue>();
        services.AddSingleton<Microsoft.Agents.AI.Workflows.Checkpointing.ICheckpointStore<System.Text.Json.JsonElement>, S3CheckpointStore>();

        services.AddSegmentationRouting(configuration);
        services.AddSegmentationPipeline(configuration);

        // Registered after AddSegmentationPipeline so it replaces the HTTP-backed converter: these
        // tests are about what phase 0 does with a conversion, not about the sidecar itself.
        services.AddSingleton<IDocumentConverter>(Converter);

        // The routing stack is registered so the executors resolve, but the Redis-backed ledger is
        // replaced with one that refuses every reservation — no container, and a hard guarantee
        // that the deterministic path reaches no provider. The last registration wins.
        services.AddSingleton<Routing.Budgets.IBudgetLedger, NoProviderBudgetLedger>();

        _services = services.BuildServiceProvider();
    }

    /// <summary>
    /// Creates the run row and its upload, exactly as <c>SubmitRun</c> would.
    ///
    /// <para>The default is a Markdown passthrough, where the source key and the markdown key are
    /// one object. <see cref="SubmitConvertibleAsync"/> is the other case.</para>
    /// </summary>
    public async Task<string> SubmitAsync(string markdown, string documentId = "test.md")
    {
        const string owner = "test-subject";
        var runId = Core.Model.Ids.NewRunId();
        var uploadId = Core.Model.Ids.NewRunId();

        await Artifacts.WriteTextAsync(ArtifactKeys.Upload(owner, uploadId), markdown, "upload");

        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SegmentationDbContext>();

        db.Uploads.Add(new Upload
        {
            UploadId = uploadId,
            OwnerSubject = owner,
            FileName = documentId,
            SizeBytes = markdown.Length,
            MediaType = "text/markdown",
            SourceExtension = ".md",
            RequiresConversion = false,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
        });

        db.Runs.Add(new Run
        {
            RunId = runId,
            OwnerSubject = owner,
            DocumentId = documentId,
            UploadId = uploadId,
            IdempotencyKey = runId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        foreach (var phase in Enum.GetValues<Core.Model.PipelinePhase>())
        {
            db.RunPhases.Add(new RunPhase { RunId = runId, Phase = phase });
        }

        await db.SaveChangesAsync();
        return runId;
    }

    /// <summary>
    /// A run over a source that needs converting — the shape <c>CreateUpload</c> records for a
    /// PDF. Only the source object exists; the Markdown is the converter's to write, which is
    /// exactly the sequencing phase 0 has to get right.
    /// </summary>
    public async Task<string> SubmitConvertibleAsync(
        string markdownTheConverterWillProduce,
        string extension = ".pdf",
        string mediaType = "application/pdf",
        string documentId = "test.pdf")
    {
        const string owner = "test-subject";
        var runId = Core.Model.Ids.NewRunId();
        var uploadId = Core.Model.Ids.NewRunId();

        await Artifacts.WriteTextAsync(
            ArtifactKeys.UploadSource(owner, uploadId, extension), "%PDF-1.7 not really", "upload");

        Converter.Produce(
            ArtifactKeys.Upload(owner, uploadId), markdownTheConverterWillProduce, Artifacts);

        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SegmentationDbContext>();

        db.Uploads.Add(new Upload
        {
            UploadId = uploadId,
            OwnerSubject = owner,
            FileName = documentId,
            SizeBytes = 1024,
            MediaType = mediaType,
            SourceExtension = extension,
            RequiresConversion = true,
            WithLayout = true,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
        });

        db.Runs.Add(new Run
        {
            RunId = runId,
            OwnerSubject = owner,
            DocumentId = documentId,
            UploadId = uploadId,
            IdempotencyKey = runId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        foreach (var phase in Enum.GetValues<Core.Model.PipelinePhase>())
        {
            db.RunPhases.Add(new RunPhase { RunId = runId, Phase = phase });
        }

        await db.SaveChangesAsync();
        return runId;
    }

    public async Task<Run?> LoadRunAsync(string runId)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SegmentationDbContext>();
        return await db.Runs.AsNoTracking().Include(r => r.Phases).FirstOrDefaultAsync(r => r.RunId == runId);
    }

    public async Task<RunResult?> LoadResultAsync(string runId)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SegmentationDbContext>();
        return await db.RunResults.AsNoTracking().FirstOrDefaultAsync(r => r.RunId == runId);
    }

    /// <summary>
    /// Retires the human gate for a family by recording the approvals the policy counts
    /// (plan §9.10: a family gates until K of its documents have been approved).
    /// </summary>
    public async Task ApproveFamilyAsync(string family, int count = 5)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SegmentationDbContext>();

        for (var i = 0; i < count; i++)
        {
            db.ReviewTasks.Add(new ReviewTask
            {
                ReviewId = Core.Model.Ids.NewRunId(),
                RunId = $"historic-{i}",
                OwnerSubject = "test-subject",
                DocumentId = $"historic-{i}.md",
                DocumentFamily = family,
                Status = "complete",
                Decision = Core.Model.ReviewDecisionKind.Approve,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-i - 1),
                DecidedAt = DateTimeOffset.UtcNow.AddDays(-i),
            });
        }

        await db.SaveChangesAsync();
    }

    /// <summary>The review this run is waiting on, or null.</summary>
    public async Task<ReviewTask?> PendingReviewAsync(string runId)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SegmentationDbContext>();
        return await db.ReviewTasks.AsNoTracking()
            .FirstOrDefaultAsync(r => r.RunId == runId && r.Status == "pending");
    }

    /// <summary>Records a decision, as <c>SubmitReviewDecision</c> does.</summary>
    public async Task DecideAsync(string reviewId, Core.Model.ReviewDecisionKind decision, string notes = "")
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SegmentationDbContext>();

        var task = await db.ReviewTasks.FirstAsync(r => r.ReviewId == reviewId);
        task.Status = "complete";
        task.Decision = decision;
        task.Notes = notes;
        task.DecidedBy = "reviewer-subject";
        task.DecidedAt = DateTimeOffset.UtcNow;

        var run = await db.Runs.FirstAsync(r => r.RunId == task.RunId);
        run.ReviewState = decision == Core.Model.ReviewDecisionKind.Reject ? "rejected" : "approved";

        await db.SaveChangesAsync();
    }

    /// <summary>Marks the run cancelled, as <c>CancelRun</c> does.</summary>
    public async Task CancelAsync(string runId)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SegmentationDbContext>();

        var run = await db.Runs.FirstAsync(r => r.RunId == runId);
        run.Cancelled = true;
        await db.SaveChangesAsync();
    }

    /// <summary>Removes the uploaded source, simulating the seven-day upload expiry.</summary>
    public async Task ForgetUploadAsync(string runId)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SegmentationDbContext>();

        var run = await db.Runs.AsNoTracking().FirstAsync(r => r.RunId == runId);
        await Artifacts.DeleteAsync(ArtifactKeys.Upload(run.OwnerSubject, run.UploadId));
    }

    public ValueTask DisposeAsync() => _services.DisposeAsync();
}
