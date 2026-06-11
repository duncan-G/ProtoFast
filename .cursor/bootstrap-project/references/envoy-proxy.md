# Step 6 — Envoy proxy

Envoy ships as a custom Docker image built from the stock upstream
image. The `proxy/` directory contains two YAML templates — a main
config (`envoy.yaml.tmpl`) with listeners, TLS, and clusters, and a
route config (`envoy.rds.yaml.tmpl`) with virtual hosts, CORS, and
routes — plus an `entrypoint.sh` that validates environment variables
and performs substitution at container startup, and CORS fragment
templates that are composed into the route config. Envoy terminates
TLS on both a TCP listener (HTTP/2 + HTTP/1.1 via ALPN) and a UDP
listener (HTTP/3 via QUIC), using Aspire's developer certificate in
dev mode. The Aspire AppHost injects all dynamic values (upstream
endpoints, CORS origins, allowed hosts, TLS cert paths) as
environment variables via extension methods.

## 6a. `proxy/` directory structure

```
proxy/
├── Dockerfile
├── envoy.yaml.tmpl           # Main config (listeners, TLS, QUIC, clusters)
├── envoy.rds.yaml.tmpl       # Route Discovery Service (virtual hosts, CORS, routes)
├── entrypoint.sh
├── cors-allow-origins-exact.tmpl
└── cors-allow-origins-with-subdomain.tmpl
```

## 6b. `proxy/Dockerfile`

```dockerfile
FROM envoyproxy/envoy:v1.34-latest

RUN mkdir -p /etc/envoy/discovery
COPY entrypoint.sh envoy.yaml.tmpl envoy.rds.yaml.tmpl \
     cors-allow-origins-exact.tmpl cors-allow-origins-with-subdomain.tmpl /etc/envoy/
RUN chmod +x /etc/envoy/entrypoint.sh
```

The image copies all templates and the entrypoint into `/etc/envoy/`.
The `discovery/` directory is where `entrypoint.sh` writes the
processed RDS config file — Envoy loads it via `path_config_source`.
No bind-mounts needed at runtime.

## 6c. `proxy/envoy.rds.yaml.tmpl` — route config template

Routes, virtual hosts, and CORS policy live in a separate RDS file.
Both listeners (TCP and QUIC) reference this via `path_config_source`.

```yaml
resources:
- "@type": type.googleapis.com/envoy.config.route.v3.RouteConfiguration
  name: service_routes
  virtual_hosts:
    - name: «projectname»
      domains: __ALLOWED_HOSTS__
      typed_per_filter_config:
        envoy.filters.http.cors:
          "@type": type.googleapis.com/envoy.extensions.filters.http.cors.v3.CorsPolicy
__CORS_ALLOW_ORIGIN_MATCHES__
          allow_methods: "GET,POST,PUT,DELETE,OPTIONS"
          allow_headers: "keep-alive,user-agent,cache-control,content-type,content-transfer-encoding,authorization,x-accept-content-transfer-encoding,x-accept-response-streaming,x-user-agent,x-grpc-web,grpc-timeout"
          expose_headers: "grpc-status,grpc-message"
          max_age: "86400"
          allow_credentials: true
      response_headers_to_add:
        - header:
            key: "alt-svc"
            value: 'h3=":__PORT__"; ma=86400'
      routes:
        - match: { prefix: "/auth/" }
          route:
            cluster: auth
            prefix_rewrite: "/"
            timeout: 0s
            max_stream_duration:
              grpc_timeout_header_max: 0s
        - match: { prefix: "/payments/" }
          route:
            cluster: payments
            prefix_rewrite: "/"
            timeout: 0s
            max_stream_duration:
              grpc_timeout_header_max: 0s
        - match: { prefix: "/api/" }
          route:
            cluster: api
            prefix_rewrite: "/"
            timeout: 0s
            max_stream_duration:
              grpc_timeout_header_max: 0s
        - match: { prefix: "/" }
          route: { cluster: admin }
```

RDS notes:

- `domains: __ALLOWED_HOSTS__` restricts the virtual host to the
  proxy's own published hostname (see `WithAllowedHosts` in 6f).
  `entrypoint.sh` converts the comma-separated env var to a JSON
  array (`["host1","host2"]`).
- `__CORS_ALLOW_ORIGIN_MATCHES__` is a whole-line placeholder replaced
  by a CORS fragment template (6e) at startup.
