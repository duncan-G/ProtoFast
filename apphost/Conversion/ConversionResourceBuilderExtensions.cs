using ProtoFast.AppHost.LocalStack;

namespace ProtoFast.AppHost.Conversion;

/// <summary>
/// The document-conversion sidecar (ingest plan §19).
///
/// <para>A container built from its own Dockerfile rather than <c>AddPythonApp</c>, because the
/// converter needs Tesseract, Ghostscript and ExifTool on the box — nobody should have to install
/// those to run <c>aspire run</c>. The first run after this lands pulls a large image (Tesseract's
/// data files and Ghostscript dominate); see docs/02-local-development.md.</para>
/// </summary>
public static class ConversionResourceBuilderExtensions
{
    /// <summary>The port the converter listens on, matching the compose service in deploy/.</summary>
    private const int Port = 8090;

    private const string ContextPath = "../services/conversion";

    public static IResourceBuilder<ContainerResource> AddConversionService(
        this IDistributedApplicationBuilder builder,
        string name,
        string bucketName,
        string region) =>
        builder
            .AddDockerfile(name, ContextPath)
            // Proxied and allocated, unlike LocalStack's gateway: nothing here ends up in a
            // browser, and the only caller is the segmentation worker, which is handed the
            // resolved endpoint below.
            .WithHttpEndpoint(targetPort: Port, env: "CONVERSION_PORT")
            .WithEnvironment("CONVERSION_BUCKET", bucketName)
            .WithEnvironment("AWS_REGION", region)
            .WithEnvironment("AWS_DEFAULT_REGION", region)
            .WithHttpHealthCheck("/health");

    /// <summary>
    /// Points the converter at LocalStack (development only).
    ///
    /// <para>By container DNS, not by <c>LocalStackResourceBuilderExtensions.GatewayUrl</c>: that
    /// URL's hostname resolves to 127.0.0.1, which inside this container is this container. The
    /// scheme is plain HTTP for the same reason it is HTTPS there — nothing here is a browser on
    /// an HTTPS page, so there is no mixed content to avoid and no certificate to trust.</para>
    ///
    /// <para>The credentials are LocalStack's throwaway pair. In production the converter holds no
    /// credentials at all: boto3's default chain finds the instance role over IMDS.</para>
    /// </summary>
    public static IResourceBuilder<ContainerResource> WithLocalStack(
        this IResourceBuilder<ContainerResource> conversion,
        IResourceBuilder<ContainerResource> localstack)
    {
        var gateway = localstack.GetEndpoint(
            LocalStackResourceBuilderExtensions.GatewayEndpointName,
            KnownNetworkIdentifiers.DefaultAspireContainerNetwork);

        return conversion
            .WithEnvironment(
                "CONVERSION_S3_ENDPOINT",
                ReferenceExpression.Create(
                    $"http://{gateway.Property(EndpointProperty.Host)}:{gateway.Property(EndpointProperty.Port)}"))
            .WithEnvironment("AWS_ACCESS_KEY_ID", "localstack")
            .WithEnvironment("AWS_SECRET_ACCESS_KEY", "localstack")
            .WaitFor(localstack);
    }
}
