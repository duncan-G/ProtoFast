using ProtoFast.DocumentImport.Core;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Storage;

namespace ProtoFast.DocumentImport.Engine.Discovery;

public sealed class StageAgentExecutor(IDiscoveryAgent agent, AgentToolsFactory tools, TimeProvider time) : IExecutor
{
    public Tier Tier => Tier.Orchestrator;

    public async Task<StageResult> ExecuteAsync(StageRequest request, CancellationToken ct)
    {
        var trace = new TraceRef($"{request.RunId}/{request.Stage.Id}/{DocumentImportIds.New()}");
        var scoped = tools.ForStage(request, trace, ct);
        var started = time.GetTimestamp();

        await agent.RunStageAsync(request, scoped, trace, ct);

        return new StageResult(
            scoped.ScopedOutput ?? ArtifactRef.None,
            trace,
            new Cost(0, time.GetElapsedTime(started)),
            scoped.ScopedDecisions);
    }
}
