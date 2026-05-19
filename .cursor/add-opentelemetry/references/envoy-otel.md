# Step 3 — Configure Envoy to emit telemetry to the collector

Envoy sends three categories of telemetry to the OTel collector:

1. **Access logs** — per-request structured logs via the OTel access
   logger (gRPC).
2. **Metrics** — Envoy stats (counters, gauges, histograms) via the
   OTel stats sink (gRPC).
3. **Traces** — distributed tracing spans via the OTel tracer (gRPC).

Additionally, Envoy exposes a `/otlp/v1/` route that forwards
browser-originated OTLP HTTP telemetry to the collector's HTTP
endpoint (port 4318).

## 3a. Update `envoy.yaml.tmpl` — admin access log format

Replace the deprecated `access_log_path` with the structured
`access_log` block:

```yaml
admin:
  access_log:
    - name: envoy.access_loggers.file
      typed_config:
        "@type": type.googleapis.com/envoy.extensions.access_loggers.file.v3.FileAccessLog
        path: /tmp/admin_access.log
  address:
    socket_address: { address: 0.0.0.0, port_value: __ENVOY_ADMIN_PORT__ }
```

## 3b. Update `envoy.yaml.tmpl` — add node, stats sink, and flush interval

Add these blocks between `admin:` and `static_resources:`:

```yaml
node:
  id: «projectname»-proxy
  cluster: «projectname»-proxy-cluster

stats_sinks:
  - name: envoy.stat_sinks.open_telemetry
    typed_config:
      "@type": type.googleapis.com/envoy.extensions.stat_sinks.open_telemetry.v3.SinkConfig
      grpc_service:
        envoy_grpc:
          cluster_name: otel_collector_grpc_cluster
          authority: __OTEL_GRPC_HOST__

stats_flush_interval: 5s
```

- `node` identifies this Envoy instance in telemetry.
- `stats_sinks` pushes all Envoy metrics to the collector via gRPC.
- `stats_flush_interval` controls how often metrics are flushed
  (5 seconds is a reasonable default for dev).

## 3c. Update `envoy.yaml.tmpl` — add `/otlp/v1/` route and tracing headers

In the `routes:` list within the virtual host, add the OTLP
passthrough route **before** all service routes (it must match before
`/` catch-all):

```yaml
                        - match:
                            prefix: "/otlp/v1/"
                          route:
                            cluster: otel_collector_http_cluster
                            prefix_rewrite: "/v1/"
                            timeout: 0s
                          tracing:
                            client_sampling:
                              numerator: 0
                              denominator: HUNDRED
                            random_sampling:
                              numerator: 0
                              denominator: HUNDRED
                            overall_sampling:
                              numerator: 0
                              denominator: HUNDRED
```

The route rewrites `/otlp/v1/traces` → `/v1/traces` (the collector's
native OTLP HTTP path). All tracing sampling is set to 0% to prevent
recursive trace loops.

Update the CORS `allow_headers` to include distributed tracing context
propagation headers:

```yaml
                          allow_headers: "keep-alive,user-agent,cache-control,content-type,content-transfer-encoding,authorization,x-accept-content-transfer-encoding,x-accept-response-streaming,x-user-agent,x-grpc-web,grpc-timeout,traceparent,tracestate,b3,baggage"
```

And `expose_headers` to include error detail headers:

```yaml
                          expose_headers: "grpc-status,grpc-message,grpc-messages,error-code,error-codes"
```

## 3d. Update `envoy.yaml.tmpl` — add OTel access logger and tracing provider

Replace the existing `access_log:` block under
`http_connection_manager` with both a file logger and an OTel logger.
Both loggers include a filter that excludes `/otlp/` paths to prevent
log feedback loops:

