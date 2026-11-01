using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProtoFast.ProtoStrap;
using ProtoFast.Storage.Abstractions;

namespace ProtoFast.Storage;

public static class SqsServiceCollectionExtensions
{
    /// <summary>
    /// Registers an <see cref="IMessageQueue"/> keyed by <paramref name="name"/>. As with S3,
    /// credentials come from the SDK's default chain unless a LocalStack <c>ServiceUrl</c> is set.
    /// <paramref name="queue"/> is the spec's name for the queue, whose URL ProtoStrap gives as
    /// QUEUE_&lt;NAME&gt;_URL when the options name no queue.
    /// </summary>
    public static IServiceCollection AddSqsQueue(
        this IServiceCollection services, string name, Action<SqsQueueOptions> configureOptions, string? queue = null)
    {
        services.Configure(name, configureOptions);
        services.PostConfigure<SqsQueueOptions>(name, options =>
        {
            if (string.IsNullOrWhiteSpace(options.QueueUrl) && string.IsNullOrWhiteSpace(options.QueueName) && queue is not null)
            {
                options.QueueUrl = StorageServiceCollectionExtensions.Variable(Replica.Name("QUEUE_", queue) + "_URL")
                    ?? throw new InvalidOperationException($"{Replica.Name("QUEUE_", queue)}_URL is not set, and the options name no queue");
            }

            options.ServiceUrl ??= StorageServiceCollectionExtensions.Variable("AWS_ENDPOINT_URL_SQS");
            if (options.ServiceUrl is not null)
            {
                options.AwsRegion ??= StorageServiceCollectionExtensions.Variable("AWS_REGION");
            }
        });
        services.AddKeyedSingleton<IMessageQueue>(name, (sp, _) => new SqsMessageQueue(
            sp.GetRequiredService<IOptionsMonitor<SqsQueueOptions>>().Get(name),
            sp.GetRequiredService<ILogger<SqsMessageQueue>>()));
        return services;
    }
}
