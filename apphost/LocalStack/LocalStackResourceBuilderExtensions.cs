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

        var resource = new LocalStackResource(name);

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

    public static IResourceBuilder<LocalStackResource> WithClientOrigins(
        this IResourceBuilder<LocalStackResource> localstack,
        IReadOnlyList<string> origins)
    {
        if (origins.Count == 0)
        {
            return localstack;
        }

        return localstack.WithEnvironment("EXTRA_CORS_ALLOWED_ORIGINS", string.Join(',', origins));
    }

    public static IResourceBuilder<LocalStackResource> WithBuckets(
        this IResourceBuilder<LocalStackResource> localstack,
        IReadOnlyList<string> buckets)
    {
        if (buckets.Count == 0)
        {
            return localstack;
        }

        return localstack.WithEnvironment("BUCKET_NAMES", string.Join(',', buckets));
    }

    public static IResourceBuilder<LocalStackResource> WithQueues(
        this IResourceBuilder<LocalStackResource> localstack,
        IReadOnlyList<string> queues)
    {
        if (queues.Count == 0)
        {
            return localstack;
        }

        return localstack.WithEnvironment("QUEUE_NAMES", string.Join(',', queues));
    }

    /// <summary>
    /// Points a service's <c>S3</c> options at the LocalStack gateway: endpoint, bucket, region,
    /// and object lock off, because LocalStack's community edition does not implement it. The
    /// <paramref name="envPrefix"/> is the service's own env-var prefix ("Api_", …).
    ///
    /// <para>In publish mode this is a no-op: production resolves the real S3 endpoint from the
    /// region and the instance role, and deliberately never carries a ServiceUrl.</para>
    /// </summary>
    public static IResourceBuilder<ProjectResource> WithLocalStackS3(
        this IResourceBuilder<ProjectResource> project,
        IResourceBuilder<LocalStackResource> localstack,
        string envPrefix,
        string bucket)
    {
        if (project.ApplicationBuilder.ExecutionContext.IsPublishMode)
        {
            return project;
        }

        var awsRegion = project.ApplicationBuilder.Configuration[AwsRegion]
            ?? throw new InvalidOperationException("AWS region not configured");

        return project
            .WaitFor(localstack)
            .WithEnvironment($"{envPrefix}S3__ServiceUrl", LocalStackResource.GatewayUrl)
            .WithEnvironment($"{envPrefix}S3__Bucket", bucket)
            .WithEnvironment($"{envPrefix}S3__AwsRegion", awsRegion)
            .WithEnvironment($"{envPrefix}S3__ObjectLockEnabled", "false");
    }
}
