using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.Data.Entities;

/// <summary>
/// One phase of one run. <see cref="IdempotencyKey"/> is what makes re-running a phase free: the
/// artifact store checks it before doing any work, so a re-delivered SQS message costs nothing
/// (plan N4).
/// </summary>
public sealed class RunPhase
{
    public long Id { get; set; }

    public required string RunId { get; set; }

    public PipelinePhase Phase { get; set; }

    public PhaseState State { get; set; } = PhaseState.Pending;

    /// <summary>S3 key of this phase's output artifact, relative to the bucket.</summary>
    public string? ArtifactKey { get; set; }

    public string? IdempotencyKey { get; set; }

    public string? Error { get; set; }

    /// <summary>
    /// Repair rounds spent on this phase. It lives here rather than in worker memory so the limit
    /// survives a resume — a crash-loop must not get a fresh budget of provider calls (plan §13.2).
    /// </summary>
    public int RepairRounds { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public Run? Run { get; set; }
}
