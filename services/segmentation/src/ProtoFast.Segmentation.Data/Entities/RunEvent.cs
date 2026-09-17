using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.Data.Entities;

/// <summary>
/// An append-only phase transition. This table is the source of ThePlot's progress stream
/// (<c>WatchRun</c>), which is why it is append-only: a stream that could observe a row change
/// under it would have to re-read, and a user watching a run wants to see what happened, not the
/// latest summary.
/// </summary>
public sealed class RunEvent
{
    public long Id { get; set; }

    public required string RunId { get; set; }

    public PipelinePhase Phase { get; set; }

    public PhaseState State { get; set; }

    public string Message { get; set; } = string.Empty;

    public DateTimeOffset At { get; set; }

    public Run? Run { get; set; }
}
