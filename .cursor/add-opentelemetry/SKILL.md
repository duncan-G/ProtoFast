---
name: add-opentelemetry
description: >-
  Adds OpenTelemetry observability to an existing Aspire-orchestrated
  project: deploys an OTel collector container, routes .NET service
  telemetry through the collector instead of directly to the Aspire
  dashboard, configures Envoy to emit access logs / metrics / traces
  via OTel, adds an /otlp/ passthrough route for browser telemetry,
  and wires the client app to forward telemetry through Envoy. Use
  when the user asks to add observability, tracing, metrics, logging,
  OpenTelemetry, or OTel to the project.
disable-model-invocation: true
---

# Add OpenTelemetry

Adds full OpenTelemetry observability to an Aspire-orchestrated project.
Telemetry from every layer — .NET services, Envoy proxy, and browser
clients — flows through a central OTel collector that forwards to the
Aspire dashboard (and later to any additional backend).

## Placeholders

- `«ProjectName»` — PascalCase root namespace, detected from the AppHost
  `.csproj` filename (e.g. `Nimbus.AppHost.csproj` → `Nimbus`).

## Prerequisites

- `apphost/` exists with a working `.csproj` and `Program.cs`.
- `proxy/` exists with `envoy.yaml.tmpl` and `entrypoint.sh` (the
  Envoy proxy from `bootstrap-project` Step 7).
- At least one .NET gRPC service exists under `services/` with
  `ServiceDefaults` already wired (`AddServiceDefaults()` /
  `MapDefaultEndpoints()`).
- At least one Angular client is registered via `AddClientApp` in
  `apphost/ClientApp/ClientAppResourceBuilderExtensions.cs`.
- Docker is available (the OTel collector runs as a container).

## Architecture

```
Browser ──► Envoy ──/otlp/v1/──► OTel Collector ──► Aspire Dashboard
                                       ▲
.NET services (OTLP gRPC) ─────────────┘
Envoy (access logs, metrics, traces) ──┘
```

The collector is the single telemetry ingress point. .NET services send
via OTLP gRPC; Envoy sends access logs, stats-sink metrics, and traces
via OTLP gRPC; browser clients send via OTLP HTTP through Envoy's
`/otlp/v1/` route (prefix-rewritten to `/v1/`). The collector exports
everything to the Aspire dashboard's OTLP endpoint.

## Step 1 — Create the OTel collector container

**Load `references/otel-collector.md`** and follow Steps 1a–1d. Creates
`otel-collector/Dockerfile` and `otel-collector/config.yaml`, then adds
`apphost/OpenTelemetryCollector/OpenTelemetryCollectorResource.cs` and
`apphost/OpenTelemetryCollector/OpenTelemetryCollectorResourceBuilderExtensions.cs`
with `AddOpenTelemetryCollector` and `WithOtlpCollectorReference`.

After this step the AppHost can create the collector container and
redirect any resource's OTLP exporter through it.

## Step 2 — Route .NET service telemetry through the collector

In `apphost/Program.cs`:

1. Add the collector:

```csharp
using «ProjectName».AppHost.OpenTelemetryCollector;

var otel = builder.AddOpenTelemetryCollector("otel-collector");
```

2. Chain `.WithOtlpCollectorReference(otel)` on every
   `builder.AddProject<...>(...)` call:

```csharp
var auth     = builder.AddProject<Projects.«ProjectName»_Auth_Api>("auth").WithOtlpCollectorReference(otel);
var payments = builder.AddProject<Projects.«ProjectName»_Payments_Api>("payments").WithOtlpCollectorReference(otel);
var api      = builder.AddProject<Projects.«ProjectName»_Api>("api").WithOtlpCollectorReference(otel);
```

`WithOtlpCollectorReference` overrides `OTEL_EXPORTER_OTLP_ENDPOINT`
so the service's existing `ServiceDefaults` OpenTelemetry configuration
routes through the collector instead of directly to the dashboard.

## Step 3 — Configure Envoy to emit telemetry to the collector

**Load `references/envoy-otel.md`** and follow Steps 3a–3f. Updates
`envoy.yaml.tmpl` with OTel access loggers, a stats sink, a tracing
provider, OTel collector clusters, and a `/otlp/v1/` client-telemetry
passthrough route. Updates `entrypoint.sh` with new required env vars
and TLS handling. Adds `WithOtelCollectorEndpoints` to
`EnvoyProxyResourceBuilderExtensions.cs` and wires it in `Program.cs`.

## Step 4 — Wire client app OTel endpoints in AppHost

Update the `AddClientApp` call in `apphost/Program.cs` to pass the
collector's HTTP and gRPC endpoints:

