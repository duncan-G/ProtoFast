using Microsoft.Agents.AI.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Tree;
using ProtoFast.Segmentation.Data;
using ProtoFast.Segmentation.Data.Entities;
using ProtoFast.Segmentation.Pipeline.Agents;
using ProtoFast.Segmentation.Storage;

namespace ProtoFast.Segmentation.Pipeline.Executors;

/// <summary>
/// Phase 8 (plan §9.10): the human gate.
///
/// <para>Two things happen here, and both are necessary. The workflow emits an external request
/// through MAF's request port and checkpoints — that is what suspends the run without holding a
/// worker. And a <c>review_tasks</c> row is written — that is what lets ThePlot list outstanding
/// reviews without touching the workflow store, so the review screen works even when no worker is
/// running.</para>
/// </summary>
public sealed class HumanGateExecutor(
    RunArtifacts artifacts,
    RunJournal journal,
    IServiceScopeFactory scopes,
    ILogger<HumanGateExecutor> logger)
    : Executor<ReviewComplete, ReviewRequest>(ExecutorIds.HumanGate, declareCrossRunShareable: true)
{
    public override async ValueTask<ReviewRequest> HandleAsync(
        ReviewComplete message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var key = IdempotencyKeys.Phase(message.RunId, PipelinePhase.HumanGate);
        await journal.StartAsync(message.RunId, PipelinePhase.HumanGate, key, cancellationToken);

        var run = await journal.LoadRunAsync(message.RunId, cancellationToken)
            ?? throw new PipelineFailureException(PipelinePhase.HumanGate, "The run row is missing.");
        var review = await artifacts.ReadReviewAsync(message.RunId, cancellationToken);

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SegmentationDbContext>();

        // One open review per run. A resumed workflow re-enters this executor, and a second row
        // would show the same document twice in a reviewer's queue.
        var existing = await db.ReviewTasks
            .FirstOrDefaultAsync(r => r.RunId == message.RunId && r.Status == "pending", cancellationToken);

        var reviewId = existing?.ReviewId ?? Ids.NewRunId();

        if (existing is null)
        {
            db.ReviewTasks.Add(new ReviewTask
            {
                ReviewId = reviewId,
                RunId = run.RunId,
                OwnerSubject = run.OwnerSubject,
                DocumentId = run.DocumentId,
                DocumentFamily = run.DocumentFamily,
                WorkflowRequestId = null,
                FindingsJson = System.Text.Json.JsonSerializer.Serialize(review?.Findings ?? []),
                Status = "pending",
                CreatedAt = DateTimeOffset.UtcNow,
            });

            var gatedRun = await db.Runs.FirstOrDefaultAsync(r => r.RunId == message.RunId, cancellationToken);
            if (gatedRun is not null)
            {
                gatedRun.ReviewState = "pending";
                gatedRun.UpdatedAt = DateTimeOffset.UtcNow;
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation("Run {RunId}: waiting for human review {ReviewId}", message.RunId, reviewId);

        await journal.NoteAsync(
            message.RunId, PipelinePhase.HumanGate, "waiting for a reviewer", cancellationToken);

        return new ReviewRequest(
            message.RunId,
            reviewId,
            ArtifactKeys.Phase(message.RunId, PipelinePhase.InferStructure),
            ArtifactKeys.Phase(message.RunId, PipelinePhase.ReviewStructure));
    }
}

/// <summary>
/// Phase 9 (plan §9.11): freeze.
///
/// <para>Every check is re-run first — not because they passed a moment ago, but because a human
/// gate may have applied edits since, and the freeze is the last point at which anything can be
/// caught. Then the canonical JSON is hashed and written with an object-lock retention, which is
/// what turns "frozen" from a convention into a property of the storage.</para>
/// </summary>
public sealed class FreezeExecutor(
    RunArtifacts artifacts,
    RunJournal journal,
    PhaseGate gate,
    IServiceScopeFactory scopes,
    Microsoft.Extensions.Options.IOptions<Core.Options.PipelineOptions> options,
    ILogger<FreezeExecutor> logger)
    : Executor<ReviewComplete, FrozenComplete>(ExecutorIds.Freeze, declareCrossRunShareable: true)
{
    public override async ValueTask<FrozenComplete> HandleAsync(
        ReviewComplete message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var key = IdempotencyKeys.Phase(message.RunId, PipelinePhase.Freeze);

        if (await gate.AlreadyDoneAsync(message.RunId, PipelinePhase.Freeze, key, null, cancellationToken) is { } done)
        {
            var frozenAlready = await artifacts.ReadFrozenAsync(message.RunId, cancellationToken);
            return new FrozenComplete(message.RunId, done, frozenAlready?.TreeHash ?? string.Empty);
        }

        await journal.StartAsync(message.RunId, PipelinePhase.Freeze, key, cancellationToken);

        var cleaning = await artifacts.ReadCleaningAsync(message.RunId, cancellationToken)
            ?? throw new PipelineFailureException(PipelinePhase.Freeze, "The cleaning artifact is missing.");
        var labels = await artifacts.ReadLabelsAsync(message.RunId, cancellationToken);
        var assembly = await artifacts.ReadAssemblyAsync(message.RunId, cancellationToken)
            ?? throw new PipelineFailureException(PipelinePhase.Freeze, "The assembly artifact is missing.");
        var tree = await artifacts.ReadTreeAsync(message.RunId, cancellationToken)
            ?? throw new PipelineFailureException(PipelinePhase.Freeze, "The tree artifact is missing.");
        var run = await journal.LoadRunAsync(message.RunId, cancellationToken)
            ?? throw new PipelineFailureException(PipelinePhase.Freeze, "The run row is missing.");

        var report = Core.Validation.Checks.CheckAll(
            cleaning, labels, assembly.Paragraphs, assembly.Headings, tree.Root,
            options.Value.Paragraphs, assembly.SizeOutlierParagraphIds.ToHashSet(StringComparer.Ordinal));

        if (!report.Passed)
        {
            await journal.FailAsync(
                message.RunId, PipelinePhase.Freeze,
                "Freeze blocked by validation: " + report.ErrorReport, cancellationToken);

            throw new PipelineFailureException(PipelinePhase.Freeze, report.ErrorReport);
        }

        var treeHash = TreeCanonicalizer.Hash(tree.Root);
        var frozen = new FrozenDocument(
            RunId: run.RunId,
            DocumentId: run.DocumentId,
            DocumentTitle: tree.Root.Title,
            Root: tree.Root,
            Paragraphs: assembly.Paragraphs,
            Headings: assembly.Headings,
            TreeHash: treeHash,
            ParagraphsHash: TreeCanonicalizer.HashParagraphs(assembly.Paragraphs),
            FrozenAt: DateTimeOffset.UtcNow);

        var artifact = await artifacts.WriteFrozenAsync(message.RunId, frozen, key, cancellationToken);

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SegmentationDbContext>();

        var runRow = await db.Runs.FirstOrDefaultAsync(r => r.RunId == message.RunId, cancellationToken);
        if (runRow is not null)
        {
            runRow.TreeHash = treeHash;
            runRow.FrozenAt = frozen.FrozenAt;
            runRow.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation(
            "Run {RunId}: frozen at tree hash {TreeHash} ({Paragraphs} paragraphs)",
            message.RunId, treeHash, assembly.Paragraphs.Count);

        await journal.CompleteAsync(
            message.RunId, PipelinePhase.Freeze, artifact.Key,
            $"frozen: {assembly.Paragraphs.Count} paragraphs, tree {treeHash[..12]}", cancellationToken);

        return new FrozenComplete(message.RunId, artifact, treeHash);
    }
}
