using ProtoFast.DocumentImport.History;

namespace ProtoFast.DocumentImport.Worker.HistoricalStories;

public static class HistoricalStoryServiceCollectionExtensions
{
    /// <summary>
    /// Stories written from the history corpus: the run consumer, research desk and screenwriter.
    /// Needs the keyed "history" data source, <c>AddThePlotData</c> for the stories and
    /// <c>AddScreenplayDiscovery</c> for the models and mention tagger.
    /// </summary>
    public static IServiceCollection AddHistoricalStories(this IServiceCollection services, Action<HistoricalStoryOptions>? configure = null)
    {
        services.Configure<HistoricalStoryOptions>(options => configure?.Invoke(options));
        services.AddHistoryCorpus();
        services.AddScoped<HistoryResearcher>();
        services.AddScoped<HistoricalScreenwriter>();
        services.AddScoped<HistoricalStoryRunner>();
        services.AddHostedService<HistoricalStoryConsumer>();
        return services;
    }
}