- `response_headers_to_add` with `alt-svc` advertises HTTP/3 support
  to browsers so they upgrade from HTTP/2 on subsequent requests.
  `__PORT__` is substituted by `entrypoint.sh`.
- gRPC routes use `prefix_rewrite: "/"` to strip the `/<service>/`
  prefix so the backend sees native gRPC paths
  (`/<package>.<Service>/<Method>`).
- `max_stream_duration.grpc_timeout_header_max: 0s` disables Envoy's
  default gRPC timeout for long-running streams.

## 6d. `proxy/envoy.yaml.tmpl` — main config template

The main config defines the admin interface, two listeners (TCP with
TLS and UDP with QUIC on the same port), and clusters. Both listeners
use RDS to load the route config from the processed file at
`/etc/envoy/discovery/envoy.rds.yaml`.

```yaml
admin:
  address:
    socket_address: { address: 0.0.0.0, port_value: 9901 }

static_resources:
  listeners:
    - name: http_listener
      address:
        socket_address: { address: 0.0.0.0, port_value: __PORT__ }
      filter_chains:
        - filters:
            - name: envoy.filters.network.http_connection_manager
              typed_config:
                "@type": type.googleapis.com/envoy.extensions.filters.network.http_connection_manager.v3.HttpConnectionManager
                codec_type: auto
                stat_prefix: ingress_http
                use_remote_address: true
                scheme_header_transformation:
                  scheme_to_overwrite: http
                rds:
                  config_source:
                    path_config_source:
                      path: /etc/envoy/discovery/envoy.rds.yaml
                  route_config_name: service_routes
                http_filters:
                  - name: envoy.filters.http.cors
                    typed_config:
                      "@type": type.googleapis.com/envoy.extensions.filters.http.cors.v3.Cors
                  - name: envoy.filters.http.grpc_web
                    typed_config:
                      "@type": type.googleapis.com/envoy.extensions.filters.http.grpc_web.v3.GrpcWeb
                  - name: envoy.filters.http.router
                    typed_config:
                      "@type": type.googleapis.com/envoy.extensions.filters.http.router.v3.Router
                access_log:
                  - name: envoy.access_loggers.file
                    typed_config:
                      "@type": type.googleapis.com/envoy.extensions.access_loggers.file.v3.FileAccessLog
                      path: /dev/stdout
                      log_format:
                        text_format_source:
                          inline_string: "[%START_TIME%] \"%REQ(:METHOD)% %REQ(X-ENVOY-ORIGINAL-PATH?:PATH)% %PROTOCOL%\" %RESPONSE_CODE% %RESPONSE_FLAGS% %DURATION%ms\n"
          transport_socket:
            name: envoy.transport_sockets.tls
            typed_config:
              "@type": type.googleapis.com/envoy.extensions.transport_sockets.tls.v3.DownstreamTlsContext
              common_tls_context:
                alpn_protocols: ["h2", "http/1.1"]
                tls_certificates:
                  - certificate_chain: { filename: __ENVOY_TLS_CERT__ }
                    private_key: { filename: __ENVOY_TLS_KEY__ }

    - name: quic_listener
      address:
        socket_address: { protocol: UDP, address: 0.0.0.0, port_value: __PORT__ }
      udp_listener_config:
        quic_options: {}
      filter_chains:
        - filters:
            - name: envoy.filters.network.http_connection_manager
              typed_config:
                "@type": type.googleapis.com/envoy.extensions.filters.network.http_connection_manager.v3.HttpConnectionManager
                codec_type: HTTP3
                stat_prefix: ingress_http_3
                use_remote_address: true
                scheme_header_transformation:
                  scheme_to_overwrite: http
                rds:
                  config_source:
                    path_config_source:
                      path: /etc/envoy/discovery/envoy.rds.yaml
                  route_config_name: service_routes
                http_filters:
                  - name: envoy.filters.http.cors
                    typed_config:
                      "@type": type.googleapis.com/envoy.extensions.filters.http.cors.v3.Cors
                  - name: envoy.filters.http.grpc_web
                    typed_config:
                      "@type": type.googleapis.com/envoy.extensions.filters.http.grpc_web.v3.GrpcWeb
                  - name: envoy.filters.http.router
                    typed_config:
                      "@type": type.googleapis.com/envoy.extensions.filters.http.router.v3.Router
                access_log:
                  - name: envoy.access_loggers.file
                    typed_config:
                      "@type": type.googleapis.com/envoy.extensions.access_loggers.file.v3.FileAccessLog
                      path: /dev/stdout
                      log_format:
                        text_format_source:
                          inline_string: "[%START_TIME%] \"%REQ(:METHOD)% %REQ(X-ENVOY-ORIGINAL-PATH?:PATH)% %PROTOCOL%\" %RESPONSE_CODE% %RESPONSE_FLAGS% %DURATION%ms\n"
          transport_socket:
            name: envoy.transport_sockets.quic
            typed_config:
              "@type": type.googleapis.com/envoy.extensions.transport_sockets.quic.v3.QuicDownstreamTransport
              downstream_tls_context:
                common_tls_context:
                  alpn_protocols: ["h3"]
                  tls_certificates:
                    - certificate_chain: { filename: __ENVOY_TLS_CERT__ }
                      private_key: { filename: __ENVOY_TLS_KEY__ }

  clusters:
    - name: admin
      type: STRICT_DNS
      lb_policy: ROUND_ROBIN
      load_assignment:
        cluster_name: admin
        endpoints:
          - lb_endpoints:
              - endpoint:
                  address:
                    socket_address:
                      address: __ADMIN_HOST__
                      port_value: __ADMIN_PORT__
    - name: auth
      type: STRICT_DNS
      lb_policy: ROUND_ROBIN
      typed_extension_protocol_options:
        envoy.extensions.upstreams.http.v3.HttpProtocolOptions:
          "@type": type.googleapis.com/envoy.extensions.upstreams.http.v3.HttpProtocolOptions
          explicit_http_config:
            http2_protocol_options: {}
      load_assignment:
        cluster_name: auth
        endpoints:
          - lb_endpoints:
              - endpoint:
                  address:
                    socket_address:
                      address: __AUTH_HOST__
                      port_value: __AUTH_PORT__
    - name: payments
      type: STRICT_DNS
      lb_policy: ROUND_ROBIN
      typed_extension_protocol_options:
        envoy.extensions.upstreams.http.v3.HttpProtocolOptions:
          "@type": type.googleapis.com/envoy.extensions.upstreams.http.v3.HttpProtocolOptions
          explicit_http_config:
            http2_protocol_options: {}
      load_assignment:
        cluster_name: payments
        endpoints:
          - lb_endpoints:
              - endpoint:
                  address:
                    socket_address:
                      address: __PAYMENTS_HOST__
                      port_value: __PAYMENTS_PORT__
    - name: api
      type: STRICT_DNS
      lb_policy: ROUND_ROBIN
      typed_extension_protocol_options:
        envoy.extensions.upstreams.http.v3.HttpProtocolOptions:
          "@type": type.googleapis.com/envoy.extensions.upstreams.http.v3.HttpProtocolOptions
          explicit_http_config:
            http2_protocol_options: {}
      load_assignment:
        cluster_name: api
        endpoints:
          - lb_endpoints:
              - endpoint:
                  address:
                    socket_address:
                      address: __API_HOST__
                      port_value: __API_PORT__
```

