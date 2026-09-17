using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProtoFast.Segmentation.Core.Assembly;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;
using ProtoFast.Segmentation.Core.Tree;
using ProtoFast.Segmentation.Core.Validation;
using ProtoFast.Segmentation.Routing;

namespace ProtoFast.Segmentation.Pipeline.Agents;

/// <summary>The structurer's output: a materialized tree plus any paragraph edits it proposed.</summary>
public sealed record StructureResult(SectionNode Root, IReadOnlyList<ParagraphEdit> Edits, string? ModelKey);

/// <summary>
/// Phase 5 (plan §9.7, §10.2). Builds the section tree from the skeleton.
///
/// <para>Two things keep this affordable on a long document. First, the skeleton — ids and one
/// excerpt per paragraph — rather than the text, which is also what makes it impossible for the
/// model to return altered content. Second, chunking: a skeleton that exceeds the input budget is
/// split at top-level headings, each part structured separately, and the parts joined under a
/// final pass over the outline alone (§9.7).</para>
/// </summary>
public sealed class StructurerAgent(
    AgentRunner runner,
    IOptions<PipelineOptions> options,
    ILogger<StructurerAgent> logger)
{
    /// <summary>
    /// Skeleton entries per structuring call. Chosen so a chunk's rendered skeleton stays well
    /// inside a large model's context with room for the tree it has to return, which is several
    /// times larger than the skeleton itself.
    /// </summary>
    public const int MaxSkeletonEntriesPerCall = 1_200;

    private readonly RepairOptions _repair = options.Value.Repair;

    public async Task<StructureResult> StructureAsync(
        IReadOnlyList<Paragraph> paragraphs,
        IReadOnlyList<HeadingRecord> headings,
        StructureContext context,
        CancellationToken ct = default)
    {
        var entries = SkeletonBuilder.Build(paragraphs, headings);

        if (entries.Count <= MaxSkeletonEntriesPerCall)
        {
            return await StructureOneAsync(entries, paragraphs, context, ct);
        }

        logger.LogInformation(
            "Skeleton for run {RunId} has {Count} entries; structuring in parts.",
            context.RunId, entries.Count);

        return await StructureInPartsAsync(entries, paragraphs, headings, context, ct);
    }

    private async Task<StructureResult> StructureOneAsync(
        IReadOnlyList<SkeletonBuilder.SkeletonEntry> entries,
        IReadOnlyList<Paragraph> paragraphs,
        StructureContext context,
        CancellationToken ct)
    {
        var expectedParagraphIds = entries.Where(e => !e.IsHeading).Select(e => e.Id).ToList();
        var headingLineIds = entries.Where(e => e.IsHeading).Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var skeleton = SkeletonBuilder.Render(entries);

        var prompt = new PromptTemplate(runner.Assets.Template("structurer.v1"))
            .Set("rules", runner.Assets.Rules)
            .Set("skill", runner.Assets.Skill("hierarchy-inference"))
            .Set("familySkill", runner.Assets.FamilySkill(context.Family))
            .Set("instincts", RenderInstincts(context.Instincts))
            .Set("skeleton", skeleton)
            .Render();

        var result = await runner.RunAsync<TreeProposal>(
            prompt,
            Routing(context, skeleton.Length, entries.Count),
            proposal => Validate(proposal, expectedParagraphIds, headingLineIds),
            _repair.MaxRoundsPerArtifact,
            buildRepairPrompt: (previous, errors) => BuildRepairPrompt(previous, errors, expectedParagraphIds),
            ct);

        if (!result.Success)
        {
            throw new PipelineFailureException(
                PipelinePhase.InferStructure,
                "The structurer could not produce a valid tree: " + result.Validation.ErrorReport);
        }

        var root = TreeMaterializer.Materialize(result.Value!.Tree!, context.DocumentTitle);
        return new StructureResult(root, TreeMaterializer.ToEdits(result.Value), result.Response?.Decision.Model.Key);
    }

    /// <summary>
    /// Splits at top-level headings, structures each part, then joins the parts. The join is code,
    /// not a second model call over the whole document: the parts are already valid trees, and
    /// re-deriving their contents would risk losing a paragraph that the first pass placed
    /// correctly.
    /// </summary>
    private async Task<StructureResult> StructureInPartsAsync(
        IReadOnlyList<SkeletonBuilder.SkeletonEntry> entries,
        IReadOnlyList<Paragraph> paragraphs,
        IReadOnlyList<HeadingRecord> headings,
        StructureContext context,
        CancellationToken ct)
    {
        var parts = SplitSkeleton(entries, headings);
        var children = new List<SectionNode>();
        var edits = new List<ParagraphEdit>();
        string? modelKey = null;

        foreach (var part in parts)
        {
            var partResult = await StructureOneAsync(part, paragraphs, context, ct);
            modelKey ??= partResult.ModelKey;
            edits.AddRange(partResult.Edits);

            // Each part came back wrapped in its own synthetic root. Unwrapping it is what makes
            // the join a splice rather than a nest — otherwise every part would add a level of
            // depth that the document does not have.
            children.AddRange(partResult.Root.Children.Count > 0 ? partResult.Root.Children : [partResult.Root]);
        }

        var counter = 0;
        var root = Renumber(
            new SectionNode(string.Empty, context.DocumentTitle, true, null, 1, children, []),
            depth: 1,
            ref counter);

        return new StructureResult(root, edits, modelKey);
    }

    /// <summary>
    /// Section ids are minted per call, so parts from different calls collide. Renumbering in
    /// document order restores the invariant that <c>S0001</c> is the first section a reader sees.
    /// </summary>
    private static SectionNode Renumber(SectionNode node, int depth, ref int counter)
    {
        var id = Ids.Section(counter++);
        var children = new List<SectionNode>(node.Children.Count);

        foreach (var child in node.Children)
        {
            children.Add(Renumber(child, depth + 1, ref counter));
        }

        return node with { SectionId = id, Level = Math.Min(depth, 6), Children = children };
    }

    /// <summary>
    /// Cuts the skeleton at level-1 headings, and — when the document has none — at fixed-size
    /// chunks. The plan suggests embedding-based cut points for the headingless case (§9.7); this
    /// takes the simpler route because a wrong cut is repaired by the final outline pass either
    /// way, and an embedding call per paragraph is a real cost on exactly the documents that are
    /// already the most expensive.
    /// </summary>
    private static List<List<SkeletonBuilder.SkeletonEntry>> SplitSkeleton(
        IReadOnlyList<SkeletonBuilder.SkeletonEntry> entries,
        IReadOnlyList<HeadingRecord> headings)
    {
        var topLevel = headings
            .Where(h => h.Level is null or 1)
            .Select(h => h.LineId)
            .ToHashSet(StringComparer.Ordinal);

        var parts = new List<List<SkeletonBuilder.SkeletonEntry>>();
        var current = new List<SkeletonBuilder.SkeletonEntry>();

        foreach (var entry in entries)
        {
            var atTopLevelHeading = entry.IsHeading && topLevel.Contains(entry.Id);
            var atCapacity = current.Count >= MaxSkeletonEntriesPerCall;

            if (current.Count > 0 && (atTopLevelHeading || atCapacity))
            {
                parts.Add(current);
                current = [];
            }

            current.Add(entry);
        }

        if (current.Count > 0)
        {
            parts.Add(current);
        }

        return parts;
    }

    private static ValidationResult Validate(
        TreeProposal proposal,
        IReadOnlyList<string> expectedParagraphIds,
        IReadOnlySet<string> headingLineIds)
    {
        if (proposal.Tree is null)
        {
            return ValidationResult.Fail(Checks.Schema, "The reply had no 'tree' property.");
        }

        var root = TreeMaterializer.Materialize(proposal.Tree);

        var coverage = Checks.CheckIdCoverage(
            expectedParagraphIds, [.. root.Descend().SelectMany(n => n.ParagraphIds)]);
        if (!coverage.Passed)
        {
            return coverage;
        }

        var shape = Checks.CheckTreeShape(root);
        if (!shape.Passed)
        {
            return shape;
        }

        return Checks.CheckHeadingAnchor(root, headingLineIds);
    }

    private string BuildRepairPrompt(string previousReply, string errorReport, IReadOnlyList<string> scope) =>
        new PromptTemplate(runner.Assets.Template("tree-repair.v1"))
            .Set("rules", runner.Assets.Rules)
            .Set("skill", runner.Assets.Skill("tree-repair"))
            .Set("errorReport", errorReport)
            .Set("artifact", ModelJson.Extract(previousReply))
            .Set("scope", string.Join(", ", scope))
            .Render();

    private RoutingContext Routing(StructureContext context, int skeletonChars, int entryCount) =>
        new(
            context.RunId, context.DocumentId, PipelinePhase.InferStructure, AgentRole.Structurer,
            ModelTier.Large, context.Sensitivity,
            EstimatedInputTokens: 2_000 + skeletonChars / 4,
            // The tree is several times the skeleton's size: every paragraph id reappears, wrapped
            // in JSON, plus the titles the model invents.
            MaxOutputTokens: Math.Max(2_048, entryCount * 24))
        {
            PinnedModelKey = context.PinnedStructurerKey,
            PromptVersion = runner.Assets.VersionFor(AgentRole.Structurer),
            Unit = $"skeleton:{entryCount}",
        };

    private static string RenderInstincts(IReadOnlyList<string> instincts) =>
        instincts.Count == 0
            ? string.Empty
            : "## Learned guidance for this family\n" + string.Join('\n', instincts.Select(i => "- " + i));
}
