using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ProtoFast.AppHost.OpenTelemetryCollector;

public static class OpenTelemetryCollectorResourceBuilderExtensions
{
    private const string DashboardOtlpUrlVariableName = "ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL";
    private const string DashboardOtlpApiKeyVariableName = "AppHost:OtlpApiKey";
    private const string OtelConfigPath = "../otel-collector";
    private const int OtlpGrpcContainerPort = 4317;
    private const int OtlpHttpContainerPort = 4318;
    private const string scheme = "http";

    public static IResourceBuilder<OpenTelemetryCollectorResource> AddOpenTelemetryCollector(this IDistributedApplicationBuilder builder, string name)
    {
        var otlpApiKey = builder.Configuration[DashboardOtlpApiKeyVariableName] ?? string.Empty;

        var collectorResource = new OpenTelemetryCollectorResource(name);

        var resourceBuilder = builder
            .AddResource(collectorResource)
            .WithImage("otel-collector")
            .WithDockerfile(OtelConfigPath)
            .WithEndpoint(
                targetPort: OtlpGrpcContainerPort,
                name: OpenTelemetryCollectorResource.OtlpGrpcEndpointName,
                scheme: scheme)
            .WithEndpoint(OpenTelemetryCollectorResource.OtlpGrpcEndpointName, e => e.Transport = "http2")
            .WithEndpoint(
                targetPort: OtlpHttpContainerPort,
                name: OpenTelemetryCollectorResource.OtlpHttpEndpointName,
                scheme: scheme)
            .WithUrlForEndpoint(OpenTelemetryCollectorResource.OtlpGrpcEndpointName, u => u.DisplayLocation = UrlDisplayLocation.DetailsOnly)
            .WithUrlForEndpoint(OpenTelemetryCollectorResource.OtlpHttpEndpointName, u => u.DisplayLocation = UrlDisplayLocation.DetailsOnly)
            .WithEnvironment("ASPIRE_API_KEY", otlpApiKey)
            .WithEnvironment("ASPIRE_INSECURE", builder.ExecutionContext.IsPublishMode ? "false" : "true")
            .WithEnvironment("OTLP_GRPC_PORT", OtlpGrpcContainerPort.ToString())
            .WithEnvironment("OTLP_HTTP_PORT", OtlpHttpContainerPort.ToString())
            .WithOtlpExporter();

        if (!builder.ExecutionContext.IsPublishMode)
        {
            resourceBuilder = resourceBuilder.WithContainerRuntimeArgs("--add-host=host.docker.internal:host-gateway");
            if (TryBuildCollectorOtlpEndpointFromDashboardUrl(builder.Configuration, out var collectorOtlpEndpoint))
            {
                resourceBuilder = resourceBuilder.WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", collectorOtlpEndpoint);
            }
        }

        return resourceBuilder;
    }

    /// <summary>
    /// Derives the collector's OTLP export URL from <see cref="DashboardOtlpUrlVariableName"/>: dashboard may use
    /// <c>0.0.0.0</c> (listen on all interfaces) but the collector must dial <c>host.docker.internal</c>; force
    /// <c>https</c>; use authority only so the collector never sees a path like <c>...:21086/</c>.
    /// </summary>
    private static bool TryBuildCollectorOtlpEndpointFromDashboardUrl(IConfiguration configuration, out string collectorOtlpEndpoint)
    {
        collectorOtlpEndpoint = string.Empty;
        var dashboardUrl = configuration[DashboardOtlpUrlVariableName];
        if (string.IsNullOrWhiteSpace(dashboardUrl) || !Uri.TryCreate(dashboardUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        var uriBuilder = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps };
        if (IsHostThatNeedsDockerInternalReplacement(uriBuilder.Host))
        {
            uriBuilder.Host = "host.docker.internal";
        }

        collectorOtlpEndpoint = uriBuilder.Uri.GetLeftPart(UriPartial.Authority);
        return true;
    }

    private static bool IsHostThatNeedsDockerInternalReplacement(string host) =>
        string.Equals(host, "0.0.0.0", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "[::]", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "::", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "[::1]", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Overrides <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> to route telemetry through the collector
    /// instead of directly to the Aspire dashboard. The collector forwards to the dashboard
    /// via its own <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> (set by Aspire on the collector container).
    /// </summary>
    public static IResourceBuilder<T> WithOtlpCollectorReference<T>(
        this IResourceBuilder<T> builder,
        IResourceBuilder<OpenTelemetryCollectorResource> otelCollector)
        where T : IResourceWithEnvironment
    {
        return builder
            .WithReference(otelCollector)
            .WithEnvironment(
                "OTEL_EXPORTER_OTLP_ENDPOINT",
                otelCollector.GetEndpoint(OpenTelemetryCollectorResource.OtlpGrpcEndpointName));
    }
}
