using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProtoFast.Storage.Abstractions;

namespace ProtoFast.Storage;

public static class SqsServiceCollectionExtensions
{
    /// <summary>
    /// Registers an <see cref="IMessageQueue"/> keyed by <paramref name="name"/>. As with S3,
    /// credentials come from the SDK's default chain unless a LocalStack <c>ServiceUrl</c> is set.
    /// </summary>
    public static IServiceCollection AddSqsQueue(
        this IServiceCollection services, string name, Action<SqsQueueOptions> configureOptions)
    {
        services.Configure(name, configureOptions);
        services.AddKeyedSingleton<IMessageQueue>(name, (sp, _) => new SqsMessageQueue(
            sp.GetRequiredService<IOptionsMonitor<SqsQueueOptions>>().Get(name),
            sp.GetRequiredService<ILogger<SqsMessageQueue>>()));
        return services;
    }
}
