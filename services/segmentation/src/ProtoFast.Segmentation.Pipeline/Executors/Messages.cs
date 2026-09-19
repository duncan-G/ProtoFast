using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Storage;

namespace ProtoFast.Segmentation.Pipeline.Executors;

/// <summary>
/// The messages executors pass between themselves.
///
/// <para>Every one of them carries artifact <em>references</em> rather than payloads (plan §13.2).
/// That is what keeps checkpoints small enough to write on every superstep, and it is what makes
/// each phase independently inspectable: a run that went wrong can be diagnosed by reading the
/// S3 objects, with no need to reproduce it.</para>
/// </summary>
public sealed record RunStart(string RunId)
{
    /// <summary>Set by <c>RerunFrom</c>; phases before this one are reused from their artifacts.</summary>
    public PipelinePhase? FromPhase { get; init; }
}

public sealed record IngestComplete(string RunId, ArtifactRef Lines, ArtifactRef Statistics);

public sealed record CleanComplete(string RunId, ArtifactRef Clean, ArtifactRef Boundaries);

public sealed record TriageComplete(string RunId, ArtifactRef Triage, bool NeedsLabeling);

/// <summary>One unit of the labelling fan-out.</summary>
public sealed record LabelWindowRequest(string RunId, int WindowIndex, ArtifactRef Plan)
{
    /// <summary>
    /// <c>{runId}:label:{windowIndex}:{promptVersion}</c>. Stamped on the artifact, and checked
    /// before the window is labelled — which is what makes a re-delivered message free (plan N4).
    /// </summary>
    public required string IdempotencyKey { get; init; }
}

public sealed record LabelWindowComplete(string RunId, int WindowIndex, ArtifactRef Labels);

public sealed record LabelsMerged(string RunId, ArtifactRef Labels);

public sealed record AssembleComplete(string RunId, ArtifactRef Paragraphs, ArtifactRef Headings);

/// <summary>Phase 5's outcome; the composition family travels with it, re-confirmed from paragraphs (§6).</summary>
public sealed record PresentationComplete(
    string RunId, ArtifactRef Presentation, string CompositionFamily, int MetadataParagraphs);

public sealed record StructureComplete(string RunId, ArtifactRef Tree, string? ModelKey);

public sealed record ValidationComplete(string RunId, ArtifactRef Report, bool Passed);

/// <summary>Phase 8's outcome (scene plan §8.6).</summary>
public sealed record ItemsComplete(string RunId, ArtifactRef Items, int ItemCount, bool ValidationPassed);

/// <summary>Phase 9's outcome (scene plan §8.7).</summary>
public sealed record ReferentsComplete(
    string RunId, ArtifactRef Registries, int PersonaCount, bool ValidationPassed);

/// <summary>
/// Phase 10's outcome (scene plan §8.8). <see cref="SuppressedByFloor"/> is
/// <c>scene-cuts-suppressed-by-floor</c> — the entire tuning signal for <c>MinSceneSpan</c>, and
/// the measurement S4 sweeps against.
/// </summary>
public sealed record ScenesComplete(
    string RunId, ArtifactRef Scenes, int SceneCount, int SuppressedByFloor, bool ValidationPassed);

/// <summary>
/// Phase 11's outcome (scene plan §8.9). <see cref="ModelCalls"/> is zero on a document whose
/// deterministic pass found no candidates, which is most textbooks and most transcripts — and the
/// tripwire §13 watches.
/// </summary>
public sealed record LinksComplete(
    string RunId, ArtifactRef Links, int LinkCount, int ModelCalls, bool ValidationPassed);

public sealed record ReviewComplete(string RunId, ArtifactRef Review, bool RequiresHuman);

/// <summary>The human gate's request payload (plan §9.10).</summary>
public sealed record ReviewRequest(string RunId, string ReviewId, string TreeKey, string FindingsKey);

/// <summary>What <c>SubmitReviewDecision</c> posts back into the workflow.</summary>
public sealed record ReviewDecision(string ReviewId, ReviewDecisionKind Kind, string? Notes)
{
    public IReadOnlyList<ParagraphEdit> Edits { get; init; } = [];
}

public sealed record FrozenComplete(string RunId, ArtifactRef Frozen, string TreeHash);

public sealed record AugmentBatchRequest(string RunId, string Type, IReadOnlyList<string> ParagraphIds)
{
    public required string IdempotencyKey { get; init; }
}

public sealed record AugmentBatchComplete(string RunId, string Type, ArtifactRef Outputs);

public sealed record PublishComplete(string RunId, string TreeHash, int ParagraphCount);
