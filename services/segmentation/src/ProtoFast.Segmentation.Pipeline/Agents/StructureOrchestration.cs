using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProtoFast.Segmentation.Core.Assembly;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;
using ProtoFast.Segmentation.Core.Tree;
using ProtoFast.Segmentation.Core.Validation;
using ProtoFast.Segmentation.Core.Windowing;
using ProtoFast.Segmentation.Pipeline.Executors;

namespace ProtoFast.Segmentation.Pipeline.Agents;

/// <summary>
/// What the orchestration produced, or why it could not. <see cref="Failure"/> is not an
/// exception because the caller's answer to it is to fall back to the chunked splice — which is
/// what phase 5 does today, so the fallback is never worse than the status quo (orchestrator plan
/// §11).
/// </summary>
public sealed record OrchestrationResult(
    SectionNode? Root,
    IReadOnlyList<ParagraphEdit> Edits,
    AssemblyPlan? Plan,
    IReadOnlyList<TranscriptEntry> Transcript,
    IReadOnlyList<CapabilityGapProposal> Gaps,
    string? WindowerModelKey,
    string? OrchestratorModelKey,
    string? Failure)
{
    public bool Success => Root is not null && Failure is null;
}

/// <summary>
/// The orchestration loop of orchestrator plan §4.2, written directly rather than built on
/// <c>AgentWorkflowBuilder</c>.
///
/// <para>The reason is in §3: a MAF group chat selects <em>one</em> speaker per iteration, so the
/// parallelism would have had to be smuggled inside a participant anyway — and once it is, none of
/// what the framework provides is left. Turn order here is the loop; termination is
/// <c>plan.IsComplete</c> plus the round cap; history shaping is the fact that only digests and
/// the orchestrator's own replies are ever added. That last one is stricter than
/// <c>UpdateHistoryAsync</c> trimming, and it is what keeps the window agents stateless: they are
/// called directly with the prompt they need and never see the transcript.</para>
///
/// <para>Nothing in it does I/O of its own. Every call inside goes through <see cref="AgentRunner"/>
/// and <c>IRoutingChatClient</c>, which is where retry, truncation handling, the sensitivity filter
/// and the budget ledger already live.</para>
/// </summary>
public sealed class StructureOrchestration(
    WindowBench bench,
    StructureOrchestratorAgent orchestrator,
    IOptions<PipelineOptions> options,
    ILogger<StructureOrchestration> logger)
{
    private readonly StructureOptions _structure = options.Value.Structure;

    public async Task<OrchestrationResult> RunAsync(
        IReadOnlyList<SkeletonBuilder.SkeletonEntry> entries,
        IReadOnlyList<HeadingRecord> headings,
        StructureContext context,
        Func<string, CancellationToken, Task>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(context);

        var windows = StructureWindowPlanner.Plan(entries, headings, _structure.WindowOverlapEntries);
        var expectedParagraphIds = entries.Where(e => !e.IsHeading).Select(e => e.Id).ToList();

        logger.LogInformation(
            "Run {RunId}: structuring {Entries} skeleton entries in {Windows} windows.",
            context.RunId, entries.Count, windows.Count);

        // Round 0: the bench works with no orchestrator at all. There is nothing for it to reason
        // about until the windows have reported, and making it wait is what keeps the expensive
        // model out of the part of the phase that scales with document length.
        var report = await bench.StructureAllAsync(windows, context, progress, ct);

        if (windows.Count == 1)
        {
            // A bench of one has nothing to assemble (orchestrator plan §8). Paying a Large call to
            // be told so would make the orchestrated strategy strictly more expensive than the
            // chunked one on the documents where they agree.
            var only = report.Subtrees[0];
            var single = Renumber(
                new SectionNode(string.Empty, context.DocumentTitle, true, null, 1,
                    only.Children.Count > 0 ? only.Children : [only], []));

            return new OrchestrationResult(
                single, report.Edits, null, [], [], report.ModelKey, null, null);
        }

        var transcript = new List<TranscriptEntry>();
        var gaps = new List<CapabilityGapProposal>();
        var conversation = new List<ChatMessage> { orchestrator.OpeningMessage(report.Digest, context) };
        var rounds = Math.Max(1, _structure.MaxOrchestratorRounds);

        transcript.Add(new TranscriptEntry(0, "bench", StructureOrchestratorAgent.RenderDigest(report.Digest)));

        for (var round = 0; round < rounds; round++)
        {
            var isLastRound = round == rounds - 1;

            var turn = await orchestrator.AdvanceAsync(
                conversation,
                context,
                plan => Validate(plan, report, expectedParagraphIds, context, allowFollowUps: !isLastRound),
                ct);

            transcript.Add(new TranscriptEntry(round + 1, "orchestrator", turn.Text));

            if (!turn.Success)
            {
                return Failed(
                    transcript, gaps, report.ModelKey, turn.ModelKey,
                    $"the orchestrator's plan could not be applied after {round + 1} rounds: "
                    + turn.Validation.ErrorReport);
            }

            var plan = turn.Plan!;
            gaps.AddRange(plan.Gaps);

            if (plan.IsComplete)
            {
                // Applied a second time rather than threaded out of the validator, which keeps the
                // validator a pure predicate and this the only place a tree is produced.
                var applied = OrchestratedTreeMaterializer.Apply(
                    plan, report.Subtrees, expectedParagraphIds, context.DocumentTitle);

                return applied.Success
                    ? new OrchestrationResult(
                        applied.Root, report.Edits, plan, transcript, gaps,
                        report.ModelKey, turn.ModelKey, null)
                    : Failed(
                        transcript, gaps, report.ModelKey, turn.ModelKey,
                        "the validated plan did not materialize: " + applied.Validation.ErrorReport);
            }

            if (progress is not null)
            {
                await progress($"orchestrator round {round + 1}: {plan.FollowUps.Count} follow-ups", ct);
            }

            var answers = await bench.AnswerAsync(plan.FollowUps, windows, report.Digest, context, ct);

            transcript.Add(new TranscriptEntry(round + 1, "bench", StructureOrchestratorAgent.AnswersMessage(answers).Text));

            conversation =
            [
                .. conversation,
                new ChatMessage(ChatRole.Assistant, turn.Text),
                StructureOrchestratorAgent.AnswersMessage(answers),
            ];
        }

        return Failed(
            transcript, gaps, report.ModelKey, null,
            $"the orchestrator asked questions for all {rounds} rounds without assembling the document");
    }

    private static OrchestrationResult Failed(
        List<TranscriptEntry> transcript,
        List<CapabilityGapProposal> gaps,
        string? windowerKey,
        string? orchestratorKey,
        string failure) =>
        new(null, [], null, transcript, gaps, windowerKey, orchestratorKey, failure);

    /// <summary>
    /// The plan is valid when it materializes. A reply that asks instead of answering is valid
    /// too, until the last round — at which point the loop has no more questions to spend and a
    /// reply without an outline is the failure it is.
    /// </summary>
    private static ValidationResult Validate(
        AssemblyPlan plan,
        BenchResult report,
        IReadOnlyList<string> expectedParagraphIds,
        StructureContext context,
        bool allowFollowUps)
    {
        if (plan.IsComplete)
        {
            return OrchestratedTreeMaterializer
                .Apply(plan, report.Subtrees, expectedParagraphIds, context.DocumentTitle)
                .Validation;
        }

        if (!allowFollowUps)
        {
            return ValidationResult.Fail(
                OrchestratedTreeMaterializer.AssemblyCheck,
                "There are no rounds of questions left. Return an outline that places every section, "
                + "choosing the most likely reading where a report was ambiguous.");
        }

        var errors = new List<string>();

        if (plan.FollowUps.Count == 0)
        {
            errors.Add("the reply has neither an outline nor a follow-up question");
        }

        foreach (var followUp in plan.FollowUps)
        {
            if (!FollowUpKinds.IsKnown(followUp.Kind))
            {
                errors.Add($"'{followUp.Kind}' is not one of the four questions that can be asked");
            }

            if (!NodeRefs.TryParse(followUp.Node, out var window, out _) || !report.Subtrees.ContainsKey(window))
            {
                errors.Add($"'{followUp.Node}' is not a node any window returned");
            }
        }

        return ValidationResult.Fail(OrchestratedTreeMaterializer.AssemblyCheck, errors);
    }

    /// <summary>Section ids in document order, for the one-window path that skips the materializer.</summary>
    private static SectionNode Renumber(SectionNode root)
    {
        var counter = 0;
        return Walk(root, depth: 1, ref counter);

        static SectionNode Walk(SectionNode node, int depth, ref int counter)
        {
            var id = Ids.Section(counter++);
            var children = new List<SectionNode>(node.Children.Count);

            foreach (var child in node.Children)
            {
                children.Add(Walk(child, depth + 1, ref counter));
            }

            return node with { SectionId = id, Level = Math.Min(depth, 6), Children = children };
        }
    }
}
