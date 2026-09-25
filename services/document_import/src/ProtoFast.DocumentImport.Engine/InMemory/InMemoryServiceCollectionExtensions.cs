using Microsoft.Extensions.DependencyInjection;
using ProtoFast.DocumentImport.Engine.Discovery;
using ProtoFast.DocumentImport.Engine.Learning;
using ProtoFast.DocumentImport.Engine.Policy;
using ProtoFast.DocumentImport.Engine.Storage;

namespace ProtoFast.DocumentImport.Engine.InMemory;

public static class InMemoryServiceCollectionExtensions
{
    /// <summary>Stores and an outcome queue that live only as long as the process. For tests.</summary>
    public static IServiceCollection AddInMemoryWorkflowEngineStores(this IServiceCollection services)
    {
        services.AddSingleton<IArtifactStore, InMemoryArtifactStore>();
        services.AddSingleton<IRunLedger, InMemoryRunLedger>();
        services.AddSingleton<IRegistry, InMemoryRegistry>();
        services.AddSingleton<IPolicyStore, InMemoryPolicyStore>();
        services.AddSingleton<IDocumentFamilyPolicyStore, InMemoryDocumentFamilyPolicyStore>();
        services.AddSingleton<IDocumentFamilyCatalog, InMemoryDocumentFamilyCatalog>();
        services.AddSingleton<IMinedWorkflowStore, InMemoryMinedWorkflowStore>();

        services.AddSingleton<InMemoryOutcomeQueue>();
        services.AddSingleton<IOutcomeQueue>(sp => sp.GetRequiredService<InMemoryOutcomeQueue>());
        services.AddHostedService(sp => sp.GetRequiredService<InMemoryOutcomeQueue>());
        return services;
    }
}
