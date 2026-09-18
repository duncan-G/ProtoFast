using System.Globalization;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;
using ProtoFast.Segmentation.Core.Tree;
using ProtoFast.Segmentation.Core.Validation;
using ProtoFast.Segmentation.Routing;

namespace ProtoFast.Segmentation.Pipeline.Agents;

/// <summary>One orchestrator turn: what it said, and what the loop should do with it.</summary>
public sealed record OrchestratorTurn(AssemblyPlan? Plan, string Text, string? ModelKey, ValidationResult Validation)
{
    public bool Success => Plan is not null;
}

/// <summary>
/// The only stateful participant (orchestrator plan §4.1). It sees the digests, may ask follow-ups
/// and emits the assembly plan.
///
/// <para>It composes references, never content (§4.3). That is not a stylistic choice: the
/// orchestrator is the single point of failure for the whole tree, and a plan over node references
/// can be checked exactly — every paragraph is placed once or the plan does not materialize —
/// whereas a tree it wrote itself could only be checked after the fact, which is what the phase
/// already does and what this is trying to improve on.</para>
///
/// <para>Constructed by the loop rather than injected into the executor: a conversation is run
/// state, and <c>StructureExecutor</c> is a cross-run shared instance whose safety rests on
/// holding none (§6).</para>
/// </summary>
public sealed class StructureOrchestratorAgent(
    AgentRunner runner,
    IOptions<PipelineOptions> options)
{
    private readonly RepairOptions _repair = options.Value.Repair;

    /// <summary>
    /// The conversation's opening turn: the rules, the skill, the schema and the bench's first
    /// digest.
    /// </summary>
    public ChatMessage OpeningMessage(BenchDigest digest, StructureContext context)
    {
        ArgumentNullException.ThrowIfNull(digest);
        ArgumentNullException.ThrowIfNull(context);

        return new ChatMessage(
            ChatRole.User,
            new PromptTemplate(runner.Assets.Template("structure-orchestrator.v1"))
                .Set("rules", runner.Assets.Rules)
                .Set("skill", runner.Assets.Skill("structure-orchestration"))
                .Set("schema", runner.Assets.Schema("assembly-plan"))
                .Set("familySkill", runner.Assets.FamilySkill(context.Family))
                .Set("documentTitle", context.DocumentTitle)
                .Set("digest", RenderDigest(digest))
                .Render());
    }

    /// <summary>The answers round, as a plain user turn — the same door the opening message used.</summary>
    public static ChatMessage AnswersMessage(IReadOnlyList<FollowUpAnswer> answers)
    {
        ArgumentNullException.ThrowIfNull(answers);

        if (answers.Count == 0)
        {
            return new ChatMessage(
                ChatRole.User,
                "No window could answer. Assemble the document from what the reports already say, "
                + "choosing the reading that places every section once.");
        }

        var builder = new StringBuilder("## Answers\n");
        foreach (var answer in answers)
        {
            builder.Append("- ").Append(answer.Node).Append(" [").Append(answer.Kind).Append("]: ")
                .Append(answer.Verdict);

            if (!string.IsNullOrWhiteSpace(answer.Evidence))
            {
                builder.Append(" (").Append(answer.Evidence).Append(')');
            }

            builder.AppendLine();
        }

        builder.AppendLine()
            .AppendLine("Return the outline now, or ask again only if an answer left something genuinely open.");

        return new ChatMessage(ChatRole.User, builder.ToString());
    }

    /// <summary>
    /// One turn. <paramref name="validate"/> is the materializer: a plan that would orphan or
    /// duplicate a paragraph fails here and its exact report becomes the repair prompt, which is
    /// the whole reason the plan is references rather than a tree.
    /// </summary>
    public async Task<OrchestratorTurn> AdvanceAsync(
        IReadOnlyList<ChatMessage> conversation,
        StructureContext context,
        Func<AssemblyPlan, ValidationResult> validate,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        var result = await runner.RunAsync(
            conversation,
            Routing(context, conversation),
            validate,
            _repair.MaxRoundsPerArtifact,
            ct);

        return new OrchestratorTurn(
            result.Success ? result.Value : null,
            result.Response?.Text ?? string.Empty,
            result.Response?.Decision.Model.Key,
            result.Validation);
    }

    /// <summary>
    /// Renders the bench's report. Titles, depth and span — never an excerpt and never a paragraph
    /// id, so the orchestrator has nothing to copy even if it wanted to.
    /// </summary>
    public static string RenderDigest(BenchDigest digest)
    {
        ArgumentNullException.ThrowIfNull(digest);

        var builder = new StringBuilder();

        foreach (var window in digest.Windows)
        {
            builder.Append(CultureInfo.InvariantCulture, $"## Window {window.WindowIndex}")
                .AppendLine(CultureInfo.InvariantCulture, $"  ({window.FirstEntryId} … {window.LastEntryId})");

            foreach (var node in window.Nodes)
            {
                builder.Append(new string(' ', (node.Depth - 1) * 2))
                    .Append("- ")
                    .Append(node.Ref)
                    .Append("  \"")
                    .Append(node.Title)
                    .Append('"');

                builder.Append(node.TitleInferred
                    ? "  title=invented"
                    : $"  title=heading:{node.HeadingLineId}");

                builder.AppendLine(node.ParagraphCount > 0
                    ? $"  paragraphs={node.ParagraphCount}"
                    : $"  subsections={node.DescendantCount}");
            }

            if (window.OpenQuestions.Count > 0)
            {
                builder.AppendLine("  open questions:");
                foreach (var question in window.OpenQuestions)
                {
                    builder.Append("  ? ").AppendLine(question);
                }
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private RoutingContext Routing(StructureContext context, IReadOnlyList<ChatMessage> conversation) =>
        new(
            context.RunId, context.DocumentId, PipelinePhase.InferStructure, AgentRole.StructureOrchestrator,
            ModelTier.Large, context.Sensitivity,
            EstimatedInputTokens: 1_000 + conversation.Sum(m => m.Text.Length) / 4,
            // The plan is one row per section of the finished document, and a row is short. This is
            // generous against an outline of a few hundred sections, and deliberately not scaled
            // off the skeleton: the orchestrator never sees one.
            MaxOutputTokens: 16_384)
        {
            PinnedModelKey = context.PinnedOrchestratorKey,
            PromptVersion = runner.Assets.VersionFor(AgentRole.StructureOrchestrator),
            Unit = $"orchestrator:{conversation.Count}",
            OutputSchema = runner.Assets.WireSchemaElement("assembly-plan"),
            OutputSchemaName = "assembly-plan",
        };
}
