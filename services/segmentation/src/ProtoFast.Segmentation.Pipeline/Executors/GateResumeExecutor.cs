using Microsoft.Agents.AI.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Data;
using ProtoFast.Segmentation.Pipeline.Agents;
using ProtoFast.Segmentation.Storage;

namespace ProtoFast.Segmentation.Pipeline.Executors;

/// <summary>
/// Turns a human decision back into pipeline flow (plan §9.10).
///
/// <para>The two <c>SendsMessage</c> attributes are not decoration: MAF validates an executor's
/// outgoing types against its declared protocol and refuses anything undeclared, which is what
/// keeps a graph from developing edges nobody can see.</para>
///
/// <para>It emits two different message types rather than returning one, because the two outcomes
/// rejoin the graph at different places: an approval goes forward to the freeze, and a rejection
/// goes <em>back</em> to structure inference with the reviewer's notes as context. An executor
/// with a single return type could only express one of those, so this one sends explicitly and
/// the graph carries a typed edge for each.</para>
/// </summary>
[SendsMessage(typeof(ReviewComplete))]
[SendsMessage(typeof(PresentationComplete))]
public sealed class GateResumeExecutor(
    RunArtifacts artifacts,
    RunJournal journal,
    IServiceScopeFactory scopes,
    ILogger<GateResumeExecutor> logger)
    : Executor<ReviewDecision>(ExecutorIds.GateResume, declareCrossRunShareable: true)
{
    public override async ValueTask HandleAsync(
        ReviewDecision message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SegmentationDbContext>();

        var task = await db.ReviewTasks
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ReviewId == message.ReviewId, cancellationToken)
            ?? throw new PipelineFailureException(
                PipelinePhase.HumanGate, $"Review '{message.ReviewId}' is not in the database.");

        var runId = task.RunId;

        await artifacts.WriteDecisionAsync(
            runId, message, IdempotencyKeys.Phase(runId, PipelinePhase.HumanGate), cancellationToken);

        await journal.CompleteAsync(
            runId, PipelinePhase.HumanGate, null,
            $"{message.Kind.ToString().ToLowerInvariant()} by {task.DecidedBy ?? "a reviewer"}",
            cancellationToken);

        if (message.Kind == ReviewDecisionKind.Reject)
        {
            logger.LogInformation(
                "Run {RunId}: rejected at the gate; re-running structure inference with the notes.", runId);

            // Back to phase 6. The notes reach the structurer through the review artifact, which
            // the structure executor reads — so a rejection is a second attempt with information,
            // not a retry of the same prompt. Presentation is not re-run: what a reviewer rejected
            // is the tree, and re-classifying the front matter would spend a phase to reproduce it.
            await journal.NoteAsync(
                runId, PipelinePhase.InferStructure,
                $"re-running after rejection: {message.Notes ?? "(no notes)"}", cancellationToken);

            var presentation = await artifacts.ReadPresentationAsync(runId, cancellationToken);

            await context.SendMessageAsync(
                new PresentationComplete(
                    runId,
                    new ArtifactRef(
                        runId, ArtifactKeys.Phase(runId, PipelinePhase.ClassifyPresentation), string.Empty, 0),
                    presentation?.CompositionFamily
                        ?? Core.Classification.CompositionFamily.Unknown,
                    presentation?.Presentations.Count(p => !p.IsDisplayable) ?? 0),
                cancellationToken: cancellationToken);

            return;
        }

        var review = await artifacts.ReadReviewAsync(runId, cancellationToken);

        await context.SendMessageAsync(
            new ReviewComplete(
                runId,
                new ArtifactRef(runId, ArtifactKeys.Phase(runId, PipelinePhase.ReviewStructure), string.Empty, 0),
                // Approved: the gate has been satisfied, so the run goes to the freeze rather than
                // back through the gate it just came out of.
                RequiresHuman: false),
            cancellationToken: cancellationToken);

        _ = review;
    }
}
