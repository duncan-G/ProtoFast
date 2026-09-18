using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;
using ProtoFast.Segmentation.Core.Tree;
using ProtoFast.Segmentation.Core.Validation;
using ProtoFast.Segmentation.Core.Windowing;
using ProtoFast.Segmentation.Routing;

namespace ProtoFast.Segmentation.Pipeline.Agents;

/// <summary>
/// The window agent's wire format: a tree, the paragraph edits it noticed, and what it could not
/// settle from inside its own window.
/// </summary>
public sealed record StructureWindowReply
{
    [JsonPropertyName("tree")]
    public TreeProposalNode? Tree { get; init; }

    [JsonPropertyName("paragraphEdits")]
    public IReadOnlyList<ParagraphEditProposal> ParagraphEdits { get; init; } = [];

    [JsonPropertyName("openQuestions")]
    public IReadOnlyList<string> OpenQuestions { get; init; } = [];

    public TreeProposal ToProposal() => new() { Tree = Tree, ParagraphEdits = ParagraphEdits };
}

/// <summary>The follow-up round's wire format.</summary>
public sealed record StructureAnswersReply
{
    [JsonPropertyName("answers")]
    public IReadOnlyList<FollowUpAnswer> Answers { get; init; } = [];
}

/// <summary>What one window agent produced.</summary>
public sealed record WindowStructureResult(
    int WindowIndex,
    SectionNode Root,
    IReadOnlyList<ParagraphEdit> Edits,
    IReadOnlyList<string> OpenQuestions,
    string? ModelKey);