YAML notes:

- **Two listeners on the same port.** `http_listener` (TCP) negotiates
  HTTP/2 or HTTP/1.1 via TLS ALPN. `quic_listener` (UDP) serves
  HTTP/3. Browsers discover HTTP/3 via the `alt-svc` response header
  in the RDS config and upgrade on subsequent requests.
- **`scheme_header_transformation: scheme_to_overwrite: http`** on both
  listeners ensures upstream services see `http` scheme in the
  `:scheme` pseudo-header, matching their cleartext listeners. Without
  this, Envoy passes `https` which can confuse backend routing.
- **`transport_socket`** on the TCP listener uses
  `DownstreamTlsContext` with ALPN `["h2", "http/1.1"]`. On the QUIC
  listener it uses `QuicDownstreamTransport` with ALPN `["h3"]`. Both
  reference `__ENVOY_TLS_CERT__` and `__ENVOY_TLS_KEY__` tokens.
- **RDS** (`rds.config_source.path_config_source`) loads routes from
  `/etc/envoy/discovery/envoy.rds.yaml`, written by `entrypoint.sh`.
  Both listeners share the same route config.
- All host and port values use `__PLACEHOLDER__` tokens substituted by
  `entrypoint.sh` from environment variables injected by the AppHost.
- gRPC clusters use `typed_extension_protocol_options` with
  `http2_protocol_options: {}` for h2c upstream — paired with the
  services' Kestrel HTTP/2 configuration. The admin cluster omits
  this (HTTP/1.1 to the Angular SSR server).
