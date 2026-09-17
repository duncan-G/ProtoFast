using System.Text.Json.Serialization;
using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.Core.Evaluation;

/// <summary>
/// One annotated document in the gold set (plan §26.1). It carries the same records the pipeline
/// produces, so evaluation compares artifacts directly rather than through a bespoke format that
/// could itself be wrong.
/// </summary>
public sealed record GoldDocument
{
    [JsonPropertyName("documentId")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("condition")]
    public ConditionBucket Condition { get; init; } = ConditionBucket.Clean;

    [JsonPropertyName("family")]
    public string Family { get; init; } = Ingest.FamilyDetector.Unknown;

    /// <summary>The uploaded Markdown, verbatim.</summary>
    [JsonPropertyName("markdown")]
    public required string Markdown { get; init; }

    /// <summary>Optional layout sibling, when the gold document has one.</summary>
    [JsonPropertyName("layout")]
    public Ingest.LayoutDocument? Layout { get; init; }

    /// <summary>Adjudicated paragraph texts, in document order.</summary>
    [JsonPropertyName("paragraphs")]
    public IReadOnlyList<string> Paragraphs { get; init; } = [];

    /// <summary>Adjudicated headings: the text, and the level.</summary>
    [JsonPropertyName("headings")]
    public IReadOnlyList<GoldHeading> Headings { get; init; } = [];

    /// <summary>Text of lines that are running headers, footers or page numbers.</summary>
    [JsonPropertyName("artifacts")]
    public IReadOnlyList<string> Artifacts { get; init; } = [];
}

public sealed record GoldHeading
{
    [JsonPropertyName("text")]
    public required string Text { get; init; }

    [JsonPropertyName("level")]
    public int Level { get; init; } = 1;

    /// <summary>
    /// Index into <see cref="GoldDocument.Paragraphs"/> of the first paragraph this heading
    /// introduces.
    ///
    /// <para>Without it the reference tree cannot be reconstructed: headings and paragraphs are
    /// two separate ordered lists, and a tree built by appending all the headings first is not the
    /// document's structure — it scores a maximal edit distance against a run that got the answer
    /// exactly right, which is worse than no metric at all.</para>
    /// </summary>
    [JsonPropertyName("beforeParagraph")]
    public int BeforeParagraph { get; init; }
}

/// <summary>The metric bundle one document's evaluation produces (plan §26.2).</summary>
public sealed record DocumentMetrics(
    string DocumentId,
    ConditionBucket Condition,
    string Family,
    double Pk,
    double WindowDiff,
    PrecisionRecall BoundaryF1Exact,
    PrecisionRecall BoundaryF1Tolerant,
    PrecisionRecall HeadingF1,
    double HeadingLevelAccuracy,
    double TreeEditDistance,
    PrecisionRecall ArtifactRemoval,
    bool TextIntegrityPassed,
    bool TreeSchemaPassed,
    decimal CostUsd,
    TimeSpan Duration)
{
    /// <summary>Did this run clear every threshold for its condition bucket? The pass^k unit.</summary>
    public bool Passes(AcceptanceThresholds thresholds) =>
        TextIntegrityPassed
        && TreeSchemaPassed
        && Pk <= thresholds.MaxPk
        && WindowDiff <= thresholds.MaxWindowDiff
        && HeadingF1.F1 >= thresholds.MinHeadingF1
        && (double.IsNaN(HeadingLevelAccuracy) || HeadingLevelAccuracy >= thresholds.MinHeadingLevelAccuracy)
        && TreeEditDistance <= thresholds.MaxTreeEditDistance
        && ArtifactRemoval.Recall >= thresholds.MinArtifactRecall;
}

/// <summary>
/// The acceptance criteria of plan §6, per condition bucket. They are data rather than constants
/// because the plan is explicit that they are initial values to tune once a baseline exists — and
/// because the CI gate compares against the last accepted baseline, not against a literal.
/// </summary>
public sealed record AcceptanceThresholds(
    double MaxPk,
    double MaxWindowDiff,
    double MinHeadingF1,
    double MinHeadingLevelAccuracy,
    double MaxTreeEditDistance,
    double MinArtifactRecall)
{
    public static AcceptanceThresholds For(ConditionBucket condition) => condition switch
    {
        ConditionBucket.Clean => new AcceptanceThresholds(0.10, 0.12, 0.95, 0.90, 0.15, 0.98),
        ConditionBucket.Partial => new AcceptanceThresholds(0.15, 0.18, 0.90, 0.90, 0.15, 0.98),
        _ => new AcceptanceThresholds(0.20, 0.25, 0.85, 0.90, 0.15, 0.98),
    };
}
