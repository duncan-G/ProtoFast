using ProtoFast.AppHost.OpenTelemetryCollector;

namespace ProtoFast.AppHost.EnvoyProxy;

public static class EnvoyProxyResourceBuilderExtensions
{
    private const string EnvoyConfigPath = "../proxy";
    private const int FirstClientListenerPort = 20000;

    // Cookies ignore ports, so each dev client needs a hostname of its own or they all share one
    // pf_session. The ASP.NET dev certificate covers *.dev.localhost.
    private const string DevDomainSuffix = "dev.localhost";

    /// <summary>
    /// Adds the Envoy proxy container. The entrypoint renders its config from templates based
    /// on <c>ENVOY_MODE</c>:
    /// <list type="bullet">
    /// <item><c>dev</c> — one HTTPS listener per client (see <see cref="WithClient"/>), each
    /// routing its catch-all to that client's Angular dev server.</item>
    /// <item><c>dev-host</c> — same per-client listeners, but catch-alls route to the unified
    /// SSR host container (local smoke test of the publish artifact).</item>
    /// <item><c>publish</c> — a single listener with one virtual host per client domain, all
    /// routing to the unified SSR host.</item>
    /// </list>
    /// </summary>
    public static IResourceBuilder<ContainerResource> AddEnvoyProxy(
        this IDistributedApplicationBuilder builder,
        string name,
        bool useSsrHostInDev = false)
    {
        var envoy = builder
            .AddDockerfile(name, EnvoyConfigPath)
            .WithEntrypoint("/bin/sh")
            .WithArgs("/etc/envoy/entrypoint.sh");

        var clientsAnnotation = new EnvoyClientsAnnotation();
        envoy.Resource.Annotations.Add(clientsAnnotation);

        var mode = builder.ExecutionContext.IsPublishMode
            ? "publish"
            : useSsrHostInDev ? "dev-host" : "dev";

        envoy
            .WithEnvironment("ENVOY_MODE", mode)
            .WithEnvironment(ctx =>
            {
                ctx.EnvironmentVariables["CLIENTS"] = string.Join(',', clientsAnnotation.Clients);
                foreach (var host in clientsAnnotation.Consoles.GroupBy(c => c.Client))
                {
                    ctx.EnvironmentVariables[$"CLIENT_{ToEnvName(host.Key)}_CONSOLES"] =
                        string.Join(',', host.Select(c => c.Console));
                }
            });

        if (builder.ExecutionContext.IsPublishMode)
        {
            envoy
                .WithHttpsEndpoint(targetPort: FirstClientListenerPort, env: "PORT", isProxied: false)
                .WithEndpoint("https", e => e.IsExternal = true);
        }
        else
        {
            // Per-client listeners are added by WithClient; no base listener in dev.
            envoy
                .WithHttpsCertificateConfiguration(ctx =>
                {
                    ctx.EnvironmentVariables["ENVOY_TLS_CERT"] = ctx.CertificatePath;
                    ctx.EnvironmentVariables["ENVOY_TLS_KEY"] = ctx.KeyPath;
                    return Task.CompletedTask;
                })
                .WithContainerRuntimeArgs("--add-host=host.docker.internal:host-gateway");
        }

        envoy
            .WithHttpEndpoint(targetPort: 9901, env: "ENVOY_ADMIN_PORT", name: "admin", isProxied: false)
            .WithUrlForEndpoint("admin", u => u.DisplayText = "Envoy Admin")
            .WithHttpHealthCheck("/ready", statusCode: 200, endpointName: "admin");

        return envoy;
    }

