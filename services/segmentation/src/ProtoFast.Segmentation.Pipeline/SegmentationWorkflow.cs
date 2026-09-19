using Microsoft.Agents.AI.Workflows;
using ProtoFast.Segmentation.Pipeline.Executors;
using ProtoFast.Segmentation.Routing;

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
/// (see <see cref="LabelExecutor"/>). The five scene phases follow the same rule: phases 8, 9 and
/// 11 fan out inside their executors, over windows the document's own length decides.</para>
/// </summary>
public sealed class SegmentationWorkflowFactory(
    IngestExecutor ingest,
    CleanExecutor clean,
    TriageExecutor triage,
    LabelExecutor label,
    AssembleExecutor assemble,
    PresentationExecutor presentation,
    StructureExecutor structure,
    ValidateExecutor validate,
    ItemExecutor items,
    PersonaExecutor referents,
    SceneCutExecutor scenes,
    SceneLinkExecutor sceneLinks,
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
            .WithDescription("Hierarchical document segmentation (docs/theplot-segmentation-plan.md)")
            // Spans for the build, the run, each superstep and each executor, on the
            // Microsoft.Agents.AI.Workflows source. Unlike the chat-client instrumentation this one
            // has no environment default of its own, so the same variable is applied by hand — see
            // GenAiTelemetry. With it off the spans still describe the graph; what they omit is the
            // message payloads moving along the edges.
            .WithOpenTelemetry(o => o.EnableSensitiveData = GenAiTelemetry.CaptureMessageContent);

        builder
            .AddEdge(ingest, clean)
            .AddEdge(clean, triage)
            // Both branches of the labelling short-circuit land in the same executor: with no
            // suspect regions it merges trusted boundaries into labels without calling anything,
            // which keeps one code path for "what is this line?" (plan §9.4).
            .AddEdge(triage, label)
            .AddEdge(label, assemble)
            // Presentation before structure, deliberately: front matter and a table of contents
            // inferred AS SECTIONS is noise in the tree, and withholding them first makes the tree
            // both smaller and better (scene plan §8.5).
            .AddEdge(assemble, presentation)
            .AddEdge(presentation, structure)
            .AddEdge(structure, validate)
            // The five scene phases, in the one order their dependencies permit (scene plan §8.4):
            // items are typed before scenes are cut because mode is largely a function of the item
            // mix, and referents resolve between the two because cast is a scene coordinate while
            // personas are discovered from speech attribution.
            .AddEdge(validate, items)
            .AddEdge(items, referents)
            .AddEdge(referents, scenes)
            .AddEdge(scenes, sceneLinks)
            .AddEdge(sceneLinks, structureReview)
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
            // structure inference with the reviewer's notes (plan §9.10). The rejection edge lands
            // on presentation's output rather than assembly's, because structure now consumes that.
            .AddEdge<ReviewComplete>(gateResume, freeze, condition: _ => true)
            .AddEdge<PresentationComplete>(gateResume, structure, condition: _ => true)
            .AddEdge(freeze, augment)
            .WithOutputFrom(augment);

        return builder.Build();
    }
}