/// <summary>
/// Structures one window of the skeleton (orchestrator plan §4.1).
///
/// <para>A separate role from <see cref="StructurerAgent"/> rather than the same one at a smaller
/// tier, because it is a different job: a window agent is told it is looking at a slice, is scored
/// only on the entries it commits, and is asked what it could not answer. "Qualified to structure
/// a document" says nothing about any of that, and a shared role would have let one qualification
/// row stand for both.</para>
///
/// <para>Stateless like every other agent: each call carries its own window and nothing else. The
/// follow-up round re-sends the same window with the orchestrator's questions rather than
/// continuing a session, which is what keeps a resumed run free of chat state.</para>
/// </summary>
public sealed class StructureWindowerAgent(
    AgentRunner runner,
    IOptions<PipelineOptions> options,
    ILogger<StructureWindowerAgent> logger)
{
    private readonly RepairOptions _repair = options.Value.Repair;

    public async Task<WindowStructureResult> StructureAsync(
        StructureWindow window,
        StructureContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(context);

        var expectedParagraphIds = window.CommittedParagraphIds;
        var headingLineIds = window.CommittedHeadingLineIds;
        var rendered = StructureWindowPlanner.Render(window);

        var prompt = new PromptTemplate(runner.Assets.Template("structure-window.v1"))
            .Set("rules", runner.Assets.Rules)
            .Set("skill", runner.Assets.Skill("hierarchy-inference"))
            .Set("schema", runner.Assets.Schema("structure-window"))
            .Set("familySkill", runner.Assets.FamilySkill(context.Family))
            .Set("instincts", RenderInstincts(context.Instincts))
            .Set("window", rendered)
            .Render();

        var result = await runner.RunAsync<StructureWindowReply>(
            prompt,
            Routing(context, window, rendered.Length),
            reply => Validate(reply, expectedParagraphIds, headingLineIds),
            _repair.MaxRoundsPerArtifact,
            buildRepairPrompt: (previous, errors) => BuildRepairPrompt(previous, errors, expectedParagraphIds),
            ct);

        if (!result.Success)
        {
            throw new PipelineFailureException(
                PipelinePhase.InferStructure,
                $"Window {window.WindowIndex} could not be structured: {result.Validation.ErrorReport}");
        }

        var reply = result.Value!;

        return new WindowStructureResult(
            window.WindowIndex,
            TreeMaterializer.Materialize(reply.Tree!, $"Window {window.WindowIndex}"),
            TreeMaterializer.ToEdits(reply.ToProposal()),
            // Capped, because the digest that carries these to the orchestrator is the thing the
            // round budget is meant to bound. An agent with twenty questions has misunderstood the
            // job, and sending all twenty would not fix that.
            [.. reply.OpenQuestions.Where(q => !string.IsNullOrWhiteSpace(q)).Take(MaxOpenQuestions)],
            result.Response?.Decision.Model.Key);
    }

    /// <summary>
    /// Re-asks one window the orchestrator's questions (orchestrator plan §4.2). The questions come
    /// from a closed vocabulary and are rendered into an embedded template, so nothing the
    /// orchestrator emits reaches a model as instructions — which is what keeps
    /// <c>PromptVersion</c> a property of the image rather than of the conversation (§12.1).
    /// </summary>
    public async Task<IReadOnlyList<FollowUpAnswer>> AnswerAsync(
        StructureWindow window,
        WindowOutline outline,
        IReadOnlyList<FollowUp> questions,
        StructureContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(outline);
        ArgumentNullException.ThrowIfNull(questions);

        if (questions.Count == 0)
        {
            return [];
        }

        var rendered = StructureWindowPlanner.Render(window);

        var prompt = new PromptTemplate(runner.Assets.Template("structure-followup.v1"))
            .Set("rules", runner.Assets.Rules)
            .Set("schema", runner.Assets.Schema("structure-answers"))
            .Set("window", rendered)
            .Set("questions", RenderQuestions(questions))
            .Set("outline", RenderOutline(outline))
            .Render();

        var result = await runner.RunAsync<StructureAnswersReply>(
            prompt,
            Routing(context, window, rendered.Length) with
            {
                Unit = $"follow-up:{window.WindowIndex}",
                MaxOutputTokens = 2_048,
                OutputSchema = runner.Assets.WireSchemaElement("structure-answers"),
                OutputSchemaName = "structure-answers",
            },
            reply => ValidateAnswers(reply, questions),
            _repair.MaxRoundsPerArtifact,
            ct: ct);

        if (result.Success)
        {
            return result.Value!.Answers;
        }

        // An unanswered question is not a failed run. The orchestrator asked because an answer
        // would help, not because it could not proceed without one — and failing the document over
        // a clarifying question would make the follow-up round strictly worse than not having it.
        logger.LogWarning(
            "Run {RunId}: window {Window} did not answer its follow-ups ({Error}); the orchestrator "
            + "continues without them.",
            context.RunId, window.WindowIndex, result.Validation.CheckId);

        return [];
    }

    /// <summary>Enough for the orchestrator to see what a window is unsure about; few enough to read.</summary>
    public const int MaxOpenQuestions = 5;

    private static ValidationResult Validate(
        StructureWindowReply reply,
        IReadOnlyList<string> expectedParagraphIds,
        IReadOnlySet<string> headingLineIds)
    {
        if (reply.Tree is null)
        {
            return ValidationResult.Fail(Checks.Schema, "The reply had no 'tree' property.");
        }

        var root = TreeMaterializer.Materialize(reply.Tree);

        // Scoped to the window's committed entries. A window that placed a context entry has taken
        // work that belongs to the window before it, and the assembly step would then see the same
        // paragraph twice — so it is caught here, where the repair round is cheap.
        var coverage = Checks.CheckIdCoverage(
            expectedParagraphIds, [.. root.Descend().SelectMany(n => n.ParagraphIds)]);
        if (!coverage.Passed)
        {
            return coverage;
        }

        var shape = Checks.CheckTreeShape(root);
        return shape.Passed ? Checks.CheckHeadingAnchor(root, headingLineIds) : shape;
    }

    private static ValidationResult ValidateAnswers(
        StructureAnswersReply reply,
        IReadOnlyList<FollowUp> questions)
    {
        var asked = questions
            .Select(q => (q.Node, q.Kind))
            .ToHashSet();

        var errors = reply.Answers
            .Where(a => !asked.Contains((a.Node, a.Kind)))
            .Select(a => $"'{a.Kind}' was not asked about '{a.Node}'")
            .ToList();

        return ValidationResult.Fail(Checks.Schema, errors);
    }

    private static string RenderQuestions(IReadOnlyList<FollowUp> questions)
    {
        var builder = new StringBuilder();

        for (var i = 0; i < questions.Count; i++)
        {
            var question = questions[i];

            // The sentences live here, not in the orchestrator's reply. That is the difference
            // between selecting a behaviour the image already contains and authoring one at
            // runtime (orchestrator plan §12.1), and it is what makes the follow-up round
            // auditable: "which question was asked" has four possible answers.
            var text = question.Kind switch
            {
                FollowUpKinds.ContinuesPrevious =>
                    $"Does the section you returned as {question.Node} continue a section that began "
                    + "before this window? Answer yes if its first paragraph carries on from the context "
                    + "above. Put the id of the context line or paragraph it continues from in 'evidence'.",
                FollowUpKinds.TitleSource =>
                    $"Is there a heading line in this window that is the real title of {question.Node}? "
                    + "Answer yes and put that line's id in 'evidence'; answer no if the title you gave "
                    + "it was your own.",
                FollowUpKinds.BoundaryCheck =>
                    $"Does {question.Node} end where your window ends because the section genuinely "
                    + "ends there? Answer no if it looks like it runs past the end of the window. Put "
                    + "the id of its last paragraph in 'evidence'.",
                FollowUpKinds.DepthCheck =>
                    $"Is {question.Node} a peer of {question.RelatedNode}? Answer yes for a peer, no if "
                    + $"{question.Node} belongs underneath it. Leave 'evidence' empty.",
                _ => null,
            };

            if (text is null)
            {
                continue;
            }

            builder.Append(i + 1).Append(". [").Append(question.Kind).Append("] ").AppendLine(text);
        }

        return builder.ToString();
    }

    private static string RenderOutline(WindowOutline outline)
    {
        var builder = new StringBuilder();
        foreach (var node in outline.Nodes)
        {
            builder.Append(new string(' ', (node.Depth - 1) * 2))
                .Append("- ")
                .Append(node.Ref)
                .Append("  \"")
                .Append(node.Title)
                .Append('"')
                .AppendLine(node.TitleInferred ? "  (title you invented)" : $"  (heading {node.HeadingLineId})");
        }

        return builder.ToString();
    }

    private string BuildRepairPrompt(string previousReply, string errorReport, IReadOnlyList<string> scope) =>
        new PromptTemplate(runner.Assets.Template("tree-repair.v1"))
            .Set("rules", runner.Assets.Rules)
            .Set("skill", runner.Assets.Skill("tree-repair"))
            .Set("schema", runner.Assets.Schema("structure-window"))
            .Set("errorReport", errorReport)
            .Set("artifact", ModelJson.Extract(previousReply))
            .Set("scope", string.Join(", ", scope))
            .Render();

    private RoutingContext Routing(StructureContext context, StructureWindow window, int renderedChars) =>
        new(
            context.RunId, context.DocumentId, PipelinePhase.InferStructure, AgentRole.StructureWindower,
            // Mid, not Large. The whole hypothesis of the orchestrated strategy is that a slice is
            // a smaller job than a document and that a cheaper model can do it, with the expensive
            // one spent once on the assembly instead of once per part (orchestrator plan §7).
            ModelTier.Mid, context.Sensitivity,
            EstimatedInputTokens: 2_000 + renderedChars / 4,
            MaxOutputTokens: Math.Max(4_096, window.CommitEntries.Count * 32))
        {
            PinnedModelKey = context.PinnedWindowerKey,
            PromptVersion = runner.Assets.VersionFor(AgentRole.StructureWindower),
            Unit = $"structure-window:{window.WindowIndex}",
            OutputSchema = runner.Assets.WireSchemaElement("structure-window"),
            OutputSchemaName = "structure-window",
        };

    private static string RenderInstincts(IReadOnlyList<string> instincts) =>
        instincts.Count == 0
            ? string.Empty
            : "## Learned guidance for this family\n" + string.Join('\n', instincts.Select(i => "- " + i));
}
