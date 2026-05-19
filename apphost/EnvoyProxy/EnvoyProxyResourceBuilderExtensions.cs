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
            .WithHttpEndpoint(targetPort: 8080)
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
            .WithHttpEndpoint(targetPort: 9901, name: "admin", isProxied: false)
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
