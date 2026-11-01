using ProtoFast.DocumentImport.Engine.Executors;

namespace ProtoFast.DocumentImport.Worker.WorldModel;

public sealed class WorldModelBuildOptions
{
    /// <summary>A build is a model call per scene, so one worker runs few at once.</summary>
    public int MaxConcurrentBuilds { get; set; } = 1;

    /// <summary>Scenes that share no location or character are read side by side, up to this many at once.</summary>
    public int LocationConcurrency { get; set; } = 3;

    /// <summary>How often a running build checks whether its owner cancelled it.</summary>
    public TimeSpan CancellationPollInterval { get; set; } = TimeSpan.FromSeconds(5);

    public string ModelClass { get; set; } = ModelClasses.Medium;

    /// <summary>A provider to pin the reads to; null for the configured default and its fallback.</summary>
    public string? Provider { get; set; }
}
