using System.Collections.Concurrent;
using ProtoFast.DocumentImport.Engine;

namespace ProtoFast.DocumentImport.UnitTests.Engine;

internal sealed class ScriptedExecutorFactory : IExecutorFactory
{
    private readonly ConcurrentDictionary<ExecutorRef, (Tier Tier, Func<StageRequest, string> Produce)> _scripts = new();
    private IArtifactStore? _artifacts;

    public ConcurrentQueue<(ExecutorRef Executor, string StageId)> Calls { get; } = new();

    public void Script(ExecutorRef executor, Tier tier, Func<StageRequest, string> produce) =>
        _scripts[executor] = (tier, produce);

    public void Use(IArtifactStore artifacts) => _artifacts = artifacts;

    public bool CanBuild(ExecutorSpec spec) => _scripts.ContainsKey(spec.Ref);

    public Task<IExecutor> BuildAsync(ExecutorSpec spec, CancellationToken ct) =>
        Task.FromResult<IExecutor>(new ScriptedExecutor(spec.Ref, _scripts[spec.Ref], this));

    private sealed class ScriptedExecutor(
        ExecutorRef reference, (Tier Tier, Func<StageRequest, string> Produce) script, ScriptedExecutorFactory owner) : IExecutor
    {
        public Tier Tier => script.Tier;

        public async Task<StageResult> ExecuteAsync(StageRequest request, CancellationToken ct)
        {
            owner.Calls.Enqueue((reference, request.Stage.Id));
            var content = script.Produce(request);
            if (content == "hang")
            {
                await Task.Delay(Timeout.Infinite, ct);
            }

            if (content == "throw")
            {
                throw new InvalidOperationException("scripted fault");
            }

            var output = await owner._artifacts!.PutAsync(
                request.RunId, request.Stage.Id, EngineHarness.Utf8(content), request.Stage.Output, ct);
            return new StageResult(output, null, new Cost(1, TimeSpan.Zero), [new Decision("style", content, "scripted", 1)]);
        }
    }
}
