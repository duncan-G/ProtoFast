using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ProtoFast.DocumentImport.History.Corpus;
using ProtoFast.DocumentImport.History.Runs;

namespace ProtoFast.DocumentImport.History;

public static class HistoryServiceCollectionExtensions
{
    /// <summary>
    /// The corpus and the story-run ledger over the data source keyed <see cref="HistoryDatabase.ConnectionName"/>
    /// (<c>AddKeyedNpgsqlDataSource("history")</c>), with its tables created at startup.
    /// </summary>
    public static IServiceCollection AddHistoryCorpus(this IServiceCollection services)
    {
        services.AddSingleton(sp => new HistoryDatabase(sp.GetRequiredKeyedService<NpgsqlDataSource>(HistoryDatabase.ConnectionName)));
        services.AddSingleton<HistoryCorpus>();
        services.AddSingleton<StoryRunLedger>();
        services.AddHostedService<HistorySchemaInitializer>();
        return services;
    }
}
