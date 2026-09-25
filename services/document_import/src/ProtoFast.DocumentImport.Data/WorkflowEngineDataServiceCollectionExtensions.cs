using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using ProtoFast.DocumentImport.Engine.Discovery;
using ProtoFast.DocumentImport.Engine.Storage;

namespace ProtoFast.DocumentImport.Data;

public static class WorkflowEngineDataServiceCollectionExtensions
{
    /// <summary>
    /// Replaces the engine's in-memory registry and document family catalog. Call before
    /// <c>AddAgentWorkflowEngine</c>. Needs an <c>NpgsqlDataSource</c> (from
    /// <c>AddNpgsqlDataSource("protofast")</c>) and an <c>IObjectStore</c>.
    /// </summary>
    public static IServiceCollection AddWorkflowEngineData(this IServiceCollection services)
    {
        services.AddDbContextFactory<WorkflowEngineDbContext>((sp, options) =>
            options.UseWorkflowEngineNpgsql(sp.GetRequiredService<NpgsqlDataSource>()));

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IRegistry, PostgresRegistry>();
        services.AddSingleton<IDocumentFamilyCatalog, PostgresDocumentFamilyCatalog>();
        return services;
    }
}
