using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using ProtoFast.Segmentation.Core.Ingest;
using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.Pipeline.Agents;

/// <summary>The labeler's wire format, matching <c>labels.schema.json</c>.</summary>
public sealed record LabelWindowReply
{
    [JsonPropertyName("window")]
    public int Window { get; init; }

    [JsonPropertyName("labels")]
    public IReadOnlyList<LabelReply> Labels { get; init; } = [];
}

public sealed record LabelReply
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    [JsonPropertyName("conf")]
    public double Confidence { get; init; }

    [JsonPropertyName("kind")]
    public string? Kind { get; init; }

    public LineLabelResult ToResult() => new(
        Id,
        Enum.TryParse<LineLabel>(Label, ignoreCase: true, out var label) ? label : LineLabel.Cont,
        null,
        Confidence,
        Enum.TryParse<OtherKind>(Kind?.Replace("_", string.Empty), ignoreCase: true, out var kind) ? kind : OtherKind.None);
}

/// <summary>Builds the labeler's prompts (Appendix A.1, A.2).</summary>
public static class LabelPrompts
{
    public static string Build(
        PromptAssets assets,
        LabelWindow window,
        DocumentStatistics statistics,
        string family,
        IReadOnlyList<string> instincts,
        IReadOnlyList<string> runningOutline,
        IReadOnlyList<LineLabelResult> previousLabels)
    {
        return new PromptTemplate(assets.Template("labeler.v1"))
            .Set("rules", assets.Rules)
            .Set("skill", assets.Skill("layout-labeling"))
            .Set("familySkill", assets.FamilySkill(family))
            .Set("instincts", RenderInstincts(instincts))
            .Set("statistics", RenderStatistics(statistics))
            .Set("outline", runningOutline.Count == 0 ? "(no headings committed yet)" : string.Join('\n', runningOutline))
            .Set("previousLabels", previousLabels.Count == 0
                ? "(this is the first window)"
                : string.Join(", ", previousLabels.TakeLast(3).Select(l => $"{l.LineId}:{l.Label.ToString().ToUpperInvariant()}")))
            .Set("lines", RenderLines(window))
            .Set("firstId", window.Lines.Count > 0 ? window.Lines[0].LineId : string.Empty)
            .Set("lastId", window.Lines.Count > 0 ? window.Lines[^1].LineId : string.Empty)
            .Set("window", window.WindowIndex)
            .Render();
    }

    public static string BuildFollowUp(
        PromptAssets assets,
        LabelWindow window,
        IReadOnlyList<(LineLabelResult Label, string Evidence)> uncertain)
    {
        var uncertainties = new StringBuilder();
        foreach (var (label, evidence) in uncertain)
        {
            uncertainties.AppendLine(CultureInfo.InvariantCulture,
                $"- {label.LineId}: you labelled {label.Label.ToString().ToUpperInvariant()} " +
                $"(conf {label.Confidence:0.00}). Evidence: {evidence}.");
        }

        return new PromptTemplate(assets.Template("labeler-followup.v1"))
            .Set("rules", assets.Rules)
            .Set("window", window.WindowIndex)
            .Set("uncertainties", uncertainties.ToString())
            .Set("context", RenderContext(window, uncertain.Select(u => u.Label.LineId).ToHashSet(StringComparer.Ordinal)))
            .Set("ids", string.Join(", ", uncertain.Select(u => u.Label.LineId)))
            .Render();
    }

    /// <summary>
    /// The compact line format of plan §10.1. When a line has no layout the feature columns are
    /// omitted entirely rather than filled with defaults — a fabricated <c>w=1.0</c> would read as
    /// evidence of a full-width line rather than as an absence of evidence.
    /// </summary>
    internal static string RenderLines(LabelWindow window)
    {
        var builder = new StringBuilder();

        foreach (var line in window.Lines)
        {
            builder.Append(line.LineId).Append('|');

            if (line.Layout is { } layout)
            {
                builder
                    .Append('p').Append(layout.Page.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append('t').Append(layout.Top.ToString("0.00", CultureInfo.InvariantCulture)).Append('|')
                    .Append('i').Append(layout.Indent.ToString("0.00", CultureInfo.InvariantCulture)).Append('|')
                    .Append('w').Append(layout.Width.ToString("0.00", CultureInfo.InvariantCulture)).Append('|')
                    .Append('g').Append(layout.GapAbove.ToString("0.0", CultureInfo.InvariantCulture)).Append('|')
                    .Append('f').Append(layout.FontScale.ToString("0.0", CultureInfo.InvariantCulture)).Append('|')
                    .Append('b').Append(layout.IsBold ? '1' : '0').Append('|');
            }

            if (line.Hint != SourceHint.None)
            {
                builder.Append("hint=").Append(line.Hint.ToString().ToLowerInvariant()).Append('|');
            }

            builder.Append('"').Append(line.Text.Replace("\"", "'", StringComparison.Ordinal)).Append('"');
            builder.AppendLine();
        }

        return MarkCommitRegion(window, builder.ToString());
    }

    /// <summary>
    /// Annotates the lines outside the commit region. The model is told which answers are kept
    /// because otherwise it spends the same care on overlap it will never be asked about, and —
    /// more importantly — a labeller that does not know it is looking at context cannot tell that
    /// the paragraph it is mid-way through started before the window did.
    /// </summary>
    private static string MarkCommitRegion(LabelWindow window, string rendered)
    {
        var lines = rendered.TrimEnd().Split('\n');
        var builder = new StringBuilder();

        for (var i = 0; i < lines.Length; i++)
        {
            var absolute = window.StartIndex + i;
            builder.Append(lines[i]);

            if (!window.Commits(absolute))
            {
                builder.Append("   [context]");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    /// <summary>
    /// The uncertain lines plus five lines either side, with gaps elided. A follow-up that
    /// re-sent the whole window would cost as much as the original call and answer a narrower
    /// question (plan §10.1).
    /// </summary>
    private static string RenderContext(LabelWindow window, IReadOnlySet<string> focus)
    {
        const int radius = 5;
        var wanted = new SortedSet<int>();

        for (var i = 0; i < window.Lines.Count; i++)
        {
            if (!focus.Contains(window.Lines[i].LineId))
            {
                continue;
            }

            for (var j = Math.Max(0, i - radius); j <= Math.Min(window.Lines.Count - 1, i + radius); j++)
            {
                wanted.Add(j);
            }
        }

        var builder = new StringBuilder();
        var previous = -2;

        foreach (var index in wanted)
        {
            if (index != previous + 1 && previous >= 0)
            {
                builder.AppendLine("…");
            }

            var line = window.Lines[index];
            builder.Append(line.LineId).Append(" \"").Append(line.Text).AppendLine("\"");
            previous = index;
        }

        return builder.ToString();
    }

    internal static string RenderStatistics(DocumentStatistics statistics) => statistics.HasLayout
        ? $"pages={statistics.PageCount} columns={statistics.ColumnCount} " +
          $"body_font={TextMetrics.ToInvariant(statistics.BodyFontSize)} " +
          $"median_gap={TextMetrics.ToInvariant(statistics.MedianLineSpacing)} " +
          $"median_block_words={statistics.MedianBlockWords}"
        : $"no layout metadata; text features only. lines={statistics.LineCount} " +
          $"median_block_words={statistics.MedianBlockWords}";

    private static string RenderInstincts(IReadOnlyList<string> instincts) =>
        instincts.Count == 0
            ? string.Empty
            : "## Learned guidance for this family\n" + string.Join('\n', instincts.Select(i => "- " + i));
}