- Service cluster ports are Aspire-assigned and injected dynamically
  via `WithUpstreamEndpoint` (no hardcoded ports).

## 6e. `proxy/entrypoint.sh` — startup substitution

```bash
#!/bin/sh
set -e

require_env() {
  VAR_NAME="$1"
  eval "VAR_VALUE=\$${VAR_NAME}"
  if [ -z "$VAR_VALUE" ]; then
    echo "Error: Environment variable '$VAR_NAME' is not set." >&2
    exit 1
  fi
}

require_env ALLOWED_HOSTS
require_env CORS_ORIGIN_EXACT
require_env ADMIN_HOST
require_env ADMIN_PORT
require_env AUTH_HOST
require_env AUTH_PORT
require_env PAYMENTS_HOST
require_env PAYMENTS_PORT
require_env API_HOST
require_env API_PORT
require_env ENVOY_TLS_CERT
require_env ENVOY_TLS_KEY

if [ -n "$CORS_ORIGIN_SUBDOMAIN_REGEX" ]; then
  CORS_FRAGMENT_TMPL=/etc/envoy/cors-allow-origins-with-subdomain.tmpl
  case "$CORS_ORIGIN_SUBDOMAIN_REGEX" in
    *"*"*) CORS_ORIGIN_SUBDOMAIN_REGEX="^$(echo "$CORS_ORIGIN_SUBDOMAIN_REGEX" | sed 's/\./\\./g;s/\*/[a-zA-Z0-9.-]+/g')\$" ;;
  esac
else
  CORS_FRAGMENT_TMPL=/etc/envoy/cors-allow-origins-exact.tmpl
fi

sed \
  -e "s|__CORS_ORIGIN_EXACT__|${CORS_ORIGIN_EXACT}|g" \
  -e "s|__CORS_ORIGIN_SUBDOMAIN_REGEX__|${CORS_ORIGIN_SUBDOMAIN_REGEX}|g" \
  "$CORS_FRAGMENT_TMPL" > /tmp/cors-allow-origins.yaml

# --- Process RDS template ---
sed -e "/^__CORS_ALLOW_ORIGIN_MATCHES__$/r /tmp/cors-allow-origins.yaml" \
    -e "/^__CORS_ALLOW_ORIGIN_MATCHES__$/d" \
    /etc/envoy/envoy.rds.yaml.tmpl > /tmp/envoy.rds.yaml.tmpl

ALLOWED_HOSTS_ARRAY="[\"$(echo "$ALLOWED_HOSTS" | sed 's/,/","/g')\"]"

sed \
  -e "s|__PORT__|${PORT}|g" \
  -e "s|__ALLOWED_HOSTS__|${ALLOWED_HOSTS_ARRAY}|g" \
  /tmp/envoy.rds.yaml.tmpl > /etc/envoy/discovery/envoy.rds.yaml

# --- Process main envoy template ---
sed \
  -e "s|__PORT__|${PORT}|g" \
  -e "s|__ADMIN_HOST__|${ADMIN_HOST}|g" \
  -e "s|__ADMIN_PORT__|${ADMIN_PORT}|g" \
  -e "s|__AUTH_HOST__|${AUTH_HOST}|g" \
  -e "s|__AUTH_PORT__|${AUTH_PORT}|g" \
  -e "s|__PAYMENTS_HOST__|${PAYMENTS_HOST}|g" \
  -e "s|__PAYMENTS_PORT__|${PAYMENTS_PORT}|g" \
  -e "s|__API_HOST__|${API_HOST}|g" \
  -e "s|__API_PORT__|${API_PORT}|g" \
  -e "s|__ENVOY_TLS_CERT__|${ENVOY_TLS_CERT}|g" \
  -e "s|__ENVOY_TLS_KEY__|${ENVOY_TLS_KEY}|g" \
  /etc/envoy/envoy.yaml.tmpl > /tmp/envoy.yaml

echo "----- /etc/envoy/discovery/envoy.rds.yaml (route config) -----"
cat /etc/envoy/discovery/envoy.rds.yaml
echo "----- end envoy.rds.yaml -----"

echo "----- /tmp/envoy.yaml (full generated config) -----"
cat /tmp/envoy.yaml
echo "----- end /tmp/envoy.yaml -----"

exec envoy -c /tmp/envoy.yaml "$@"
```

The script:

1. Validates all required environment variables at startup (fail-fast),
   including `ENVOY_TLS_CERT` and `ENVOY_TLS_KEY` for TLS.
