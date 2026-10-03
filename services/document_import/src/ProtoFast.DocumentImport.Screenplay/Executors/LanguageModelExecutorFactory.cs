using Microsoft.Extensions.Logging;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Screenplay.Models;

namespace ProtoFast.DocumentImport.Screenplay.Executors;

/// <summary>Builds the agent tiers; the engine handles Orchestrator and nothing here handles Codified.</summary>
public sealed class LanguageModelExecutorFactory(
    IRegistry registry,
    IArtifactStore artifacts,
    ILanguageModelFactory models,
    LanguageModelOptions options,
    TimeProvider time,
    ILogger<LanguageModelExecutor> logger) : IExecutorFactory
{
    public bool CanBuild(ExecutorSpec spec) =>
        spec.Tier is Tier.DelegateLarge or Tier.DelegateMedium or Tier.DelegateSmall
        && spec.ModelClass is not null
        && spec.Playbook is not null;

    public async Task<IExecutor> BuildAsync(ExecutorSpec spec, CancellationToken ct)
    {
        var playbook = await registry.ResolveAsync(spec.Playbook!.Value, ct);
        return new LanguageModelExecutor(spec, playbook, models.For(spec.ModelClass!), artifacts, options, time, logger);
    }
}
