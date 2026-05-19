using ProtoFast.AppHost.OpenTelemetryCollector;

namespace ProtoFast.AppHost.EnvoyProxy;

public static class EnvoyProxyResourceBuilderExtensions
{
    private const string EnvoyConfigPath = "../proxy";

    public static IResourceBuilder<ContainerResource> AddEnvoyProxy(
        this IDistributedApplicationBuilder builder,
        string name)
    {
        var envoy = builder
            .AddDockerfile(name, EnvoyConfigPath)
            .WithHttpEndpoint(targetPort: 20000, env: "PORT")
            .WithEntrypoint("/bin/sh")
            .WithArgs("/etc/envoy/entrypoint.sh");

        if (builder.ExecutionContext.IsPublishMode)
        {
            envoy.WithEndpoint("http", e => e.IsExternal = true);
        }
        else
        {
            envoy = envoy.WithContainerRuntimeArgs("--add-host=host.docker.internal:host-gateway");
        }

        envoy
            .WithHttpEndpoint(targetPort: 9901, env: "ENVOY_ADMIN_PORT", name: "admin", isProxied: false)
            .WithUrlForEndpoint("admin", u => u.DisplayText = "Envoy Admin")
            .WithHttpHealthCheck("/ready", statusCode: 200, endpointName: "admin");

        return envoy;
    }

    public static IResourceBuilder<ContainerResource> WithCorsOriginExact(
        this IResourceBuilder<ContainerResource> envoy,
        IDistributedApplicationBuilder applicationBuilder,
        EndpointReference clientEndpoint)
    {
        if (applicationBuilder.ExecutionContext.IsPublishMode)
        {
            var clientHost = clientEndpoint.Property(EndpointProperty.Host);
            var clientScheme = "https";
            return envoy.WithEnvironment("CORS_ORIGIN_EXACT",
                ReferenceExpression.Create($"{clientScheme}://{clientHost}"));
        }

        return envoy.WithEnvironment("CORS_ORIGIN_EXACT", clientEndpoint);
    }

    public static IResourceBuilder<ContainerResource> WithClusterEndpoint(
        this IResourceBuilder<ContainerResource> envoy,
        IDistributedApplicationBuilder applicationBuilder,
        string name,
        EndpointReference endpoint)
    {
        envoy.WithEnvironment($"{name}_HOST", endpoint.Property(EndpointProperty.Host));
        envoy.WithEnvironment($"{name}_PORT", endpoint.Property(EndpointProperty.Port));
        return envoy;
    }

    public static IResourceBuilder<ContainerResource> WithCorsOriginSubdomainRegex(
        this IResourceBuilder<ContainerResource> envoy,
        IDistributedApplicationBuilder applicationBuilder,
        EndpointReference clientEndpoint)
    {
        if (applicationBuilder.ExecutionContext.IsPublishMode)
        {
            return envoy;
        }

        var clientHost = clientEndpoint.Property(EndpointProperty.HostAndPort);
        var clientScheme = clientEndpoint.Property(EndpointProperty.Scheme);
        var corsOriginSubdomainRegex = ReferenceExpression.Create($"{clientScheme}://*.{clientHost}");
        return envoy.WithEnvironment("CORS_ORIGIN_SUBDOMAIN_REGEX", corsOriginSubdomainRegex);
    }

    /// <summary>
    /// Wires the OTel collector's gRPC and HTTP endpoints into envoy-specific env vars
    /// (<c>OTEL_GRPC_HOST/PORT</c>, <c>OTEL_HTTP_HOST/PORT</c>, <c>OTEL_INSTANCE_ID</c>)
    /// so the entrypoint can template them into the envoy config.
    /// </summary>
    public static IResourceBuilder<ContainerResource> WithOtelCollectorEndpoints(
        this IResourceBuilder<ContainerResource> envoy,
        IResourceBuilder<OpenTelemetryCollectorResource> otelCollector)
    {
        var grpc = otelCollector.GetEndpoint(OpenTelemetryCollectorResource.OtlpGrpcEndpointName);
        var http = otelCollector.GetEndpoint(OpenTelemetryCollectorResource.OtlpHttpEndpointName);

        return envoy
            .WithReference(otelCollector)
            .WithEnvironment("OTEL_GRPC_HOST", grpc.Property(EndpointProperty.Host))
            .WithEnvironment("OTEL_GRPC_PORT", grpc.Property(EndpointProperty.Port))
            .WithEnvironment("OTEL_HTTP_HOST", http.Property(EndpointProperty.Host))
            .WithEnvironment("OTEL_HTTP_PORT", http.Property(EndpointProperty.Port))
            .WithEnvironment("OTEL_INSTANCE_ID", envoy.Resource.Name);
    }

    /// <remarks>
    /// ACA terminates TLS externally; the browser's Host header has no port, so HostAndPort (which
    /// includes the internal target port :80) would prevent Envoy's virtual-host domain matching.
    /// </remarks>
    public static IResourceBuilder<ContainerResource> WithAllowedHosts(
        this IResourceBuilder<ContainerResource> envoy,
        IDistributedApplicationBuilder applicationBuilder)
    {
        var endpointProperty = applicationBuilder.ExecutionContext.IsPublishMode
            ? EndpointProperty.Host
            : EndpointProperty.HostAndPort;

        var network = applicationBuilder.ExecutionContext.IsPublishMode
            ? KnownNetworkIdentifiers.PublicInternet
            : KnownNetworkIdentifiers.LocalhostNetwork;

        return envoy.WithEnvironment("ALLOWED_HOSTS",
            envoy.GetEndpoint("http", network).Property(endpointProperty));
    }
}
