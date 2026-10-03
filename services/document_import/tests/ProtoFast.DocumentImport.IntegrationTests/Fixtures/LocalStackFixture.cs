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

    /// <summary>A standard queue, like the import queue; with a receive limit it dead-letters after that many receives.</summary>
    public async Task<string> CreateQueueAsync(TimeSpan visibilityTimeout, int? maxReceiveCount = null)
    {
        var queue = $"imports-{Guid.NewGuid():N}";
        using var sqs = new AmazonSQSClient(Credentials, new AmazonSQSConfig { ServiceURL = ServiceUrl, AuthenticationRegion = Region });
        var attributes = new Dictionary<string, string>
        {
            ["VisibilityTimeout"] = ((int)visibilityTimeout.TotalSeconds).ToString(),
        };

        if (maxReceiveCount is { } max)
        {
            var dlq = await sqs.CreateQueueAsync($"{queue}-dlq");
            var arn = (await sqs.GetQueueAttributesAsync(dlq.QueueUrl, [QueueAttributeName.QueueArn])).QueueARN;
            attributes["RedrivePolicy"] = $$"""{"deadLetterTargetArn":"{{arn}}","maxReceiveCount":{{max}}}""";
        }

        await sqs.CreateQueueAsync(new CreateQueueRequest { QueueName = queue, Attributes = attributes });
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

    public void AddQueue(IServiceCollection services, string key, string queue, TimeSpan visibilityTimeout) =>
        services.AddSqsQueue(key, o =>
        {
            o.QueueName = queue;
            o.ServiceUrl = ServiceUrl;
            o.AwsRegion = Region;
            o.WaitTime = TimeSpan.FromSeconds(1);
            o.VisibilityTimeout = visibilityTimeout;
        });
}
