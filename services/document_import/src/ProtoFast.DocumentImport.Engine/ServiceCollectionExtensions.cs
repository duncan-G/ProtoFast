using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ProtoFast.DocumentImport.Engine.InMemory;

namespace ProtoFast.DocumentImport.Engine;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the engine with in-memory data-plane stores. Durable stores registered before
    /// this call win. The caller supplies what the engine cannot own: an <see cref="IClassifier"/>,
    /// an <see cref="IDiscoveryAgent"/>, <see cref="IExecutorFactory"/>s for the agent and Codified
    /// tiers, and optionally an <see cref="IRubricVerifierFactory"/>, <see cref="IVerifier"/>s and an
    /// <see cref="IDistiller"/>.
    /// </summary>
    public static IServiceCollection AddAgentWorkflowEngine(
        this IServiceCollection services, Action<EngineOptions>? configure = null)
    {
        var options = new EngineOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        services.TryAddSingleton(TimeProvider.System);

        // Data plane.
        services.TryAddSingleton<IArtifactStore, InMemoryArtifactStore>();
        services.TryAddSingleton<IRunLedger, InMemoryRunLedger>();
        services.TryAddSingleton<IPolicyStore, InMemoryPolicyStore>();
        services.TryAddSingleton<IBucketPolicyStore, InMemoryBucketPolicyStore>();
        services.TryAddSingleton<IRegistry, InMemoryRegistry>();
        services.TryAddSingleton<IBucketCatalog, InMemoryBucketCatalog>();
        services.TryAddSingleton<IMinedWorkflowStore, InMemoryMinedWorkflowStore>();

        // Control plane.
        services.TryAddSingleton<VerifierCatalog>();
        services.TryAddSingleton<VerifierRunner>();
        services.TryAddSingleton<IExecutorResolver, RegistryExecutorResolver>();
        services.TryAddSingleton<StageAttempts>();
        services.TryAddSingleton<PolicyGate>();
        services.TryAddSingleton<IShadowSampler, RandomShadowSampler>();
        services.TryAddSingleton<Scheduler>();
        services.TryAddSingleton<IScheduler>(sp => sp.GetRequiredService<Scheduler>());
        services.TryAddSingleton<AgentToolsFactory>();
        services.TryAddSingleton<IWorkflowMiner, WorkflowMiner>();
        services.TryAddSingleton<WorkflowPromotion>();
        services.TryAddSingleton<RunDispatcher>();

        // Learning plane.
        services.TryAddSingleton<IDistiller, NullDistiller>();
        services.TryAddSingleton<IPolicyUpdater, PolicyUpdater>();
        services.TryAddSingleton<PartitionedOutcomeBus>();
        services.TryAddSingleton<IOutcomeBus>(sp => sp.GetRequiredService<PartitionedOutcomeBus>());
        services.AddHostedService(sp => sp.GetRequiredService<PartitionedOutcomeBus>());

        return services;
    }
}
