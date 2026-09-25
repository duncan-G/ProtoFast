using ProtoFast.DocumentImport.Core;

namespace ProtoFast.DocumentImport.Engine;

/// <summary>
/// The seed <see cref="Tier.Orchestrator"/> executor on every ladder: the discovery agent loop
/// scoped to one stage. Its output is the last artifact the loop wrote (or delegated and saw pass),
/// and its decisions are the ones the loop recorded.
/// </summary>
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