2. Selects the CORS fragment template based on whether
   `CORS_ORIGIN_SUBDOMAIN_REGEX` is set. When present and containing
   `*` wildcards (e.g. `https://*.localhost:<port>`), the wildcard is
   converted to a safe regex character class.
3. **Processes the RDS template first**: composes the CORS fragment,
   then substitutes `ALLOWED_HOSTS` and `PORT`. Output goes to
   `/etc/envoy/discovery/envoy.rds.yaml` (loaded by Envoy via
   `path_config_source`).
4. **Processes the main template**: substitutes all remaining
   `__PLACEHOLDER__` tokens including cluster host/port pairs and TLS
   cert paths.
5. Dumps both configs to stdout for debugging.
6. `exec envoy` replaces the shell process.

## 6f. CORS fragment templates

`proxy/cors-allow-origins-exact.tmpl` — used when only exact origin
matching is needed:

```yaml
          allow_origin_string_match:
            - exact: "__CORS_ORIGIN_EXACT__"
```

`proxy/cors-allow-origins-with-subdomain.tmpl` — used when
`CORS_ORIGIN_SUBDOMAIN_REGEX` is set (dev mode with subdomain
support):

```yaml
          allow_origin_string_match:
            - exact: "__CORS_ORIGIN_EXACT__"
            - safe_regex:
                regex: "__CORS_ORIGIN_SUBDOMAIN_REGEX__"
```

These fragments are spliced into the CORS policy block of
`envoy.rds.yaml.tmpl` by `entrypoint.sh`. The indentation must match
the surrounding YAML context exactly.

## 6g. `apphost/EnvoyProxy/EnvoyProxyResourceBuilderExtensions.cs`

Create extension methods that encapsulate Envoy registration and
environment variable injection:

```csharp
namespace «ProjectName».AppHost.EnvoyProxy;

public static class EnvoyProxyResourceBuilderExtensions
{
    private const string EnvoyConfigPath = "../proxy";

    public static IResourceBuilder<ContainerResource> AddEnvoyProxy(
        this IDistributedApplicationBuilder builder,
        string name)
    {
        var envoy = builder
            .AddDockerfile(name, EnvoyConfigPath)
            .WithHttpsEndpoint(targetPort: 20000, env: "PORT", isProxied: false)
            .WithEntrypoint("/bin/sh")
            .WithArgs("/etc/envoy/entrypoint.sh");

        if (builder.ExecutionContext.IsPublishMode)
        {
            envoy.WithEndpoint("https", e => e.IsExternal = true);
        }
        else
        {
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
            return envoy.WithEnvironment("CORS_ORIGIN_EXACT",
                ReferenceExpression.Create($"https://{clientHost}"));
        }

        return envoy.WithEnvironment("CORS_ORIGIN_EXACT", clientEndpoint);
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
    /// In publish mode the external host typically terminates TLS, so the browser's Host header
    /// has no port — we use <see cref="EndpointProperty.Host"/> on
    /// <see cref="KnownNetworkIdentifiers.PublicInternet"/>.
    /// In dev mode the Aspire dev-cert hostname (<c>*.aspire.dev.internal</c>) may differ from
    /// <c>localhost</c>, so we use a wildcard to match any Host header — the CORS policy still
    /// restricts origins.
    /// </remarks>
    public static IResourceBuilder<ContainerResource> WithAllowedHosts(
        this IResourceBuilder<ContainerResource> envoy,
        IDistributedApplicationBuilder applicationBuilder)
    {
        if (applicationBuilder.ExecutionContext.IsPublishMode)
        {
            return envoy.WithEnvironment("ALLOWED_HOSTS",
                envoy.GetEndpoint("https", KnownNetworkIdentifiers.PublicInternet)
                    .Property(EndpointProperty.Host));
        }

        return envoy.WithEnvironment("ALLOWED_HOSTS", "*");
    }
}
```

Notes:

- `AddDockerfile` builds the `proxy/` directory as a Docker image
  instead of using a stock image with bind-mounts.
- **`WithHttpsEndpoint`** registers Envoy's main listener as HTTPS on
  a fixed `targetPort` of `20000` (the internal container port).
  `isProxied: false` prevents Aspire from adding a proxy layer. The
  `env: "PORT"` parameter injects the external port as an env var.
- `WithEntrypoint` + `WithArgs` overrides the Envoy image's default
  entrypoint to run `entrypoint.sh`.
