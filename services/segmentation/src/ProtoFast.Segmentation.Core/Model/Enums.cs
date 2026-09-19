namespace ProtoFast.Segmentation.Core.Model;

/// <summary>What the Markdown converter claimed about a line, before anything verified it.</summary>
public enum SourceHint
{
    None,
    MarkdownHeading,
    BlankLineBefore,
    ListItem,
    TableRow,
    CodeFence,
    Quote,
}

/// <summary>The label a line carries once phase 1–3 are done (plan §8.1).</summary>
public enum LineLabel
{
    Cont,
    Para,
    Head,
    Artifact,
    Other,
}

/// <summary>Refinement of <see cref="LineLabel.Other"/>; carried through to paragraph kind.</summary>
public enum OtherKind
{
    None,
    Caption,
    Footnote,
    Table,
    ListItem,
    Code,
    Quote,
    Equation,
}

public enum BoundaryKind
{
    ParagraphStart,
    Heading,
}

/// <summary>
/// Where a boundary came from, in descending authority. <see cref="Trusted"/> and
/// <see cref="Human"/> boundaries may never be removed by a repair pass (check
/// <c>trusted-respect</c>, plan §11); LLM boundaries always lose to deterministic ones.
/// </summary>
public enum BoundarySource
{
    Trusted,
    Deterministic,
    Llm,
    Repair,
    Human,
}

public enum ParagraphKind
{
    Body,
    ListBlock,
    Table,
    Code,
    Quote,
    Caption,
    Footnote,
    Equation,
}

/// <summary>Data-handling class of a document; the router filters providers on it (plan §14.5).</summary>
public enum Sensitivity
{
    Public,
    Internal,
    Confidential,
    Restricted,
}

/// <summary>Assigned by triage (plan §9.4) and used to bucket every evaluation metric.</summary>
public enum ConditionBucket
{
    Clean,
    Partial,
    Degraded,
    Conversational,
}

public enum RunPriority
{
    Realtime,
    Bulk,
}

/// <summary>Capability class an agent asks the router for. Never downgraded (plan §14.5).</summary>
public enum ModelTier
{
    Small,
    Mid,
    Large,
}

/// <summary>The tasks a model can be qualified for (plan §14.2).</summary>
public enum AgentRole
{
    Labeler,
    HeadingLeveler,
    Structurer,

    /// <summary>
    /// Structures one window of the skeleton for the orchestrated strategy (orchestrator plan
    /// §4.1). Separate from <see cref="Structurer"/> because it is a different job at a different
    /// tier: a window agent sees a slice and is told so, and qualification for "structure a whole
    /// document" says nothing about it.
    /// </summary>
    StructureWindower,

    /// <summary>Assembles the window agents' subtrees into one tree (orchestrator plan §4.1).</summary>
    StructureOrchestrator,

    StructureReviewer,
    TreeRepairer,
    Augmenter,
    AugmentReviewer,
    ParagraphSummarizer,

    /// <summary>Separates displayable text from metadata, over candidates only (scene plan §8.5).</summary>
    PresentationClassifier,

    /// <summary>
    /// Partitions displayable paragraphs into typed items with candidate tags (scene plan §8.6).
    /// It never resolves a referent — that is phase 9's job, and splitting the work is what breaks
    /// the circularity between cast and speech attribution (§8.4).
    /// </summary>
    ItemTyper,

    /// <summary>Clusters surface forms inside one window of paragraphs (scene plan §8.7).</summary>
    PersonaWindower,

    /// <summary>
    /// Decides whether window 3's "the professor" is window 1's "Dr. Vance" — the join no window
    /// planner can guarantee, which is exactly the test that makes phase 9 an orchestration (§8.2).
    /// </summary>
    PersonaOrchestrator,

    /// <summary>Assigns Setting and Subject and confirms the deterministic boundaries (scene plan §8.8).</summary>
    SceneCutter,

    /// <summary>Proposes links inside a window of scene digests (scene plan §8.9).</summary>
    SceneLinkWindower,

    /// <summary>Settles the links whose two endpoints cannot be forced into one window (§8.9).</summary>
    SceneLinkOrchestrator,
}

public enum PhaseState
{
    Pending,
    Running,
    Done,
    Failed,
    Skipped,
}

/// <summary>
/// The eighteen pipeline phases of scene plan §8.1, in order. The numeric values are the artifact
/// prefixes (<c>00_lines.jsonl</c>…) and the <c>RerunFrom</c> argument, so they are pinned.
///
/// <para>The five scene phases are inserted rather than appended, which renumbers everything from
/// <see cref="ClassifyPresentation"/> onwards. That is safe in storage — <c>RunPhase.Phase</c> is
/// persisted by name, not by value — and it is what keeps the artifact prefixes in reading order,
/// which is the whole reason the numbers are the prefixes.</para>
/// </summary>
public enum PipelinePhase
{
    Ingest = 0,
    Clean = 1,
    Triage = 2,
    Label = 3,
    Assemble = 4,

    /// <summary>
    /// Separate displayable text from metadata, and emit the per-paragraph family evidence
    /// (scene plan §8.5). Before structure inference deliberately: front matter inferred <em>as
    /// sections</em> is noise in the tree, and withholding it first makes the tree both smaller
    /// and better.
    /// </summary>
    ClassifyPresentation = 5,

    InferStructure = 6,
    Validate = 7,

    /// <summary>Partition displayable paragraphs into typed items with candidate tags (§8.6).</summary>
    TypeItems = 8,

    /// <summary>Cluster surface forms into personas and bind every tag to a registry id (§8.7).</summary>
    ResolveReferents = 9,

    /// <summary>Assign situations and cut scene boundaries (§8.8).</summary>
    CutScenes = 10,

    /// <summary>Infer the scene links a coordinate cannot derive (§8.9). Skippable, usually skipped.</summary>
    LinkScenes = 11,

    ReviewStructure = 12,
    HumanGate = 13,
    Freeze = 14,
    Augment = 15,
    ReviewAugmentation = 16,
    Publish = 17,
}

public enum ReviewDecisionKind
{
    Approve,
    ApproveWithEdits,
    Reject,
}

public enum FindingSeverity
{
    Low,
    Medium,
    High,
}
