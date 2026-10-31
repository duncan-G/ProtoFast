using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ProtoFast.ProtoStrap;
using ProtoFast.Storage.Abstractions;

namespace ProtoFast.Storage;

public static class StorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers the S3 client, the object store and the presigned-URL factory.
    ///
    /// <para>Credentials are never configured here. In an account the api and the worker run with
    /// their component's role, which the SDK's default chain finds from ProtoStrap's agent; under
    /// <c>aspire run</c> the LocalStack endpoint is set and any credentials will do. A
    /// <c>ServiceUrl</c> set in an account would silently point the whole feature at a machine that
    /// is not AWS, so it comes only from AWS_ENDPOINT_URL_S3, which only the AppHost sets.</para>
    /// </summary>
    /// <param name="bucket">The spec's name for the bucket, whose name ProtoStrap gives as
    /// BUCKET_&lt;NAME&gt; when the options name none.</param>
    public static IServiceCollection AddS3ObjectStorage(
        this IServiceCollection services,
        Action<S3StorageOptions> configureOptions,
        string bucket = "documents")
    {
        services.Configure(configureOptions);
        services.PostConfigure<S3StorageOptions>(options =>
        {
            if (string.IsNullOrWhiteSpace(options.Bucket))
            {
                options.Bucket = Environment.GetEnvironmentVariable(Replica.Name("BUCKET_", bucket))
                    ?? throw new InvalidOperationException($"{Replica.Name("BUCKET_", bucket)} is not set, and S3:Bucket names no bucket");
            }

            options.ServiceUrl ??= Variable("AWS_ENDPOINT_URL_S3");
            if (options.ServiceUrl is not null)
            {
                options.AwsRegion ??= Variable("AWS_REGION");
            }
        });
        services.AddSingleton<IAmazonS3>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<S3StorageOptions>>();
            return CreateS3(options.Value.ServiceUrl, options.Value.AwsRegion);
        });

        // A presigned POST signs its policy directly rather than through the client's signer, so
        // the credentials have to be resolvable on their own. In an account this is the component's
        // role from ProtoStrap's agent, and the object refreshes itself, which is what keeps a
        // signed policy valid across a credential rotation.
        services.AddSingleton<AWSCredentials>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<S3StorageOptions>>();

            return string.IsNullOrWhiteSpace(options.Value.ServiceUrl)
                ? Amazon.Runtime.Credentials.DefaultAWSCredentialsIdentityResolver.GetCredentials(new AmazonS3Config())
                : new BasicAWSCredentials("localstack", "localstack");
        });

        services.AddSingleton<S3ObjectStore>();
        services.AddSingleton<IObjectStore>(sp => sp.GetRequiredService<S3ObjectStore>());
        services.AddSingleton<IPresignedUrlFactory>(sp => sp.GetRequiredService<S3ObjectStore>());

        return services;
    }

    internal static string? Variable(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;

    private static AmazonS3Client CreateS3(string? serviceUrl, string? region)
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
}
