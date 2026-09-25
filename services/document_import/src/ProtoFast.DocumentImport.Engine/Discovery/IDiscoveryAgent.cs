namespace ProtoFast.DocumentImport.Engine;

/// <summary>The engine mints <c>trace</c> up front because every <c>WriteArtifact</c> must carry one.</summary>
public interface IDiscoveryAgent
{
    Task RunAsync(ArtifactRef input, IAgentTools tools, TraceRef trace, CancellationToken ct);

    /// <summary>Scoped to one stage; its output is whatever it last wrote.</summary>
    Task RunStageAsync(StageRequest request, IAgentTools tools, TraceRef trace, CancellationToken ct);
}
