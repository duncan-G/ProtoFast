using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ProtoFast.DocumentImport.Worker.WorldModel;

public static class WorldModelServiceCollectionExtensions
{
    /// <summary>
    /// The world-model build: its queue consumer, the runner and the pieces it reads and writes
    /// with. Pair with <c>AddThePlotData</c> for the tables and <c>AddScreenplayDiscovery</c> for
    /// the models.
    /// </summary>
    public static IServiceCollection AddWorldModelBuilding(this IServiceCollection services, Action<WorldModelBuildOptions>? configure = null)
    {
        services.Configure<WorldModelBuildOptions>(options => configure?.Invoke(options));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<WorldModelLedger>();
        services.AddSingleton<SceneReader>();
        services.AddSingleton<LocationDescriber>();
        services.AddScoped<StoryLoader>();
        services.AddScoped<ObservationApplier>();
        services.AddScoped<WorldModelBuildRunner>();
        services.AddHostedService<WorldModelBuildConsumer>();
        return services;
    }
}
