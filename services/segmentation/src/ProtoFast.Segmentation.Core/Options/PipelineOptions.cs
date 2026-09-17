namespace ProtoFast.Segmentation.Core.Options;

/// <summary>
/// Every tunable number the deterministic pipeline reads, bound from <c>Seg_Pipeline__*</c>
/// (plan §20.3). Defaults here are the plan's; they are starting points to move against the
/// gold set, not constants — which is exactly why none of them is hard-coded at a call site.
/// </summary>
public sealed class PipelineOptions
{
    public const string SectionName = "Pipeline";

    public WindowingOptions Windowing { get; set; } = new();

    public TriageOptions Triage { get; set; } = new();

    public LabelingOptions Labeling { get; set; } = new();

    public RepairOptions Repair { get; set; } = new();

    public ParagraphOptions Paragraphs { get; set; } = new();

    public HumanGateOptions HumanGate { get; set; } = new();

    public AugmentationOptions Augmentation { get; set; } = new();

    public CleaningOptions Cleaning { get; set; } = new();
}

public sealed class WindowingOptions
{
    /// <summary>Hard ceiling on lines per window; the smaller of this and the token budget wins.</summary>
    public int MaxLines { get; set; } = 200;

    public int MaxInputTokens { get; set; } = 6000;

    /// <summary>Context overlap on each side, as a fraction of the commit region (plan §10.1).</summary>
    public double OverlapFraction { get; set; } = 0.25;

    /// <summary>Characters per token used to size windows before a real tokenizer is available.</summary>
    public double CharsPerToken { get; set; } = 3.8;
}

public sealed class TriageOptions
{
    public double OversizeMedianMultiplier { get; set; } = 3.0;

    public int OversizeMinWords { get; set; } = 250;

    public double MinPunctuationPer40Words { get; set; } = 1.0;

    /// <summary>Above this suspect fraction, the whole document becomes one suspect region (plan §9.4).</summary>
    public double WholeDocSuspectFraction { get; set; } = 0.6;

    /// <summary>Lines of un-bounded text that count as a "structureless region" (≈ 2 pages).</summary>
    public int StructurelessRunLines { get; set; } = 90;
}

public sealed class LabelingOptions
{
    public double UncertainConfidence { get; set; } = 0.6;

    public int MaxFollowUpRounds { get; set; } = 3;

    /// <summary>Headings per heading-level call in phase 3b.</summary>
    public int HeadingBatchSize { get; set; } = 300;
}

public sealed class RepairOptions
{
    public int MaxRoundsPerArtifact { get; set; } = 2;

    public bool EscalateOnceBeforeHuman { get; set; } = true;
}

public sealed class ParagraphOptions
{
    public int MinWords { get; set; } = 15;

    public int MaxWords { get; set; } = 300;
}

public sealed class HumanGateOptions
{
    /// <summary>A family with fewer approved documents than this always gates (plan §9.10).</summary>
    public int MinApprovedPerFamily { get; set; } = 5;

    public bool RequireForRestricted { get; set; } = true;
}

public sealed class AugmentationOptions
{
    public double ReviewSampleRate { get; set; } = 0.10;

    /// <summary>Paragraphs per augmentation call when the type allows batching by section.</summary>
    public int MaxParagraphsPerCall { get; set; } = 4;

    /// <summary>Fan-out batch size per superstep, so a 10k-paragraph document does not explode.</summary>
    public int FanOutBatchSize { get; set; } = 50;
}

public sealed class CleaningOptions
{
    /// <summary>Fraction of page height counted as the header/footer margin zone.</summary>
    public double MarginZoneFraction { get; set; } = 0.08;

    /// <summary>A repeating margin line on at least this share of pages is a running header/footer.</summary>
    public double RunningHeaderPageFraction { get; set; } = 0.30;

    /// <summary>A line at least this wide (relative to the column) is "full width" for continuation tests.</summary>
    public double FullWidthThreshold { get; set; } = 0.9;

    public double ShortLineThreshold { get; set; } = 0.8;

    public double HeadingFontScale { get; set; } = 1.1;

    public double HeadingGapAbove { get; set; } = 1.3;

    public double ParagraphGapAbove { get; set; } = 1.3;

    public double ParagraphIndent { get; set; } = 0.02;
}
