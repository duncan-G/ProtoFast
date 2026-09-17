namespace ProtoFast.AppHost.LocalStack;

public static class LocalStackResourceBuilderExtensions
{
    /// <summary>The single gateway port LocalStack serves every emulated AWS API on.</summary>
    public const string GatewayEndpointName = "gateway";

    /// <summary>
    /// Adds LocalStack with S3 and SQS, and creates the segmentation bucket and the three queues
    /// on container start (plan §22.1).
    ///
    /// <para>The init script is what makes a fresh clone work: without it, <c>aspire run</c> would
    /// come up with a worker polling a queue that does not exist, and every developer would have
    /// to run the same four <c>awslocal</c> commands by hand before anything worked.</para>
    /// </summary>
    public static IResourceBuilder<ContainerResource> AddLocalStack(
        this IDistributedApplicationBuilder builder,
        string name,
        string bucketName) =>
        builder
            .AddContainer(name, "localstack/localstack", "4")
            .WithHttpEndpoint(targetPort: 4566, name: GatewayEndpointName)
            .WithEnvironment("SERVICES", "s3,sqs")
            .WithEnvironment("DEBUG", "0")
            .WithEnvironment("SEGMENTATION_BUCKET", bucketName)
            // ready.d runs once the emulated services are actually accepting calls, which
            // init-ready scripts elsewhere in the tree are not; anything earlier races the
            // services it is trying to configure.
            .WithBindMount(
                "../scripts/localstack-init.sh",
                "/etc/localstack/init/ready.d/segmentation.sh",
                isReadOnly: true)
            .WithHttpHealthCheck("/_localstack/health", statusCode: 200, endpointName: GatewayEndpointName);

    /// <summary>
    /// The queue URL a client inside the Aspire network should use. LocalStack derives queue URLs
    /// from the host it was called on, so a URL built against <c>localhost</c> is unusable from a
    /// container and vice versa — building it from the resolved endpoint avoids guessing.
    /// </summary>
    public static ReferenceExpression QueueUrl(
        this IResourceBuilder<ContainerResource> localstack, string queueName)
    {
        var gateway = localstack.GetEndpoint(GatewayEndpointName);

        return ReferenceExpression.Create(
            $"{gateway}/000000000000/{queueName}");
    }
}
