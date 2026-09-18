using Amazon.Runtime;
using Amazon.S3;
using Amazon.SQS;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace ProtoFast.Segmentation.Storage;

public static class SegmentationStorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers the S3 and SQS clients, the artifact store and the checkpoint store.
    ///
    /// <para>Credentials are never configured here. On Host B both <c>api</c> and the worker run
    /// under the instance profile and the SDK's default chain finds it over IMDS; in dev the
    /// LocalStack endpoint is set and any credentials will do. A <c>ServiceUrl</c> that is set in
    /// production would silently point the whole feature at a machine that is not AWS, so it is
    /// left unset there rather than defaulted (plan §20.1).</para>
    /// </summary>
    public static IServiceCollection AddSegmentationStorage(
        this IServiceCollection services,
        IConfiguration configuration,
        string storageSection = StorageOptions.SectionName,
        string queueSection = QueueOptions.SectionName)
    {
        services.Configure<StorageOptions>(configuration.GetSection(storageSection));
        services.Configure<QueueOptions>(configuration.GetSection(queueSection));

        var serviceUrl = configuration[$"{storageSection}:ServiceUrl"]
            ?? configuration["Aws:ServiceUrl"];

        // Only meaningful alongside a ServiceUrl: it has to name the region LocalStack's init
        // script created the bucket and queues in, which the AppHost supplies to both sides.
        var region = configuration[$"{storageSection}:Region"] ?? "us-west-2";

        services.AddSingleton<IAmazonS3>(_ => CreateS3(serviceUrl, region));
        services.AddSingleton<IAmazonSQS>(_ => CreateSqs(serviceUrl, region));

        // A presigned POST signs its policy directly rather than through the client's signer, so
        // the credentials have to be resolvable on their own. In production this is the instance
        // role over IMDS and the object refreshes itself, which is what keeps a signed policy
        // valid across a credential rotation.
        services.AddSingleton<AWSCredentials>(_ => string.IsNullOrWhiteSpace(serviceUrl)
            ? Amazon.Runtime.Credentials.DefaultAWSCredentialsIdentityResolver.GetCredentials(new AmazonS3Config())
            : new BasicAWSCredentials("localstack", "localstack"));

        services.AddSingleton<S3ArtifactStore>();
        services.AddSingleton<IArtifactStore>(sp => sp.GetRequiredService<S3ArtifactStore>());
        services.AddSingleton<IPresignedUrlFactory>(sp => sp.GetRequiredService<S3ArtifactStore>());
        services.AddSingleton<IRunQueue, SqsRunQueue>();
        services.AddSingleton<ICheckpointStore<JsonElement>, S3CheckpointStore>();

        return services;
    }

    private static AmazonS3Client CreateS3(string? serviceUrl, string region)
    {
        if (string.IsNullOrWhiteSpace(serviceUrl))
        {
            return new AmazonS3Client();
        }

        return new AmazonS3Client(
            new BasicAWSCredentials("localstack", "localstack"),
            new AmazonS3Config
            {
                ServiceURL = serviceUrl,
                // LocalStack serves one host for every bucket, so virtual-host addressing
                // (bucket.localhost) does not resolve.
                ForcePathStyle = true,
                AuthenticationRegion = region,
            });
    }

    private static AmazonSQSClient CreateSqs(string? serviceUrl, string region)
    {
        if (string.IsNullOrWhiteSpace(serviceUrl))
        {
            return new AmazonSQSClient();
        }

        return new AmazonSQSClient(
            new BasicAWSCredentials("localstack", "localstack"),
            new AmazonSQSConfig
            {
                ServiceURL = serviceUrl,
                AuthenticationRegion = region,
            });
    }
}
