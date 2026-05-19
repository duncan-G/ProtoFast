# Step 1 — Create the OTel collector container

The collector runs as a custom Docker image built from the upstream
`opentelemetry-collector-contrib` image. It receives OTLP telemetry
(gRPC on port 4317, HTTP on port 4318), batches it, and exports to
the Aspire dashboard's OTLP endpoint.

## 1a. `otel-collector/` directory structure

```
otel-collector/
├── Dockerfile
└── config.yaml
```

## 1b. `otel-collector/Dockerfile`

```dockerfile
FROM ghcr.io/open-telemetry/opentelemetry-collector-releases/opentelemetry-collector-contrib:0.123.0
COPY config.yaml /etc/otelcol-contrib/config.yaml
```

Pin to a specific tag (not `latest`) for reproducible builds. The
`-contrib` variant includes the full set of receivers, processors,
and exporters.

## 1c. `otel-collector/config.yaml`

```yaml
receivers:
  otlp:
    protocols:
      grpc:
        endpoint: 0.0.0.0:${env:OTLP_GRPC_PORT}
      http:
        endpoint: 0.0.0.0:${env:OTLP_HTTP_PORT}

processors:
  batch:
  resource/service-name:
    attributes:
      - key: service.name
        value: envoy-proxy
        action: insert

exporters:
  debug:
    verbosity: detailed
  otlp/aspire:
    endpoint: ${env:OTEL_EXPORTER_OTLP_ENDPOINT}
    headers:
      x-otlp-api-key: ${env:ASPIRE_API_KEY}
    tls:
      insecure: ${env:ASPIRE_INSECURE}
      insecure_skip_verify: true

service:
  pipelines:
    traces:
      receivers: [otlp]
      processors: [batch]
      exporters: [otlp/aspire]
    logs:
      receivers: [otlp]
      processors: [batch]
      exporters: [otlp/aspire]
    metrics:
      receivers: [otlp]
      processors: [resource/service-name, batch]
      exporters: [otlp/aspire]
```

Notes:

- Ports use `${env:...}` syntax — the AppHost injects `OTLP_GRPC_PORT`
  and `OTLP_HTTP_PORT` as container-internal ports.
- `OTEL_EXPORTER_OTLP_ENDPOINT` is the Aspire dashboard's OTLP
  ingress URL, derived from the dashboard configuration (see 1d).
- `ASPIRE_API_KEY` authenticates the collector with the dashboard.
- `ASPIRE_INSECURE` is `true` in dev mode, `false` in publish mode.
- `resource/service-name` processor inserts `service.name` for Envoy
  metrics (Envoy's stats sink sends metrics without a service name
  resource attribute).
- The metrics pipeline includes `resource/service-name` before `batch`;
  traces and logs pipelines use `batch` only (services send their own
  `service.name`).

## 1d. `apphost/OpenTelemetryCollector/OpenTelemetryCollectorResource.cs`

```csharp
namespace «ProjectName».AppHost.OpenTelemetryCollector;

public class OpenTelemetryCollectorResource(string name) : ContainerResource(name), IResourceWithServiceDiscovery
{
    internal const string OtlpGrpcEndpointName = "otlp-grpc";
    internal const string OtlpHttpEndpointName = "otlp-http";
}
```

The resource type:

- Extends `ContainerResource` (it runs as a Docker container).
- Implements `IResourceWithServiceDiscovery` so other resources can
  resolve the collector's endpoints via Aspire service discovery.
- Exposes endpoint name constants for consistent reference across
  the AppHost.

## 1e. `apphost/OpenTelemetryCollector/OpenTelemetryCollectorResourceBuilderExtensions.cs`

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace «ProjectName».AppHost.OpenTelemetryCollector;

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
```

Key design decisions:

- **Custom resource type** rather than a plain `AddContainer` call. This
  allows typed `IResourceBuilder<OpenTelemetryCollectorResource>`
  overloads so the compiler prevents accidentally passing a different
  container to `WithOtlpCollectorReference`.
- **`WithDockerfile`** builds from `../otel-collector` (relative to
  AppHost) so the collector image is built alongside the app.
- **gRPC endpoint gets `Transport = "http2"`** so Aspire's proxy uses
  h2c (HTTP/2 cleartext) — required for gRPC.
- **Dashboard URL endpoints are details-only** (`UrlDisplayLocation.DetailsOnly`) to
  avoid cluttering the Aspire dashboard's main URL list.
- **`WithOtlpExporter`** ensures the collector itself sends its own
  internal telemetry to the dashboard (collector health metrics).
- **Dev mode** adds `--add-host=host.docker.internal:host-gateway` so
  the container can reach the host-bound Aspire dashboard process,
  and derives the OTLP export endpoint from the dashboard URL
  (replacing listen-all addresses like `0.0.0.0` with
  `host.docker.internal` and forcing HTTPS).
- **`WithOtlpCollectorReference`** is generic over `T : IResourceWithEnvironment`
  so it works with both `ProjectResource` and `ContainerResource`.
  It overrides `OTEL_EXPORTER_OTLP_ENDPOINT` — the standard env var
  that the .NET OpenTelemetry SDK reads — to point at the collector's
  gRPC endpoint instead of the dashboard.
