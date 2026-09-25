using Amazon.Runtime;
using Amazon.S3;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.DependencyInjection;
using ProtoFast.Storage;
using Testcontainers.LocalStack;
using Xunit;

namespace ProtoFast.DocumentImport.IntegrationTests.Fixtures;

/// <summary>One disposable LocalStack for the assembly; each test class makes its own bucket or queue.</summary>
public sealed class LocalStackFixture : IAsyncLifetime
{
    public const string Region = "us-east-1";

    private static readonly BasicAWSCredentials Credentials = new("localstack", "localstack");

    private readonly LocalStackContainer _container = new LocalStackBuilder("localstack/localstack:4").Build();

    public string ServiceUrl => _container.GetConnectionString();

    public ValueTask InitializeAsync() => new(_container.StartAsync());

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    public async Task<string> CreateBucketAsync()
    {
        var bucket = $"engine-{Guid.NewGuid():N}";
        using var s3 = new AmazonS3Client(Credentials, new AmazonS3Config { ServiceURL = ServiceUrl, ForcePathStyle = true });
        await s3.PutBucketAsync(bucket);
        return bucket;
    }

    public async Task<string> CreateFifoQueueAsync(TimeSpan visibilityTimeout)
    {
        var queue = $"outcomes-{Guid.NewGuid():N}.fifo";
        using var sqs = new AmazonSQSClient(Credentials, new AmazonSQSConfig { ServiceURL = ServiceUrl, AuthenticationRegion = Region });
        await sqs.CreateQueueAsync(new CreateQueueRequest
        {
            QueueName = queue,
            Attributes = new Dictionary<string, string>
            {
                ["FifoQueue"] = "true",
                ["VisibilityTimeout"] = ((int)visibilityTimeout.TotalSeconds).ToString(),
            },
        });
        return queue;
    }

    public void AddObjectStorage(IServiceCollection services, string bucket) =>
        services.AddS3ObjectStorage(o =>
        {
            o.Bucket = bucket;
            o.ServiceUrl = ServiceUrl;
            o.AwsRegion = Region;
            o.ObjectLockEnabled = false;
        });

    public void AddQueue(IServiceCollection services, string key, string queue) =>
        services.AddSqsQueue(key, o =>
        {
            o.QueueName = queue;
            o.ServiceUrl = ServiceUrl;
            o.AwsRegion = Region;
            o.WaitTime = TimeSpan.FromSeconds(1);
        });
}
