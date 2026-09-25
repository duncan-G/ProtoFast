using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtoFast.DocumentImport.Engine.Discovery;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Learning;
using ProtoFast.DocumentImport.Engine.Policy;
using ProtoFast.DocumentImport.Engine.Scheduling;
using ProtoFast.DocumentImport.Engine.Verification;
using ProtoFast.DocumentImport.Engine.Workflows;

namespace ProtoFast.DocumentImport.Engine;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the engine's logic but no stores or outcome queue: add those with
    /// <c>AddDurableWorkflowEngineStores</c>, or <c>AddInMemoryWorkflowEngineStores</c> for tests.
    /// Callers must also register an <see cref="IClassifier"/>, an <see cref="IDiscoveryAgent"/>
    /// and <see cref="IExecutorFactory"/>s.
    /// </summary>
    public static IServiceCollection AddAgentWorkflowEngine(
        this IServiceCollection services, Action<EngineOptions>? configure = null)
    {
        var options = new EngineOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        services.TryAddSingleton(TimeProvider.System);
        services.AddHostedService<WorkflowEngineStartupCheck>();

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

        services.TryAddSingleton<IDistiller, NullDistiller>();
        services.TryAddSingleton<IPolicyUpdater, PolicyUpdater>();

        return services;
    }
}