```yaml
                access_log:
                  - name: envoy.access_loggers.file
                    filter:
                      and_filter:
                        filters:
                          - header_filter:
                              header:
                                name: ":path"
                                string_match:
                                  prefix: "/otlp/"
                                invert_match: true
                          - header_filter:
                              header:
                                name: "x-envoy-original-path"
                                string_match:
                                  prefix: "/otlp/"
                                invert_match: true
                    typed_config:
                      "@type": type.googleapis.com/envoy.extensions.access_loggers.file.v3.FileAccessLog
                      path: /dev/stdout
                      log_format:
                        text_format_source:
                          inline_string: "[%START_TIME%] \"%REQ(:METHOD)% %REQ(X-ENVOY-ORIGINAL-PATH?:PATH)% %PROTOCOL%\" %RESPONSE_CODE% %RESPONSE_FLAGS% %DURATION%ms\n"
                  - name: envoy.access_loggers.open_telemetry
                    filter:
                      and_filter:
                        filters:
                          - header_filter:
                              header:
                                name: ":path"
                                string_match:
                                  prefix: "/otlp/"
                                invert_match: true
                          - header_filter:
                              header:
                                name: "x-envoy-original-path"
                                string_match:
                                  prefix: "/otlp/"
                                invert_match: true
                    typed_config:
                      "@type": type.googleapis.com/envoy.extensions.access_loggers.open_telemetry.v3.OpenTelemetryAccessLogConfig
                      common_config:
                        grpc_service:
                          envoy_grpc:
                            cluster_name: otel_collector_grpc_cluster
                            authority: __OTEL_GRPC_HOST__
                          timeout: 0.5s
                        log_name: "envoy-proxy"
                      body:
                        string_value: |
                          [%START_TIME%] %REQ(:METHOD)% %REQ(X-ENVOY-ORIGINAL-PATH?:PATH)%
                          -> %RESPONSE_CODE% flags=%RESPONSE_FLAGS%
                          detail=%RESPONSE_CODE_DETAILS%
                          transport=%UPSTREAM_TRANSPORT_FAILURE_REASON%
                      attributes:
                        values:
                          - key: "upstream_cluster"
                            value: { string_value: "%UPSTREAM_CLUSTER%" }
                      resource_attributes:
                        values:
                          - key: "service.name"
                            value: { string_value: "envoy-proxy" }
                          - key: "service.instance.id"
                            value: { string_value: "__OTEL_INSTANCE_ID__" }
```

The `/otlp/` filter uses an `and_filter` on both `:path` and
`x-envoy-original-path` because prefix-rewrite changes the `:path`
header — we need to exclude based on the original path too.

Add a `tracing:` block after `access_log:` (at the same level, inside
`http_connection_manager`):

```yaml
                tracing:
                  provider:
                    name: envoy.tracers.opentelemetry
                    typed_config:
                      "@type": type.googleapis.com/envoy.config.trace.v3.OpenTelemetryConfig
                      service_name: "envoy-proxy"
                      grpc_service:
                        envoy_grpc:
                          cluster_name: otel_collector_grpc_cluster
                          authority: __OTEL_GRPC_HOST__
                        timeout: 0.5s
```

## 3e. Update `envoy.yaml.tmpl` — add OTel collector clusters

Add two clusters **before** the existing service clusters:

```yaml
    - name: otel_collector_http_cluster
      type: logical_dns
      dns_lookup_family: V4_ONLY
      lb_policy: round_robin
      load_assignment:
        cluster_name: otel_collector_http_cluster
        endpoints:
          - lb_endpoints:
              - endpoint:
                  address:
                    socket_address:
                      address: __OTEL_HTTP_HOST__
                      port_value: __OTEL_HTTP_PORT__

    - name: otel_collector_grpc_cluster
      type: logical_dns
      dns_lookup_family: V4_ONLY
      typed_extension_protocol_options:
        envoy.extensions.upstreams.http.v3.HttpProtocolOptions:
          "@type": type.googleapis.com/envoy.extensions.upstreams.http.v3.HttpProtocolOptions
          explicit_http_config:
            http2_protocol_options: {}
      lb_policy: round_robin
__OTEL_GRPC_TLS_BLOCK__
      load_assignment:
        cluster_name: otel_collector_grpc_cluster
        endpoints:
          - lb_endpoints:
              - endpoint:
                  address:
                    socket_address:
                      address: __OTEL_GRPC_HOST__
                      port_value: __OTEL_GRPC_PORT__
```

Notes:

- Both use `logical_dns` with `V4_ONLY` since the collector runs as a
  container alongside Envoy (Aspire-assigned hostnames resolve to
  IPv4 addresses).
- The gRPC cluster uses `http2_protocol_options` for h2c upstream.
- `__OTEL_GRPC_TLS_BLOCK__` is a whole-line placeholder replaced by
  `entrypoint.sh` with either a TLS transport socket (publish mode,
  port 443) or nothing (dev mode). This handles the difference between
  local h2c and cloud TLS gRPC ingress.

## 3f. Update `proxy/entrypoint.sh`

Add the new required env vars to the validation block:

