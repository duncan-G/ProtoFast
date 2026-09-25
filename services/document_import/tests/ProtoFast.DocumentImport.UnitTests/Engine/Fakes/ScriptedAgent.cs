using ProtoFast.DocumentImport.Engine;

namespace ProtoFast.DocumentImport.UnitTests.Engine;

internal sealed class ScriptedAgent : IDiscoveryAgent
{
    public Func<ArtifactRef, IAgentTools, Task> Run { get; set; } = (_, _) => Task.CompletedTask;

    public Func<StageRequest, IAgentTools, Task> RunStage { get; set; } = async (request, tools) =>
        await tools.WriteArtifact(request.Stage.Id, EngineHarness.Utf8($"orchestrated {request.Stage.Id}"), request.Stage.Output);

    public Task RunAsync(ArtifactRef input, IAgentTools tools, TraceRef trace, CancellationToken ct) => Run(input, tools);

    public Task RunStageAsync(StageRequest request, IAgentTools tools, TraceRef trace, CancellationToken ct) =>
        RunStage(request, tools);
}
