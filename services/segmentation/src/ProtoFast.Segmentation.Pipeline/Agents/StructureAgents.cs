using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using ProtoFast.Segmentation.Core.Assembly;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;
using ProtoFast.Segmentation.Core.Tree;
using ProtoFast.Segmentation.Core.Validation;
using ProtoFast.Segmentation.Routing;

namespace ProtoFast.Segmentation.Pipeline.Agents;

/// <summary>The heading-level pass's wire format.</summary>
public sealed record HeadingLevelsReply
{
    [JsonPropertyName("levels")]
    public IReadOnlyList<HeadingLevelReply> Levels { get; init; } = [];
}

public sealed record HeadingLevelReply
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("level")]
    public int Level { get; init; } = 1;

    [JsonPropertyName("conf")]
    public double Confidence { get; init; } = 1;
}

/// <summary>The structure reviewer's wire format, matching <c>review.schema.json</c>.</summary>
public sealed record ReviewReply
{
    [JsonPropertyName("verdict")]
    public string Verdict { get; init; } = "pass";

    [JsonPropertyName("findings")]
    public IReadOnlyList<FindingReply> Findings { get; init; } = [];

    public IReadOnlyList<Finding> ToFindings() =>
    [
        .. Findings.Select(f => new Finding(
            Enum.TryParse<FindingSeverity>(f.Severity, ignoreCase: true, out var severity) ? severity : FindingSeverity.Low,
            f.Type,
            f.Ids,
            f.Message)),
    ];
}

public sealed record FindingReply
{
    [JsonPropertyName("severity")]
    public string Severity { get; init; } = "low";

    [JsonPropertyName("type")]
    public string Type { get; init; } = "other";

    [JsonPropertyName("ids")]
    public IReadOnlyList<string> Ids { get; init; } = [];

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}

/// <summary>Document-level facts every structure agent needs.</summary>
public sealed record StructureContext(
    string RunId,
    string DocumentId,
    string DocumentTitle,
    string Family,
    Sensitivity Sensitivity,
    IReadOnlyList<string> Instincts,
    string? PinnedStructurerKey,
    string? PinnedLevelerKey);

/// <summary>
/// Phase 3b (plan §10.1): one sequential pass over the detected headings.
///
/// <para>It is separate from labelling because heading levels are a document-wide judgement that
/// parallel windows cannot make — window 4 has no way to know whether its heading is a peer of
/// window 17's. Its input is small (the headings only, not the document), so one call covers a
/// few hundred headings and the sequential dependency costs almost nothing.</para>
/// </summary>
public sealed class HeadingLevelAgent(AgentRunner runner, IOptions<PipelineOptions> options)
{
    private readonly LabelingOptions _labeling = options.Value.Labeling;
    private readonly RepairOptions _repair = options.Value.Repair;

    public async Task<IReadOnlyList<HeadingRecord>> AssignAsync(
        IReadOnlyList<HeadingRecord> headings,
        StructureContext context,
        CancellationToken ct = default)
    {
        // The deterministic assigner answers most documents outright — numbered headings, or a
        // clean set of font tiers. No call is made for those (plan §10.1).
        if (!HeadingLevelAssigner.NeedsModel(headings))
        {
            return HeadingLevelAssigner.Assign(headings);
        }

        var assigned = new List<HeadingRecord>(headings.Count);

        foreach (var batch in headings.Chunk(_labeling.HeadingBatchSize))
        {
            var ids = batch.Select(h => h.LineId).ToList();

            var prompt = new PromptTemplate(runner.Assets.Template("heading-levels.v1"))
                .Set("rules", runner.Assets.Rules)
                .Set("skill", runner.Assets.Skill("heading-levels"))
                .Set("headings", RenderHeadings(batch))
                .Render();

            var result = await runner.RunAsync<HeadingLevelsReply>(
                prompt,
                new RoutingContext(
                    context.RunId, context.DocumentId, PipelinePhase.Label, AgentRole.HeadingLeveler,
                    ModelTier.Small, context.Sensitivity,
                    EstimatedInputTokens: 800 + batch.Length * 20,
                    MaxOutputTokens: Math.Max(256, batch.Length * 16))
                {
                    PinnedModelKey = context.PinnedLevelerKey,
                    PromptVersion = runner.Assets.VersionFor(AgentRole.HeadingLeveler),
                    Unit = $"headings:{batch.Length}",
                },
                reply => Checks.CheckIdCoverage(ids, [.. reply.Levels.Select(l => l.Id)], requireOrder: false),
                _repair.MaxRoundsPerArtifact, buildRepairPrompt: null, ct);

            if (!result.Success)
            {
                // Falling back to the deterministic assigner is better than failing the run: it is
                // what the pipeline would have used anyway had the evidence been a little stronger.
                assigned.AddRange(HeadingLevelAssigner.Assign(batch));
                continue;
            }

            var levels = result.Value!.Levels.ToDictionary(l => l.Id, l => l.Level, StringComparer.Ordinal);
            assigned.AddRange(batch.Select(h =>
                h with { Level = levels.TryGetValue(h.LineId, out var level) ? level : h.Level ?? 1 }));
        }

        // Normalize whatever came back: the model may skip a level, and tree-shape requires levels
        // to increase by depth.
        return HeadingLevelAssigner.Normalize(assigned);
    }

    private static string RenderHeadings(IEnumerable<HeadingRecord> headings)
    {
        var builder = new StringBuilder();
        foreach (var heading in headings)
        {
            builder.Append(heading.LineId).Append(" | ");

            if (heading.Layout is { } layout)
            {
                builder
                    .Append("page ").Append(layout.Page.ToString(CultureInfo.InvariantCulture)).Append(" | f")
                    .Append(layout.FontScale.ToString("0.00", CultureInfo.InvariantCulture))
                    .Append(layout.IsBold ? " | bold" : string.Empty).Append(" | ");
            }

            var depth = Core.Ingest.TextMetrics.NumberingDepth(heading.Text);
            if (depth > 0)
            {
                builder.Append("numbering depth ").Append(depth.ToString(CultureInfo.InvariantCulture)).Append(" | ");
            }

            builder.Append('"').Append(heading.Text).AppendLine("\"");
        }

        return builder.ToString();
    }
}
