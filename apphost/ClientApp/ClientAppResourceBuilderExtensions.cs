namespace ProtoFast.AppHost.ClientApp;

public static class ClientAppResourceBuilderExtensions
{
    /// <summary>
    /// Adds a client app to the distributed application. In publish mode, the client is built as a
    /// Docker container with external HTTP endpoints.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="clientName">The resource name for the client app.</param>
    /// <param name="clientPath">The file-system path to the client app project directory.</param>
    /// <param name="productionPort">The container port the client ssr server listens on in publish mode.</param>
    /// <param name="serverEndpoint">The backend server endpoint injected as <c>SERVER_URL</c>.</param>
    /// <param name="clientOtelEndpoint">Optional browser-side OpenTelemetry collector endpoint injected as <c>BROWSER_OTEL_ENDPOINT</c>.</param>
    /// <param name="clientServerOtelEndpoint">Optional server-side OpenTelemetry collector endpoint injected as <c>SERVER_OTEL_ENDPOINT</c>.</param>
    /// <returns>An <see cref="EndpointReference"/> for the client app's HTTPS endpoint.</returns>
    public static EndpointReference AddClientApp(
        this IDistributedApplicationBuilder builder,
        string clientName,
        string clientPath,
        int productionPort,
        EndpointReference serverEndpoint,
        EndpointReference? clientOtelEndpoint = null,
        EndpointReference? clientServerOtelEndpoint = null)
    {
        if (builder.ExecutionContext.IsPublishMode)
        {
            var clientApp = builder.AddDockerfile(clientName, clientPath)
                .WithHttpsEndpoint(targetPort: productionPort, env: "PORT")
                .WithExternalHttpEndpoints();

            var clientEndpoint = clientApp.GetEndpoint("https", KnownNetworkIdentifiers.PublicInternet);
            clientApp
                .WithEnvironment("NG_ALLOWED_HOSTS", clientEndpoint.Property(EndpointProperty.Host))
                .WithEnvironment("SERVER_URL", serverEndpoint)
                .WithOtelEndpoints(clientOtelEndpoint, clientServerOtelEndpoint);

            return clientEndpoint;
        }

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

        return clientAppDev.GetEndpoint("https", KnownNetworkIdentifiers.LocalhostNetwork);
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
