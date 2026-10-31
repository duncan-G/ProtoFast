using Aspire.Hosting.JavaScript;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ProtoFast.AppHost.ClientApp;

public static class ClientAppResourceBuilderExtensions
{
    private static readonly HttpClient DevServerProbe = new() { Timeout = TimeSpan.FromSeconds(5) };

    /// <summary>
    /// Adds a client's Angular dev server, with HMR, for <c>aspire run</c> only. ProtoStrap's
    /// local edge reaches it over plain HTTP, at the routes the caller gives it with
    /// PublishAsProtoStrapService, so the browser uses the client's host
    /// (<c>http://«host».dev.localhost:20000</c>) exactly as in an account.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The client's name: its directory under clients/ and its resource.</param>
    /// <param name="serverUrl">The origin of the host it is served on, its <c>SERVER_URL</c>.</param>
    /// <param name="environment">Settings the platform owns, e.g. an admin console's <c>ADMIN_CONSOLE_ROLES</c>.</param>
    public static IResourceBuilder<JavaScriptAppResource> AddClientApp(
        this IDistributedApplicationBuilder builder,
        string name,
        string serverUrl,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var client = builder.AddJavaScriptApp(name, $"../clients/{name}", runScriptName: "start")
            .WithHttpEndpoint(env: "PORT")
            // The browser's gRPC-Web calls and telemetry go to the client's own origin.
            .WithEnvironment("SERVER_URL", serverUrl)
            .WithOtlpExporter(OtlpProtocol.HttpProtobuf);

        foreach (var (key, value) in environment ?? new Dictionary<string, string>())
        {
            client.WithEnvironment(key, value);
        }

        client.WithDevServerHealthCheck(builder, name);
        return client;
    }

    /// <summary>
    /// Reports the dev server unhealthy until it actually serves HTTP.
    /// </summary>
    /// <remarks>
    /// <c>ng serve</c> survives a failed Angular build: the process stays up and simply never
    /// binds its port. With no health check registered Aspire treats "process running" as
    /// healthy, so a broken build shows green on the dashboard while the edge's requests to it
    /// fail. This is an association only: nothing waits on it.
    /// </remarks>
    private static void WithDevServerHealthCheck<T>(
        this IResourceBuilder<T> clientApp,
        IDistributedApplicationBuilder builder,
        string clientName)
        where T : IResourceWithEndpoints
    {
        // Pinned to localhost: the probe runs in the apphost process, not on the container network.
        var endpoint = clientApp.GetEndpoint("http", KnownNetworkIdentifiers.LocalhostNetwork);
        var healthCheckKey = $"{clientName}-dev-server";

        builder.Services
            .AddHealthChecks()
            .AddAsyncCheck(healthCheckKey, async cancellationToken =>
            {
                if (!endpoint.IsAllocated)
                {
                    return HealthCheckResult.Unhealthy("Dev server endpoint is not allocated yet.");
                }

                try
                {
                    using var response = await DevServerProbe.GetAsync(
                        new Uri(new Uri(endpoint.Url), "/"),
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken);

                    // Any answer means the build finished and the server is listening. The status
                    // code itself is not a health signal: `/` is a redirect to sign-in for a client
                    // whose root is protected (admin), and Angular's SSRF guard answers 400 for a
                    // Host it was not told to allow.
                    return (int)response.StatusCode < 500
                        ? HealthCheckResult.Healthy()
                        : HealthCheckResult.Unhealthy($"Dev server returned {(int)response.StatusCode}.");
                }
                catch (Exception ex)
                {
                    return HealthCheckResult.Unhealthy("Dev server is not accepting requests.", ex);
                }
            });

        clientApp.WithHealthCheck(healthCheckKey);
    }
}