```csharp
var adminEndpoint = builder.AddClientApp("admin", "../clients/admin", 4000, proxy.GetEndpoint("http"),
    clientOtelEndpoint: otel.GetEndpoint(OpenTelemetryCollectorResource.OtlpHttpEndpointName),
    clientServerOtelEndpoint: otel.GetEndpoint(OpenTelemetryCollectorResource.OtlpGrpcEndpointName));
```

If the `AddClientApp` method does not yet accept `clientOtelEndpoint`
and `clientServerOtelEndpoint` parameters, update
`apphost/ClientApp/ClientAppResourceBuilderExtensions.cs`:

1. Add optional parameters to `AddClientApp`:

```csharp
public static EndpointReference AddClientApp(
    this IDistributedApplicationBuilder builder,
    string clientName,
    string clientPath,
    int productionPort,
    EndpointReference serverEndpoint,
    EndpointReference? clientOtelEndpoint = null,
    EndpointReference? clientServerOtelEndpoint = null)
```

2. Add a private `WithOtelEndpoints` helper that maps them to env vars:
   - `clientOtelEndpoint` → `BROWSER_OTEL_ENDPOINT` (browser-side OTLP
     HTTP endpoint — the browser sends telemetry here)
   - `clientServerOtelEndpoint` → `SERVER_OTEL_ENDPOINT` (SSR
     server-side OTLP gRPC endpoint)

3. Call `WithOtelEndpoints` in both the publish-mode and dev-mode
   branches.

These env vars are consumed by the client app's OTel instrumentation
(configured in Step 5).

## Step 5 — Configure client app to send telemetry

<!-- To be implemented — this step will configure the Angular client's
     OpenTelemetry SDK to send browser telemetry to the /otlp/v1/ route
     and SSR server telemetry directly to the collector's gRPC endpoint
     using the BROWSER_OTEL_ENDPOINT and SERVER_OTEL_ENDPOINT env vars
     injected in Step 4. -->

## Full `apphost/Program.cs` after all steps

```csharp
using «ProjectName».AppHost.ClientApp;
using «ProjectName».AppHost.EnvoyProxy;
using «ProjectName».AppHost.OpenTelemetryCollector;

var builder = DistributedApplication.CreateBuilder(args);

var otel = builder.AddOpenTelemetryCollector("otel-collector");

var auth     = builder.AddProject<Projects.«ProjectName»_Auth_Api>("auth").WithOtlpCollectorReference(otel);
var payments = builder.AddProject<Projects.«ProjectName»_Payments_Api>("payments").WithOtlpCollectorReference(otel);
var api      = builder.AddProject<Projects.«ProjectName»_Api>("api").WithOtlpCollectorReference(otel);

var proxy = builder.AddEnvoyProxy("envoy")
    .WithOtelCollectorEndpoints(otel)
    .WaitFor(auth)
    .WaitFor(payments)
    .WaitFor(api);

var adminEndpoint = builder.AddClientApp("admin", "../clients/admin", 4000, proxy.GetEndpoint("http"),
    clientOtelEndpoint: otel.GetEndpoint(OpenTelemetryCollectorResource.OtlpHttpEndpointName),
    clientServerOtelEndpoint: otel.GetEndpoint(OpenTelemetryCollectorResource.OtlpGrpcEndpointName));

proxy
    .WithCorsOriginExact(builder, adminEndpoint)
    .WithCorsOriginSubdomainRegex(builder, adminEndpoint)
    .WithAllowedHosts(builder);

proxy
    .WithClusterEndpoint(builder, "ADMIN", adminEndpoint)
    .WithClusterEndpoint(builder, "AUTH", auth.GetEndpoint("http"))
    .WithClusterEndpoint(builder, "PAYMENTS", payments.GetEndpoint("http"))
    .WithClusterEndpoint(builder, "API", api.GetEndpoint("http"));

builder.Build().Run();
```

Key ordering: the OTel collector is created first (other resources
depend on it); Envoy gets `.WithOtelCollectorEndpoints(otel)` before
the `WaitFor` calls; the client app receives the collector endpoints.

## Guardrails

- Never hardcode ports — Aspire assigns them dynamically.
- The OTel collector's OTLP endpoints use `http` scheme (not `https`)
  in dev mode. In publish mode the collector may sit behind TLS
  ingress; the entrypoint handles this by injecting a TLS transport
  socket when the gRPC port is 443.
- The `/otlp/v1/` route has all tracing sampling set to 0% to avoid
  recursive trace loops (telemetry about telemetry).
- Access log filters exclude `/otlp/` paths from both the file logger
  and the OTel logger to prevent log feedback loops.
- The CORS `allow_headers` list must include `traceparent`, `tracestate`,
  `b3`, and `baggage` for distributed tracing context propagation from
  the browser.
