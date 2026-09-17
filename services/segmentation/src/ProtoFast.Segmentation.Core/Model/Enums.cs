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
    StructureReviewer,
    TreeRepairer,
    Augmenter,
    AugmentReviewer,
    ParagraphSummarizer,
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
/// The twelve pipeline phases of plan §9.1, in order. The numeric values are the artifact
/// prefixes (<c>00_lines.jsonl</c>…) and the <c>RerunFrom</c> argument, so they are pinned.
/// </summary>
public enum PipelinePhase
{
    Ingest = 0,
    Clean = 1,
    Triage = 2,
    Label = 3,
    Assemble = 4,
    InferStructure = 5,
    Validate = 6,
    ReviewStructure = 7,
    HumanGate = 8,
    Freeze = 9,
    Augment = 10,
    ReviewAugmentation = 11,
    Publish = 12,
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
