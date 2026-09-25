using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using ProtoFast.DocumentImport.Data.Postgres;
using ProtoFast.DocumentImport.Data.S3;
using ProtoFast.DocumentImport.Data.Sqs;
using ProtoFast.DocumentImport.Engine.Discovery;
using ProtoFast.DocumentImport.Engine.Learning;
using ProtoFast.DocumentImport.Engine.Policy;
using ProtoFast.DocumentImport.Engine.Storage;

namespace ProtoFast.DocumentImport.Data;

public static class DurableStoresServiceCollectionExtensions
{
    /// <summary>
    /// Postgres, S3 and SQS backing for every engine store and the outcome queue. Needs an
    /// <c>NpgsqlDataSource</c> (<c>AddNpgsqlDataSource("protofast")</c>), an <c>IObjectStore</c>
    /// (<c>AddS3ObjectStorage</c>) and a FIFO queue registered as
    /// <c>AddSqsQueue(SqsOutcomeQueue.QueueKey, …)</c>.
    /// </summary>
    public static IServiceCollection AddDurableWorkflowEngineStores(this IServiceCollection services)
    {
        services.AddDbContextFactory<WorkflowEngineDbContext>((sp, options) =>
            options.UseWorkflowEngineNpgsql(sp.GetRequiredService<NpgsqlDataSource>()));
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<IArtifactStore, S3ArtifactStore>();
        services.AddSingleton<IRunLedger, PostgresRunLedger>();
        services.AddSingleton<IRegistry, PostgresRegistry>();
        services.AddSingleton<IPolicyStore, PostgresPolicyStore>();
        services.AddSingleton<IDocumentFamilyPolicyStore, PostgresDocumentFamilyPolicyStore>();
        services.AddSingleton<IDocumentFamilyCatalog, PostgresDocumentFamilyCatalog>();
        services.AddSingleton<IMinedWorkflowStore, PostgresMinedWorkflowStore>();

        services.AddSingleton<IOutcomeQueue, SqsOutcomeQueue>();
        services.AddHostedService<OutcomeQueueConsumer>();
        return services;
    }
}