- **Dev mode** uses `WithHttpsCertificateConfiguration` to inject
  Aspire's developer certificate paths as `ENVOY_TLS_CERT` and
  `ENVOY_TLS_KEY` environment variables. Also adds
  `--add-host=host.docker.internal:host-gateway` so clusters can
  reach host processes (Appendix A).
- **Publish mode** marks the HTTPS endpoint as external for cloud
  ingress (e.g. Azure Container Apps).
- The admin endpoint (`9901`) uses plain HTTP (`isProxied: false`) and
  gets a health check on `/ready`.
- `WithCorsOriginExact` hardcodes `https://` in publish mode (cloud
  ingress terminates TLS). In dev mode it passes the full endpoint
  reference (which resolves to the client's HTTPS URL).
- **`WithUpstreamEndpoint`** is generic — it injects `{NAME}_HOST` and
  `{NAME}_PORT` for any upstream cluster. The `name` argument must
  match the `__NAME__` prefix in `envoy.yaml.tmpl`.
- `WithCorsOriginSubdomainRegex` is only active in dev mode; in
  publish mode it's a no-op.
- **`WithAllowedHosts`** uses `"*"` wildcard in dev mode (the Aspire
  dev-cert hostname like `*.aspire.dev.internal` may differ from
  `localhost`, so a wildcard avoids virtual-host mismatch — the CORS
  policy still restricts origins). In publish mode it uses
  `GetEndpoint("https", PublicInternet).Property(Host)`.

## 6h. Register Envoy in `apphost/Program.cs`

```csharp
using «ProjectName».AppHost.EnvoyProxy;

// ... after service and admin registrations ...

var envoy = builder.AddEnvoyProxy("envoy")
    .WaitFor(auth)
    .WaitFor(payments)
    .WaitFor(api);

envoy
    .WithCorsOriginExact(builder, adminEndpoint)
    .WithCorsOriginSubdomainRegex(builder, adminEndpoint)
    .WithAllowedHosts(builder);

envoy
    .WithUpstreamEndpoint("ADMIN", adminEndpoint)
    .WithUpstreamEndpoint("AUTH", auth.GetEndpoint("http"))
    .WithUpstreamEndpoint("PAYMENTS", payments.GetEndpoint("http"))
    .WithUpstreamEndpoint("API", api.GetEndpoint("http"));
```

Notes:

- `adminEndpoint` is the `EndpointReference` captured from the admin
  registration in Step 4f.
- `WaitFor` ensures services are healthy before Envoy starts
  receiving traffic.
- Upstream names in `WithUpstreamEndpoint` must match the `__NAME__`
  prefixes in `envoy.yaml.tmpl` (e.g. `"AUTH"` → `__AUTH_HOST__`,
  `__AUTH_PORT__`).
- Backend services still expose `http` endpoints — Envoy terminates
  TLS at the edge and uses cleartext upstream connections.

## 6i. Full `apphost/Program.cs` at this point

```csharp
using «ProjectName».AppHost.ClientApp;
using «ProjectName».AppHost.EnvoyProxy;

var builder = DistributedApplication.CreateBuilder(args);

var auth     = builder.AddProject<Projects.«ProjectName»_Auth>("auth");
var payments = builder.AddProject<Projects.«ProjectName»_Payments>("payments");
var api      = builder.AddProject<Projects.«ProjectName»_Api>("api");

var envoy = builder.AddEnvoyProxy("envoy")
    .WaitFor(auth)
    .WaitFor(payments)
    .WaitFor(api);

var adminEndpoint = builder.AddClientApp(
    "admin", "../clients/admin", 4000, envoy.GetEndpoint("https"));

envoy
    .WithCorsOriginExact(builder, adminEndpoint)
    .WithCorsOriginSubdomainRegex(builder, adminEndpoint)
    .WithAllowedHosts(builder);

envoy
    .WithUpstreamEndpoint("ADMIN", adminEndpoint)
    .WithUpstreamEndpoint("AUTH", auth.GetEndpoint("http"))
    .WithUpstreamEndpoint("PAYMENTS", payments.GetEndpoint("http"))
    .WithUpstreamEndpoint("API", api.GetEndpoint("http"));

builder.Build().Run();
```

Key change from the pre-TLS version: `envoy.GetEndpoint("https")`
is passed as the `serverEndpoint` to `AddClientApp`, so the Angular
SSR server knows to talk to Envoy over HTTPS. Backend services still
use `GetEndpoint("http")` since Envoy talks to them over cleartext.
