using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProtoFast.DocumentImport.Engine.Discovery;
using ProtoFast.DocumentImport.Engine.Learning;
using ProtoFast.DocumentImport.Engine.Policy;
using ProtoFast.DocumentImport.Engine.Storage;

namespace ProtoFast.DocumentImport.Engine;

/// <summary>Fails startup when a store is missing, rather than at the first run that needs it.</summary>
public sealed class WorkflowEngineStartupCheck(IServiceProviderIsService services) : IHostedService
{
    public static readonly IReadOnlyList<Type> RequiredServices =
    [
        typeof(IArtifactStore),
        typeof(IRunLedger),
        typeof(IRegistry),
        typeof(IPolicyStore),
        typeof(IDocumentFamilyPolicyStore),
        typeof(IDocumentFamilyCatalog),
        typeof(IMinedWorkflowStore),
        typeof(IOutcomeQueue),
    ];

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var missing = RequiredServices.Where(t => !services.IsService(t)).Select(t => t.Name).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"The workflow engine has no {string.Join(", ", missing)}. Call AddDurableWorkflowEngineStores, " +
                "or AddInMemoryWorkflowEngineStores for tests.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
