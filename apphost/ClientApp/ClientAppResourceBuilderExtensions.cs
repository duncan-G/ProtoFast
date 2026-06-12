namespace ProtoFast.AppHost.ClientApp;

public static class ClientAppResourceBuilderExtensions
{
    /// <summary>
    /// Adds a single client's Angular dev server (run mode only). The browser reaches the
    /// client through its per-client Envoy listener; <paramref name="serverEndpoint"/> is
    /// that listener's endpoint, injected as <c>SERVER_URL</c>.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="clientName">The resource name for the client app.</param>
    /// <param name="clientPath">The file-system path to the client app project directory.</param>
    /// <param name="serverEndpoint">The client's Envoy listener endpoint injected as <c>SERVER_URL</c>.</param>
    /// <param name="clientOtelEndpoint">Optional browser-side OpenTelemetry collector endpoint injected as <c>BROWSER_OTEL_ENDPOINT</c>.</param>
    /// <param name="clientServerOtelEndpoint">Optional server-side OpenTelemetry collector endpoint injected as <c>SERVER_OTEL_ENDPOINT</c>.</param>
    /// <returns>An <see cref="EndpointReference"/> for the dev server's HTTPS endpoint (used as the Envoy upstream).</returns>
    public static EndpointReference AddClientApp(
        this IDistributedApplicationBuilder builder,
        string clientName,
        string clientPath,
        EndpointReference serverEndpoint,
        EndpointReference? clientOtelEndpoint = null,
        EndpointReference? clientServerOtelEndpoint = null)
    {
        var clientAppDev = builder.AddJavaScriptApp(clientName, clientPath, runScriptName: "start")
            .WithHttpsEndpoint(env: "PORT")
            .WithHttpsDeveloperCertificate()
            .WithHttpsCertificateConfiguration(ctx =>
            {
                ctx.EnvironmentVariables["SSL_CERT"] = ctx.CertificatePath;
                ctx.EnvironmentVariables["SSL_KEY"] = ctx.KeyPath;
                return Task.CompletedTask; 
            })
            .WithEnvironment("SERVER_URL", serverEndpoint);

        clientAppDev.WithOtelEndpoints(clientOtelEndpoint, clientServerOtelEndpoint);

        // Unpinned endpoint: resolves per consumer network, so the Envoy
        // container sees a host it can reach instead of "localhost".
        return clientAppDev.GetEndpoint("https");
    }

    /// <summary>
    /// Adds the unified SSR host container (clients/host/Dockerfile, built from the repo root
    /// context) that serves every client's SSR bundle from one Node process. Used in publish
    /// mode, and in run mode when the SSR-host dev toggle is enabled.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The resource name for the unified host.</param>
    /// <param name="defaultClient">Client served when no/unknown <c>x-client</c> header is present.</param>
    /// <param name="allowedHosts">Browser-facing hostnames injected as <c>NG_ALLOWED_HOSTS</c>; Angular SSR rejects requests whose <c>Host</c> isn't listed.</param>
    /// <param name="clientOtelEndpoint">Optional browser-side OpenTelemetry collector endpoint injected as <c>BROWSER_OTEL_ENDPOINT</c>.</param>
    /// <param name="clientServerOtelEndpoint">Optional server-side OpenTelemetry collector endpoint injected as <c>SERVER_OTEL_ENDPOINT</c>.</param>
    /// <returns>An <see cref="EndpointReference"/> for the host's HTTP endpoint (used as the Envoy upstream).</returns>
    public static EndpointReference AddClientHost(
        this IDistributedApplicationBuilder builder,
        string name,
        string defaultClient,
        ReferenceExpression allowedHosts,
        EndpointReference? clientOtelEndpoint = null,
        EndpointReference? clientServerOtelEndpoint = null)
    {
        var host = builder.AddDockerfile(name, "..", "clients/host/Dockerfile")
            .WithHttpEndpoint(targetPort: 4000, env: "PORT")
            .WithEnvironment("DEFAULT_CLIENT", defaultClient)
            // Only Envoy can reach this host, so its forwarded headers are trustworthy.
            // Angular SSR strips them (and logs a warning) unless explicitly trusted.
            .WithEnvironment("NG_TRUST_PROXY_HEADERS", "x-forwarded-for,x-forwarded-proto")
            .WithEnvironment("NG_ALLOWED_HOSTS", allowedHosts);

        host.WithOtelEndpoints(clientOtelEndpoint, clientServerOtelEndpoint);

        return host.GetEndpoint("http");
    }

    private static IResourceBuilder<IResourceWithEnvironment> WithOtelEndpoints(
        this IResourceBuilder<IResourceWithEnvironment> clientApp,
        EndpointReference? clientOtelEndpoint,
        EndpointReference? clientServerOtelEndpoint)
    {
        if (clientOtelEndpoint is not null)
        {
            clientApp = clientApp.WithEnvironment("BROWSER_OTEL_ENDPOINT", clientOtelEndpoint);
        }

        if (clientServerOtelEndpoint is not null)
        {
            clientApp = clientApp.WithEnvironment("SERVER_OTEL_ENDPOINT", clientServerOtelEndpoint);
        }

        return clientApp;
    }
}
