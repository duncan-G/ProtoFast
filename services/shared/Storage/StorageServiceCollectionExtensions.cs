using System.Text.Json;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ProtoFast.Storage.Abstractions;

namespace ProtoFast.Storage;

public static class StorageServiceCollectionExtensions
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
    public static IServiceCollection AddS3ObjectStorage(
        this IServiceCollection services,
        Action<S3StorageOptions> configureOptions )
    {
        services.Configure(configureOptions);
        services.AddSingleton<IAmazonS3>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<S3StorageOptions>>();
            return CreateS3(options.Value.ServiceUrl, options.Value.AwsRegion);
        });

        // A presigned POST signs its policy directly rather than through the client's signer, so
        // the credentials have to be resolvable on their own. In production this is the instance
        // role over IMDS and the object refreshes itself, which is what keeps a signed policy
        // valid across a credential rotation.
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
}
