namespace ProtoFast.AppHost.LocalStack;

public static class LocalStackResourceBuilderExtensions
{
    private const string AwsRegion = "AWS_REGION";

    public static IResourceBuilder<LocalStackResource> AddLocalStack(
        this IDistributedApplicationBuilder builder,
        string name)
    {
        var awsRegion = builder.Configuration[AwsRegion];
        if (string.IsNullOrEmpty(awsRegion))
        {
            throw new InvalidOperationException("AWS region not configured");
        }

        var resource =  new LocalStackResource(name);

        var localstack = builder
            .AddResource(resource)
            .WithImage("localstack/localstack", "4")
            .WithHttpEndpoint(
                port: LocalStackResource.GatewayPort, targetPort: LocalStackResource.GatewayPort, name: LocalStackResource.GatewayEndpointName, isProxied: false)
            .WithEnvironment("SERVICES", "s3,sqs")
            .WithEnvironment("DEBUG", "0")
            .WithEnvironment("USE_SSL", "1")
            .WithEnvironment("LOCALSTACK_HOST", LocalStackResource.GatewayHostAndPort)
            .WithEnvironment("AWS_DEFAULT_REGION", awsRegion)
            .WithBindMount(
                "../scripts/localstack-init.sh",
                "/etc/localstack/init/ready.d/init.sh",
                isReadOnly: true)
            .WithHttpHealthCheck("/_localstack/health", statusCode: 200, endpointName: LocalStackResource.GatewayEndpointName);

        if (!builder.ExecutionContext.IsPublishMode)
        {
            // LocalStack's community edition keeps every bucket and queue in memory, and reloading them on
            // start is a Pro feature. So container needs survive — Persistent makes Aspire reuse the running
            // one instead of tearing it down at Ctrl+C and creating a fresh, empty one on the next `aspire run`.
            localstack.WithLifetime(ContainerLifetime.Persistent);
        }

        return localstack;
    }

    public static IResourceBuilder<ContainerResource> WithClientOrigins(
        this IResourceBuilder<ContainerResource> localstack,
        IReadOnlyList<string> origins)
    {
        if (origins.Count == 0)
        {
            return localstack;
        }

        var list = string.Join(',', origins);

        return localstack.WithEnvironment("EXTRA_CORS_ALLOWED_ORIGINS", list);
    }

    public static IResourceBuilder<ContainerResource> WithBuckets(
        this IResourceBuilder<ContainerResource> localstack,
        IReadOnlyList<string> buckets)
    {
        if (buckets.Count == 0)
        {
            return localstack;
        }

        var list = string.Join(',', buckets);

        return localstack.WithEnvironment("BUCKET_NAMES", list);
    }

    public static IResourceBuilder<ContainerResource> WithQueues(
        this IResourceBuilder<ContainerResource> localstack,
        IReadOnlyList<string> queues)
    {
        if (queues.Count == 0)
        {
            return localstack;
        }

        var list = string.Join(',', queues);

        return localstack.WithEnvironment("QUEUE_NAMES", list);
    }

    /// <summary>
    /// The queue URL a client should use, built from <see cref="GatewayUrl"/> so it agrees with
    /// every other consumer of the gateway rather than being derived separately.
    /// </summary>
    public static string QueueUrl(
        this IResourceBuilder<ContainerResource> _, string queueName) =>
        $"{LocalStackResource.GatewayUrl}/000000000000/{queueName}";
}
