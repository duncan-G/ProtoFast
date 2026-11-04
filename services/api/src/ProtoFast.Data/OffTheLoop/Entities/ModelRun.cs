using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.OffTheLoop.Entities;

/// <summary>
/// Model work queued to run in the background as its owner: researching a company, reading a
/// posting, writing in a note. The worker claims rows across users, so it reads and writes them
/// over plain SQL; the owner's calls go through the user filter like every other table.
/// </summary>
public sealed class ModelRun : IDateStamped
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = "";

    public ModelRunKind Kind { get; set; }

    public Guid? CompanyId { get; set; }

    public Guid? ApplicationId { get; set; }

    public Guid? NoteId { get; set; }

    public required string Label { get; set; }

    /// <summary>The request, as a serialized <c>RunWork</c> protobuf message.</summary>
    public required byte[] Work { get; set; }

    public ModelRunStatus Status { get; set; }

    /// <summary>Steps and text so far, as a serialized <c>RunProgress</c> protobuf message.</summary>
    public byte[]? Progress { get; set; }

    /// <summary>What it found, as a serialized <c>RunResult</c> protobuf message.</summary>
    public byte[]? Result { get; set; }

    public string? Error { get; set; }

    /// <summary>How many times a worker has claimed it; a worker that died loses its claim when the lease runs out.</summary>
    public int Attempts { get; set; }

    public DateTime? LeaseUntil { get; set; }

    /// <summary>Bumped by every write after it is queued, so a watch can tell it changed without trusting clocks.</summary>
    public int Version { get; set; }

    /// <summary>The W3C traceparent of the call that queued it, so its run joins that trace.</summary>
    public string? TraceParent { get; set; }

    /// <summary>When a writing run's note was last saved as the run was queued.</summary>
    public DateTime? NoteModifiedAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public DateTime DateCreated { get; set; }

    public DateTime DateLastModified { get; set; }
}
