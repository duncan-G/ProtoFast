using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace ProtoFast.DocumentImport.Engine;

/// <summary>
/// Builds a runnable executor from its spec. Agent tiers need a model client and Codified needs an
/// assembly loader in a sandbox; both live outside the engine and plug in here.
/// </summary>
public interface IExecutorFactory
{
    bool CanBuild(ExecutorSpec spec);
    Task<IExecutor> BuildAsync(ExecutorSpec spec, CancellationToken ct);
}

/// <summary>
/// Resolves refs through the registry and the registered <see cref="IExecutorFactory"/>s. Refs are
/// immutable, so a resolved executor is cached for the process lifetime. The configured
/// Orchestrator ref always resolves to the <see cref="StageAgentExecutor"/>.
/// </summary>
public sealed class RegistryExecutorResolver(
    IRegistry registry,
    IEnumerable<IExecutorFactory> factories,
    IServiceProvider services,
    EngineOptions options) : IExecutorResolver
{
    private readonly ConcurrentDictionary<ExecutorRef, IExecutor> _cache = new();

    public async Task<IExecutor> ResolveAsync(ExecutorRef reference, CancellationToken ct)
    {
        if (_cache.TryGetValue(reference, out var cached))
        {
            return cached;
        }

        IExecutor executor;
        if (reference == options.Orchestrator)
        {
            // Resolved lazily: the stage agent's tools run attempts, which resolve executors.
            executor = new StageAgentExecutor(
                services.GetRequiredService<IDiscoveryAgent>(),
                services.GetRequiredService<AgentToolsFactory>(),
                services.GetRequiredService<TimeProvider>());
        }
        else
        {
            var spec = await registry.ResolveAsync(reference, ct);
            var factory = factories.FirstOrDefault(f => f.CanBuild(spec))
                ?? throw new InvalidOperationException($"No executor factory can build {reference} ({spec.Tier}).");
            executor = await factory.BuildAsync(spec, ct);
            if (executor.Tier != spec.Tier)
            {
                throw new InvalidOperationException($"{reference} is specified at {spec.Tier} but built at {executor.Tier}.");
            }
        }

        return _cache.GetOrAdd(reference, executor);
    }
}
