using ProtoFast.AppHost.LocalStack;
using ProtoFast.AppHost.OpenTelemetryCollector;

namespace ProtoFast.AppHost.Conversion;

/// <summary>
/// The document-conversion service, built from its Dockerfile so Tesseract, Ghostscript and
/// ExifTool never have to be installed locally.
/// </summary>
public static class ConversionResourceBuilderExtensions
{
    // Matches the compose service in deploy/.
    private const int Port = 8090;

    private const string ContextPath = "../services/conversion";

    public static IResourceBuilder<ContainerResource> AddConversionService(
        this IDistributedApplicationBuilder builder,
        string name,
        string bucket)
    {
        var awsRegion = builder.Configuration["AWS_REGION"]
            ?? throw new InvalidOperationException("AWS region not configured");

        return builder
            .AddDockerfile(name, ContextPath)
            .WithHttpEndpoint(targetPort: Port, env: "CONVERSION_PORT")
            .WithEnvironment("CONVERSION_BUCKET", bucket)
            .WithEnvironment("AWS_REGION", awsRegion)
            .WithEnvironment("AWS_DEFAULT_REGION", awsRegion)
            .WithHttpHealthCheck("/health");
    }

    /// <summary>
    /// Points the container at LocalStack by container DNS; <see cref="LocalStackResource.GatewayUrl"/>
    /// resolves to 127.0.0.1, which inside this container is the container itself.
    /// </summary>
    public static IResourceBuilder<ContainerResource> WithLocalStack(
        this IResourceBuilder<ContainerResource> conversion,
        IResourceBuilder<LocalStackResource> localstack)
    {
        var gateway = localstack.GetEndpoint(
            LocalStackResource.GatewayEndpointName,
            KnownNetworkIdentifiers.DefaultAspireContainerNetwork);

        return conversion
            .WithEnvironment(
                "CONVERSION_S3_ENDPOINT",
                ReferenceExpression.Create(
                    $"http://{gateway.Property(EndpointProperty.Host)}:{gateway.Property(EndpointProperty.Port)}"))
            // LocalStack's throwaway pair; production uses the instance role.
            .WithEnvironment("AWS_ACCESS_KEY_ID", "localstack")
            .WithEnvironment("AWS_SECRET_ACCESS_KEY", "localstack")
            .WaitFor(localstack);
    }

    public static IResourceBuilder<ContainerResource> WithOtelCollector(
        this IResourceBuilder<ContainerResource> conversion,
        IResourceBuilder<OpenTelemetryCollectorResource> otel)
    {
        var grpc = otel.GetEndpoint(
            OpenTelemetryCollectorResource.OtlpGrpcEndpointName,
            KnownNetworkIdentifiers.DefaultAspireContainerNetwork);

        return conversion.WithEnvironment(
            "OTEL_EXPORTER_OTLP_ENDPOINT",
            ReferenceExpression.Create(
                $"http://{grpc.Property(EndpointProperty.Host)}:{grpc.Property(EndpointProperty.Port)}"));
    }
}
