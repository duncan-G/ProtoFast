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

    public StructureOptions Structure { get; set; } = new();

    public PresentationOptions Presentation { get; set; } = new();

    public ItemOptions Items { get; set; } = new();

    public FamilyScopeOptions FamilyScopes { get; set; } = new();

    public SceneCutOptions Scenes { get; set; } = new();

    public SceneLinkOptions SceneLinks { get; set; } = new();
}

/// <summary>Phase 5 (scene plan §8.5).</summary>
public sealed class PresentationOptions
{
    /// <summary>
    /// Leading and trailing paragraphs offered to the classifier as candidates. Front matter is a
    /// prefix and back matter a suffix — that is the invariant the window planner can guarantee,
    /// and it is why the phase is a fan-out rather than an orchestration (§8.2).
    /// </summary>
    public int EdgeParagraphs { get; set; } = 40;

    /// <summary>Below this the classifier's answer is kept but the paragraph is flagged (§5.2).</summary>
    public double UncertainConfidence { get; set; } = 0.6;

    /// <summary>The <c>metadata-recall</c> band a family's metadata rate is expected to fall in.</summary>
    public double MinMetadataRate { get; set; } = 0.0;

    public double MaxMetadataRate { get; set; } = 0.35;

    public int FanOutBatchSize { get; set; } = 8;
}

/// <summary>Phase 8 (scene plan §8.6).</summary>
public sealed class ItemOptions
{
    /// <summary>Displayable paragraphs per typing window; items never cross a paragraph (§8.2).</summary>
    public int ParagraphsPerWindow { get; set; } = 12;

    /// <summary>Paragraphs of read-only context on each side, as phase 3 does for lines.</summary>
    public int WindowOverlapParagraphs { get; set; } = 2;

    public int FanOutBatchSize { get; set; } = 8;
}

/// <summary>Phase 7's derivation of family scopes (scene plan §6.1).</summary>
public sealed class FamilyScopeOptions
{
    /// <summary>Anthology stories and course-pack units sit at the top of the tree.</summary>
    public int MaxFamilyScopeDepth { get; set; } = 2;

    /// <summary>A three-paragraph vignette inside a textbook is an Enacted island, not a family.</summary>
    public int MinFamilyScopeParagraphs { get; set; } = 60;

    /// <summary>A volume holds tens of works; hundreds means the detector is chasing sections.</summary>
    public int MaxFamilyScopes { get; set; } = 32;

    /// <summary>
    /// How far a subtree's evidence must sit from its parent's before the disagreement is a family
    /// rather than noise. The learned band of <c>family-homogeneity</c> (§9).
    /// </summary>
    public double DisagreementBand { get; set; } = 0.35;
}

/// <summary>Phase 10 (scene plan §8.8).</summary>
public sealed class SceneCutOptions
{
    /// <summary>Sentences (§3.5). The floor an unmarked mode change must clear to become a cut.</summary>
    public int DefaultMinSceneSpan { get; set; } = 2;

    /// <summary>
    /// Priors for the S4 sweep rather than constants. The transcript is highest because the
    /// passing aside is its characteristic failure [unit §7 case 2]; the textbook is low because
    /// its short Enacted islands are precisely the spans a renderer wants; the screenplay is 1
    /// because its mode changes are marked, so the floor never runs there anyway.
    /// </summary>
    public IDictionary<string, int> MinSceneSpanByFamily { get; set; }
        = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
          { ["novel"] = 2, ["textbook"] = 2, ["transcript"] = 3, ["screenplay"] = 1 };

    /// <summary>C14, advisory: paragraphs touched, kept as the human-legible measure.</summary>
    public int OversizedSceneParagraphs { get; set; } = 40;

    /// <summary>Beyond this, <c>deep-setting-inheritance</c> is raised for review [unit §3.1.1].</summary>
    public int MaxInheritanceDepth { get; set; } = 3;

    public int FanOutBatchSize { get; set; } = 8;
}

/// <summary>Phase 11 (scene plan §8.9).</summary>
public sealed class SceneLinkOptions
{
    /// <summary>Caps the fan one scene can accumulate.</summary>
    public int MaxLinksPerScene { get; set; } = 4;

    public int FanOutBatchSize { get; set; } = 8;

    public int MaxOrchestratorRounds { get; set; } = 3;

    /// <summary>Consecutive scenes per link window, with overlap so a near frame stays in-window.</summary>
    public int ScenesPerWindow { get; set; } = 40;

    public int WindowOverlapScenes { get; set; } = 8;
}

/// <summary>
/// How phase 5 builds the tree when a model is involved (orchestrator plan §5).
///
/// <para>Neither strategy touches the deterministic path: a document with a coherent heading
/// hierarchy and no suspect regions still costs zero model calls, because the strategy is read
/// only after that branch has been taken.</para>
/// </summary>
public sealed class StructureOptions
{
    /// <summary>
    /// Defaults to <see cref="StructureStrategy.Chunked"/> — the orchestration is an experiment
    /// until the gold set says otherwise (orchestrator plan §9), and the flag is what lets the
    /// same document be re-run both ways.
    /// </summary>
    public StructureStrategy Strategy { get; set; } = StructureStrategy.Chunked;

    /// <summary>Windows structured concurrently per batch, bounding provider load as in phase 3.</summary>
    public int FanOutBatchSize { get; set; } = 8;

    /// <summary>
    /// Orchestrator turns before the run falls back to the chunked splice. The conversation is
    /// deliberately not checkpointed, so this also bounds what a resumed run has to replay
    /// (orchestrator plan §6).
    /// </summary>
    public int MaxOrchestratorRounds { get; set; } = 4;

    public int MaxFollowUpsPerRound { get; set; } = 6;

    /// <summary>
    /// Skeleton entries of the previous window shown as read-only context, so a window agent can
    /// recognise a section that started before it. Mirrors <c>WindowingOptions.OverlapFraction</c>
    /// for labelling.
    /// </summary>
    public int WindowOverlapEntries { get; set; } = 40;

    /// <summary>Writes the orchestrator's capability-gap rows (orchestrator plan §12.3(d)).</summary>
    public bool EmitCapabilityGaps { get; set; } = true;
}

/// <summary>Which of the two model paths phase 5 takes (orchestrator plan §9).</summary>
public enum StructureStrategy
{
    /// <summary>Sequential parts joined by a splice — what <c>StructurerAgent</c> has always done.</summary>
    Chunked,

    /// <summary>Parallel window agents assembled by an orchestrator (orchestrator plan §4).</summary>
    Orchestrated,
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