    /// <summary>
    /// Registers a client with the proxy and returns its browser origin (pages and API share it).
    /// In run mode this adds a dedicated HTTPS listener served as <c>«client».dev.localhost</c>;
    /// in publish mode this wires a <c>«client»-domain</c> parameter into the client's virtual host.
    /// </summary>
    public static ReferenceExpression WithClient(
        this IResourceBuilder<ContainerResource> envoy,
        IDistributedApplicationBuilder applicationBuilder,
        string clientName)
    {
        var clientsAnnotation = envoy.Resource.Annotations
            .OfType<EnvoyClientsAnnotation>()
            .Single();
        var listenerPort = FirstClientListenerPort + clientsAnnotation.Clients.Count;
        clientsAnnotation.Clients.Add(clientName);

        var envName = ToEnvName(clientName);

        if (applicationBuilder.ExecutionContext.IsPublishMode)
        {
            var domain = applicationBuilder.AddParameter(
                $"{clientName}-domain", $"{clientName}.example.com", publishValueAsDefault: true);
            clientsAnnotation.Domains.Add(domain.Resource);
            envoy.WithEnvironment($"CLIENT_{envName}_DOMAIN", domain);
            return ReferenceExpression.Create($"https://{domain}");
        }

        var endpointName = $"{clientName}-web";
        var origin = DevOrigin(clientName, listenerPort);
        // Pin the host port too — Keycloak's redirect URIs are exact
        // (https://admin.dev.localhost:20000/signin-oidc). targetPort-only maps a random
        // host port and the authorize request fails with invalid_redirect_uri.
        envoy
            .WithHttpsEndpoint(port: listenerPort, targetPort: listenerPort, name: endpointName, isProxied: false)
            .WithEnvironment($"CLIENT_{envName}_LISTENER_PORT", listenerPort.ToString())
            .WithEnvironment($"CLIENT_{envName}_DOMAIN", DevDomain(clientName))
            .WithUrlForEndpoint(endpointName, u =>
            {
                u.Url = origin;
                u.DisplayText = $"{clientName} (web)";
            });

        return ReferenceExpression.Create($"{origin}");
    }

    /// <summary>
    /// The hostnames browsers use to reach the clients through the proxy: the client domain
    /// parameters in publish mode, or <c>«client».dev.localhost</c> in run mode. Feed this to the
    /// SSR host's <c>NG_ALLOWED_HOSTS</c>.
    /// </summary>
    public static ReferenceExpression GetClientHostnames(this IResourceBuilder<ContainerResource> envoy)
    {
        var clients = envoy.Resource.Annotations
            .OfType<EnvoyClientsAnnotation>()
            .Single();
        var domains = clients.Domains;

        if (domains.Count == 0)
        {
            var devDomains = string.Join(',', clients.Clients.Select(DevDomain));
            return ReferenceExpression.Create($"{devDomains}");
        }

        var expression = new ReferenceExpressionBuilder();
        for (var i = 0; i < domains.Count; i++)
        {
            if (i > 0)
            {
                expression.AppendLiteral(",");
            }

            expression.Append($"{domains[i]}");
        }

        return expression.Build();
    }

    public static IReadOnlyList<string> GetClientOrigins(this IResourceBuilder<ContainerResource> envoy)
    {
        if (envoy.ApplicationBuilder.ExecutionContext.IsPublishMode)
        {
            return [];
        }

        var clients = envoy.Resource.Annotations
            .OfType<EnvoyClientsAnnotation>()
            .Single()
            .Clients;

        return [.. clients.Select((client, i) => DevOrigin(client, FirstClientListenerPort + i))];
    }

    /// <summary>
    /// Mounts an app console at <c>/{console}/</c> on <paramref name="clientName"/>'s host, served
    /// by its own process at <paramref name="endpoint"/> and only to holders of
    /// <c>admin-{console}</c> (docs/design/admin-consoles.md).
    /// </summary>
    public static IResourceBuilder<ContainerResource> WithConsole(
        this IResourceBuilder<ContainerResource> envoy,
        string clientName,
        string consoleName,
        EndpointReference endpoint)
    {
        envoy.Resource.Annotations
            .OfType<EnvoyClientsAnnotation>()
            .Single()
            .Consoles.Add((clientName, consoleName));
        return envoy.WithUpstreamEndpoint($"CONSOLE_{ToEnvName(consoleName)}", endpoint);
    }

    public static IResourceBuilder<ContainerResource> WithUpstreamEndpoint(
        this IResourceBuilder<ContainerResource> envoy,
        string name,
        EndpointReference endpoint)
    {
        envoy.WithEnvironment($"{name}_HOST", endpoint.Property(EndpointProperty.Host));
        envoy.WithEnvironment($"{name}_PORT", endpoint.Property(EndpointProperty.Port));
        return envoy;
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

    private static string DevDomain(string clientName) => $"{clientName}.{DevDomainSuffix}";

    private static string DevOrigin(string clientName, int listenerPort) =>
        $"https://{DevDomain(clientName)}:{listenerPort}";

    private static string ToEnvName(string clientName) =>
        clientName.ToUpperInvariant().Replace('-', '_');

    private sealed class EnvoyClientsAnnotation : IResourceAnnotation
    {
        public List<string> Clients { get; } = [];

        public List<ParameterResource> Domains { get; } = [];

        public List<(string Client, string Console)> Consoles { get; } = [];
    }
}
