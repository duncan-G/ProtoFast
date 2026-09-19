using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.Data.Entities;

/// <summary>
/// One document's pass through the pipeline — the unit of queueing, checkpointing and billing
/// (plan §8.4). Every read and write filters on <see cref="OwnerSubject"/>, which comes from the
/// internal JWT's <c>sub</c> and never from a request field (F17).
/// </summary>
public sealed class Run
{
    /// <summary>ULID; also the SQS message deduplication key.</summary>
    public required string RunId { get; set; }

    /// <summary>Keycloak <c>sub</c> of the owner. The only authorization key in this schema.</summary>
    public required string OwnerSubject { get; set; }

    /// <summary>The caller's own document identifier; free-form, not interpreted here.</summary>
    public required string DocumentId { get; set; }

    /// <summary>
    /// The <b>production</b> family: how the file was made (scene plan §6). It governs cleaning,
    /// running apparatus, OCR repair and layout trust. The name is unchanged because every existing
    /// consumer means this one.
    /// </summary>
    public string DocumentFamily { get; set; } = Core.Ingest.FamilyDetector.Unknown;

    /// <summary>
    /// The <b>composition</b> family: what kind of work it is (scene plan §6). It governs metadata
    /// policy, item typing, scene cutting and persona scope, and is the single strongest predictor of
    /// everything in the scene plan — which is why it is a field with a detector rather than a prompt
    /// hint.
    ///
    /// <para>A scanned PDF of a novel and a scanned PDF of a textbook share every production instinct
    /// and almost no composition instinct, so one field cannot carry both. It is the <em>fallback</em>
    /// of the §6.1 resolution: a section carrying its own family scope overrides it for its subtree.</para>
    /// </summary>
    public string CompositionFamily { get; set; } = Core.Classification.CompositionFamily.Unknown;

    public Sensitivity Sensitivity { get; set; } = Sensitivity.Internal;

    public ConditionBucket Condition { get; set; } = ConditionBucket.Clean;

    public RunPriority Priority { get; set; } = RunPriority.Realtime;

    /// <summary>S3 key prefix of the upload this run consumed.</summary>
    public required string UploadId { get; set; }

    /// <summary>
    /// The client's idempotency key for submission. Unique per owner, so a retried
    /// <c>SubmitRun</c> returns the same run rather than a second run and a second bill (plan §17).
    /// </summary>
    public required string IdempotencyKey { get; set; }

    /// <summary>Augmentation type names requested, comma-separated.</summary>
    public string Augmentations { get; set; } = string.Empty;

    /// <summary>Phase name to <c>provider/model</c>, pinned on the first successful call (plan §14.6).</summary>
    public Dictionary<string, string> PinnedModels { get; set; } = [];

    public bool RequiresReview { get; set; }

    /// <summary><c>none | pending | approved | rejected</c>.</summary>
    public string ReviewState { get; set; } = "none";

    public bool Cancelled { get; set; }

    /// <summary>Set when the run reaches a terminal failure the worker cannot retry past.</summary>
    public string? Error { get; set; }

    /// <summary>Running total from the <see cref="ModelCall"/> ledger, denormalized for listing.</summary>
    public decimal CostUsd { get; set; }

    public string? TreeHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? FrozenAt { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public ICollection<RunPhase> Phases { get; set; } = [];

    public ICollection<RunEvent> Events { get; set; } = [];
}
