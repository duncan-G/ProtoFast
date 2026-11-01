namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// One step of a build: the seed, one scene read by the model, or the finish. Owned by its
/// <see cref="WorldModelBuild"/>. A scene read keeps the prompt it sent and the reply it got, so
/// an operator can see exactly what the model was asked and answered.
/// </summary>
public sealed class WorldModelBuildStep
{
    public Guid Id { get; set; }

    public Guid BuildId { get; set; }

    public WorldModelBuild Build { get; set; } = null!;

    public int Sequence { get; set; }

    public WorldModelStepKind Kind { get; set; }

    public Guid? SceneId { get; set; }

    public Scene? Scene { get; set; }

    /// <summary>The scene's title, or what the step did.</summary>
    public required string Title { get; set; }

    public WorldModelStepStatus Status { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public string? ModelId { get; set; }

    public long InputTokens { get; set; }

    public long OutputTokens { get; set; }

    public decimal Cost { get; set; }

    public string? Prompt { get; set; }

    public string? Reply { get; set; }

    /// <summary>What the step changed, in a line.</summary>
    public string? Summary { get; set; }

    public string? Failure { get; set; }
}
