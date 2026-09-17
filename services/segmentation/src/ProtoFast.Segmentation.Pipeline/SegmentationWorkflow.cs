using Microsoft.Agents.AI.Workflows;
using ProtoFast.Segmentation.Pipeline.Executors;

namespace ProtoFast.Segmentation.Pipeline;

/// <summary>
/// The workflow graph of plan §13.1.
///
/// <para>Executors are bound as shared instances, which is safe because none of them holds run
/// state: everything a phase needs comes from artifacts in S3 and from the run row in Postgres,
/// addressed by the run id in the message. That is also what lets any worker resume any run.</para>
///
/// <para>The graph is linear with two conditional branches — the human gate, and the labelling
/// short-circuit. Fan-out is inside the label and augment executors rather than across edges,
/// because the number of windows and paragraphs is not known until the document has been read
/// (see <see cref="LabelExecutor"/>).</para>
/// </summary>
public sealed class SegmentationWorkflowFactory(
    IngestExecutor ingest,
    CleanExecutor clean,
    TriageExecutor triage,
    LabelExecutor label,
    AssembleExecutor assemble,
    StructureExecutor structure,
    ValidateExecutor validate,
    StructureReviewExecutor structureReview,
    HumanGateExecutor humanGate,
    GateResumeExecutor gateResume,
    FreezeExecutor freeze,
    AugmentExecutor augment)
{
    /// <summary>The request port the human gate suspends on (plan §9.10).</summary>
    public static readonly RequestPort<ReviewRequest, ReviewDecision> HumanGatePort =
        RequestPort.Create<ReviewRequest, ReviewDecision>(ExecutorIds.HumanGatePort);

    public Workflow Build()
    {
        var builder = new WorkflowBuilder(ingest)
            .WithName("segmentation")
            .WithDescription("Hierarchical document segmentation (docs/theplot-segmentation-plan.md)");

        builder
            .AddEdge(ingest, clean)
            .AddEdge(clean, triage)
            // Both branches of the labelling short-circuit land in the same executor: with no
            // suspect regions it merges trusted boundaries into labels without calling anything,
            // which keeps one code path for "what is this line?" (plan §9.4).
            .AddEdge(triage, label)
            .AddEdge(label, assemble)
            .AddEdge(assemble, structure)
            .AddEdge(structure, validate)
            .AddEdge(validate, structureReview)
            // The gate is conditional. A run that needs no human goes straight to the freeze.
            .AddEdge<ReviewComplete>(structureReview, freeze, condition: r => r?.RequiresHuman == false)
            // One that does goes through the gate executor — which writes the review_tasks row so
            // ThePlot can list it without touching the workflow store — and then suspends on the
            // request port until SubmitReviewDecision posts a decision back.
            .AddEdge<ReviewComplete>(structureReview, humanGate, condition: r => r?.RequiresHuman == true)
            .AddEdge(humanGate, HumanGatePort)
            .AddEdge(HumanGatePort, gateResume)
            // The two ways out of the gate rejoin the graph at different places, so each gets its
            // own typed edge: an approval goes forward to the freeze, a rejection goes back to
            // structure inference with the reviewer's notes (plan §9.10).
            .AddEdge<ReviewComplete>(gateResume, freeze, condition: _ => true)
            .AddEdge<AssembleComplete>(gateResume, structure, condition: _ => true)
            .AddEdge(freeze, augment)
            .WithOutputFrom(augment);

        return builder.Build();
    }
}
