namespace ProtoFast.AppHost.ClientApp;

public static class ClientAppResourceBuilderExtensions
{
    public static EndpointReference AddClientApp(
        this IDistributedApplicationBuilder builder,
        string clientName,
        string clientPath,
        EndpointReference serverEndpoint,
        EndpointReference? clientOtelEndpoint = null,
        EndpointReference? clientServerOtelEndpoint = null)
    {
        if (builder.ExecutionContext.IsPublishMode)
        {
            var clientApp = builder.AddDockerfile(clientName, clientPath)
                // Allocate a port and inject it as the PORT environment variable
                .WithHttpEndpoint(env: "PORT")
                .WithExternalHttpEndpoints();

            var clientEndpoint = clientApp.GetEndpoint("http", KnownNetworkIdentifiers.PublicInternet);
            clientApp
                .WithEnvironment("NG_ALLOWED_HOSTS", clientEndpoint.Property(EndpointProperty.Host))
                .WithEnvironment("SERVER_URL", serverEndpoint)
                .WithOtelEndpoints(clientOtelEndpoint, clientServerOtelEndpoint);

            return clientEndpoint;
        }

        var clientAppDev = builder.AddJavaScriptApp(clientName, clientPath, runScriptName: "start")
            // Allocate a port and inject it as the PORT environment variable
            .WithHttpEndpoint(env: "PORT")
            .WithEnvironment("SERVER_URL", serverEndpoint);
            
        clientAppDev.WithOtelEndpoints(clientOtelEndpoint, clientServerOtelEndpoint);

        return clientAppDev.GetEndpoint("http");
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
