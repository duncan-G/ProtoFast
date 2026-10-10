using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// Director work queued to run in the background: Direction requests run in order as their owner.
/// The worker claims rows across users, so it reads and writes them over plain SQL; the author's
/// calls go through the user filter like every other table.
/// </summary>
public sealed class DirectorJob : IDateStamped
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = "";

    public Guid StoryId { get; set; }

    public Story Story { get; set; } = null!;

    public Guid? SceneId { get; set; }

    /// <summary>Jobs that share it run one at a time: <c>scene:{id}</c>, or <c>library:{storyId}</c>.</summary>
    public required string Lane { get; set; }

    public required string Label { get; set; }

    /// <summary>The steps, as a serialized <c>JobSteps</c> protobuf message.</summary>
    public required byte[] Steps { get; set; }

    public int StepCount { get; set; }

    public int StepsDone { get; set; }

    public bool FollowVariations { get; set; }

    public DirectorJobStatus Status { get; set; }

    public string? Error { get; set; }

    /// <summary>The finished steps' replies, as a serialized <c>JobResults</c> protobuf message.</summary>
    public byte[]? Results { get; set; }

    /// <summary>How many times a worker has claimed it; a worker that died loses its claim when the lease runs out.</summary>
    public int Attempts { get; set; }

    public DateTime? LeaseUntil { get; set; }

    /// <summary>The W3C traceparent of the call that queued it, so its run joins that trace.</summary>
    public string? TraceParent { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public DateTime DateCreated { get; set; }

    public DateTime DateLastModified { get; set; }
}
