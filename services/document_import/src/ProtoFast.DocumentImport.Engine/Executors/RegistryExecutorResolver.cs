using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using ProtoFast.DocumentImport.Engine.Discovery;
using ProtoFast.DocumentImport.Engine.Storage;

namespace ProtoFast.DocumentImport.Engine.Executors;

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
            // Resolved lazily: the stage agent's tools run attempts, which need this resolver.
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
