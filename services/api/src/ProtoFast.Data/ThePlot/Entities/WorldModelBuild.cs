using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// One request to build a story's location world model, and what became of it: the ledger the
/// writer's progress and the console's view are both read from. <see cref="Cost"/> is US dollars
/// across every delivery of the request.
/// </summary>
public sealed class WorldModelBuild : IDateStamped
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = "";

    public Guid StoryId { get; set; }

    public Story Story { get; set; } = null!;

    public WorldModelBuildStatus Status { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public int ScenesTotal { get; set; }

    public int ScenesDone { get; set; }

    public decimal Cost { get; set; }

    /// <summary>What the writer is told, while it runs or once it stopped.</summary>
    public string? Message { get; set; }

    /// <summary>Why it failed, for operators.</summary>
    public string? Failure { get; set; }

    /// <summary>How many times the queue handed the request to a worker.</summary>
    public int Deliveries { get; set; }

    public string? TraceId { get; set; }

    public int PartCount { get; set; }

    public int ObjectCount { get; set; }

    public int ObservationCount { get; set; }

    public List<WorldModelBuildStep> Steps { get; set; } = [];

    public DateTime DateCreated { get; set; }

    public DateTime DateLastModified { get; set; }
}