```bash
require_env OTEL_INSTANCE_ID
require_env OTEL_HTTP_HOST
require_env OTEL_HTTP_PORT
require_env OTEL_GRPC_HOST
require_env OTEL_GRPC_PORT
```

Add the TLS block generation after the CORS fragment composition and
before the final `sed` substitution:

```bash
# ACA internal OTLP gRPC ingress is TLS on :443; local Aspire uses cleartext h2c on the OTLP port.
OTEL_GRPC_TLS_BLOCK_FILE=/tmp/otel_grpc_tls_block.yaml
if [ "$OTEL_GRPC_PORT" = "443" ]; then
  cat > "$OTEL_GRPC_TLS_BLOCK_FILE" <<EOF
      transport_socket:
        name: envoy.transport_sockets.tls
        typed_config:
          "@type": type.googleapis.com/envoy.extensions.transport_sockets.tls.v3.UpstreamTlsContext
          sni: ${OTEL_GRPC_HOST}
          common_tls_context:
            alpn_protocols: [ "h2" ]
            validation_context:
              trust_chain_verification: ACCEPT_UNTRUSTED
EOF
else
  : > "$OTEL_GRPC_TLS_BLOCK_FILE"
fi

sed -e "/^__OTEL_GRPC_TLS_BLOCK__$/r ${OTEL_GRPC_TLS_BLOCK_FILE}" \
    -e "/^__OTEL_GRPC_TLS_BLOCK__$/d" \
    /tmp/envoy.yaml.tmpl > /tmp/envoy.yaml.tmpl2
mv /tmp/envoy.yaml.tmpl2 /tmp/envoy.yaml.tmpl
```

When `OTEL_GRPC_PORT` is `443` (publish mode behind cloud TLS
ingress), the block injects an `UpstreamTlsContext` with SNI and h2
ALPN. In dev mode (any other port), the file is empty and the
placeholder line is simply removed.

Add the new OTel substitutions to the final `sed` command:

```bash
  -e "s|__OTEL_INSTANCE_ID__|${OTEL_INSTANCE_ID}|g" \
  -e "s|__OTEL_HTTP_HOST__|${OTEL_HTTP_HOST}|g" \
  -e "s|__OTEL_HTTP_PORT__|${OTEL_HTTP_PORT}|g" \
  -e "s|__OTEL_GRPC_HOST__|${OTEL_GRPC_HOST}|g" \
  -e "s|__OTEL_GRPC_PORT__|${OTEL_GRPC_PORT}|g" \
```

## 3g. Add `WithOtelCollectorEndpoints` to `EnvoyProxyResourceBuilderExtensions.cs`

Add a `using` for the collector resource at the top of the file:

```csharp
using «ProjectName».AppHost.OpenTelemetryCollector;
```

Add this extension method:

```csharp
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
```

The method:

- Uses `WithReference` so Aspire knows Envoy depends on the collector
  (startup ordering and service discovery).
- Injects host/port pairs for both the gRPC and HTTP endpoints —
  Envoy's stats sink, access logger, and tracer use gRPC; the
  `/otlp/v1/` passthrough route uses HTTP.
- Sets `OTEL_INSTANCE_ID` to the Envoy resource name for the
  `service.instance.id` OTel resource attribute.

## 3h. Wire in `apphost/Program.cs`

Chain `.WithOtelCollectorEndpoints(otel)` on the Envoy proxy builder
(before the `.WaitFor(...)` calls):

```csharp
var proxy = builder.AddEnvoyProxy("envoy")
    .WithOtelCollectorEndpoints(otel)
    .WaitFor(auth)
    .WaitFor(payments)
    .WaitFor(api);
```

## Environment variable summary

| Env var | Source | Used by |
|---|---|---|
| `OTEL_GRPC_HOST` | Collector gRPC endpoint host | `envoy.yaml.tmpl` (stats sink, access logger, tracer, gRPC cluster) |
| `OTEL_GRPC_PORT` | Collector gRPC endpoint port | `envoy.yaml.tmpl` (gRPC cluster), `entrypoint.sh` (TLS decision) |
| `OTEL_HTTP_HOST` | Collector HTTP endpoint host | `envoy.yaml.tmpl` (HTTP cluster) |
| `OTEL_HTTP_PORT` | Collector HTTP endpoint port | `envoy.yaml.tmpl` (HTTP cluster) |
| `OTEL_INSTANCE_ID` | Envoy resource name | `envoy.yaml.tmpl` (OTel resource attribute `service.instance.id`) |
