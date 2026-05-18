# Step 6 — Envoy proxy

Envoy ships as a custom Docker image built from the stock upstream
image. The `proxy/` directory contains a YAML template with
`__PLACEHOLDER__` tokens, an `entrypoint.sh` that validates
environment variables and performs substitution at container startup,
and CORS fragment templates that are composed into the final config.
The Aspire AppHost injects all dynamic values (cluster endpoints,
CORS origins, allowed hosts) as environment variables via extension
methods.

## 6a. `proxy/` directory structure

```
proxy/
├── Dockerfile
├── envoy.yaml.tmpl
├── entrypoint.sh
├── cors-allow-origins-exact.tmpl
└── cors-allow-origins-with-subdomain.tmpl
```

## 6b. `proxy/Dockerfile`

```dockerfile
FROM envoyproxy/envoy:v1.34-latest

COPY entrypoint.sh envoy.yaml.tmpl cors-allow-origins-exact.tmpl cors-allow-origins-with-subdomain.tmpl /etc/envoy/
RUN chmod +x /etc/envoy/entrypoint.sh
```

The image copies all templates and the entrypoint into `/etc/envoy/`.
No bind-mounts needed at runtime.

## 6c. `proxy/envoy.yaml.tmpl` — config template

```yaml
admin:
  address:
    socket_address: { address: 0.0.0.0, port_value: 9901 }

static_resources:
  listeners:
    - name: http_listener
      address:
        socket_address: { address: 0.0.0.0, port_value: 8080 }
      filter_chains:
        - filters:
            - name: envoy.filters.network.http_connection_manager
              typed_config:
                "@type": type.googleapis.com/envoy.extensions.filters.network.http_connection_manager.v3.HttpConnectionManager
                codec_type: auto
                stat_prefix: ingress_http
                use_remote_address: true
                route_config:
                  name: local_route
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
                        text_format: "[%START_TIME%] \"%REQ(:METHOD)% %REQ(X-ENVOY-ORIGINAL-PATH?:PATH)% %PROTOCOL%\" %RESPONSE_CODE% %RESPONSE_FLAGS% %DURATION%ms\n"

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

- All host and port values use `__PLACEHOLDER__` tokens substituted by
  `entrypoint.sh` from environment variables injected by the AppHost.
- `domains: __ALLOWED_HOSTS__` restricts the virtual host to the
  proxy's own published hostname (see `WithAllowedHosts` in 6f).
  `entrypoint.sh` converts the comma-separated env var to a JSON
  array (`["host1","host2"]`).
- `__CORS_ALLOW_ORIGIN_MATCHES__` is a whole-line placeholder replaced
  by a CORS fragment template (6e) at startup.
- gRPC clusters use `typed_extension_protocol_options` with
  `http2_protocol_options: {}` for h2c upstream — paired with the
  services' Kestrel HTTP/2 configuration. The admin cluster omits
  this (HTTP/1.1 to the Angular SSR server).
- `prefix_rewrite: "/"` strips the `/<service>/` prefix so the
  service sees its native gRPC path
  (`/<package>.<Service>/<Method>`).
- `max_stream_duration.grpc_timeout_header_max: 0s` disables Envoy's
  default gRPC timeout for long-running streams.
- Service cluster ports are Aspire-assigned and injected dynamically
  via `WithClusterEndpoint` (no hardcoded ports).

## 6d. `proxy/entrypoint.sh` — startup substitution

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

sed -e "/^__CORS_ALLOW_ORIGIN_MATCHES__$/r /tmp/cors-allow-origins.yaml" \
    -e "/^__CORS_ALLOW_ORIGIN_MATCHES__$/d" \
    /etc/envoy/envoy.yaml.tmpl > /tmp/envoy.yaml.tmpl

ALLOWED_HOSTS_ARRAY="[\"$(echo "$ALLOWED_HOSTS" | sed 's/,/","/g')\"]"

sed \
  -e "s|__ALLOWED_HOSTS__|${ALLOWED_HOSTS_ARRAY}|g" \
  -e "s|__ADMIN_HOST__|${ADMIN_HOST}|g" \
  -e "s|__ADMIN_PORT__|${ADMIN_PORT}|g" \
  -e "s|__AUTH_HOST__|${AUTH_HOST}|g" \
  -e "s|__AUTH_PORT__|${AUTH_PORT}|g" \
  -e "s|__PAYMENTS_HOST__|${PAYMENTS_HOST}|g" \
  -e "s|__PAYMENTS_PORT__|${PAYMENTS_PORT}|g" \
  -e "s|__API_HOST__|${API_HOST}|g" \
  -e "s|__API_PORT__|${API_PORT}|g" \
  /tmp/envoy.yaml.tmpl > /tmp/envoy.yaml

echo "----- /tmp/envoy.yaml (full generated config) -----"
cat /tmp/envoy.yaml
echo "----- end /tmp/envoy.yaml -----"

exec envoy -c /tmp/envoy.yaml "$@"
```

The script:

1. Validates all required environment variables at startup (fail-fast).
2. Selects the CORS fragment template based on whether
   `CORS_ORIGIN_SUBDOMAIN_REGEX` is set. When present and containing
   `*` wildcards (e.g. `http://*.localhost:<port>`), the wildcard is
   converted to a safe regex character class.
3. Composes the CORS fragment into the main template by replacing the
   `__CORS_ALLOW_ORIGIN_MATCHES__` placeholder line.
4. Converts comma-separated `ALLOWED_HOSTS` to a JSON array for the
   Envoy `domains` field.
5. Substitutes all remaining `__PLACEHOLDER__` tokens.
6. Dumps the final config to stdout for debugging.
7. `exec envoy` replaces the shell process.

## 6e. CORS fragment templates

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
                                google_re2:
                                  max_program_size: 512
                                regex: "__CORS_ORIGIN_SUBDOMAIN_REGEX__"
```

These fragments are spliced into the CORS policy block of
`envoy.yaml.tmpl` by `entrypoint.sh`. The indentation must match the
surrounding YAML context exactly.

## 6f. `apphost/EnvoyProxy/EnvoyProxyResourceBuilderExtensions.cs`

Create extension methods that encapsulate Envoy registration and
environment variable injection:

```csharp
using Microsoft.Extensions.Hosting;

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
            .WithHttpEndpoint(targetPort: 8080)
            .WithEntrypoint("/bin/sh")
            .WithArgs("/etc/envoy/entrypoint.sh");

        if (builder.ExecutionContext.IsPublishMode)
        {
            envoy.WithEndpoint("http", e => e.IsExternal = true);
        }
        else
        {
            envoy = envoy.WithContainerRuntimeArgs("--add-host=host.docker.internal:host-gateway");
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
            var clientScheme = "https";
            return envoy.WithEnvironment("CORS_ORIGIN_EXACT",
                ReferenceExpression.Create($"{clientScheme}://{clientHost}"));
        }

        return envoy.WithEnvironment("CORS_ORIGIN_EXACT", clientEndpoint);
    }

    public static IResourceBuilder<ContainerResource> WithClusterEndpoint(
        this IResourceBuilder<ContainerResource> envoy,
        IDistributedApplicationBuilder applicationBuilder,
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
    /// ACA terminates TLS externally; the browser's Host header has no port, so HostAndPort (which
    /// includes the internal target port :80) would prevent Envoy's virtual-host domain matching.
    /// </remarks>
    public static IResourceBuilder<ContainerResource> WithAllowedHosts(
        this IResourceBuilder<ContainerResource> envoy,
        IDistributedApplicationBuilder applicationBuilder)
    {
        var endpointProperty = applicationBuilder.ExecutionContext.IsPublishMode
            ? EndpointProperty.Host
            : EndpointProperty.HostAndPort;

        var network = applicationBuilder.ExecutionContext.IsPublishMode
            ? KnownNetworkIdentifiers.PublicInternet
            : KnownNetworkIdentifiers.LocalhostNetwork;

        return envoy.WithEnvironment("ALLOWED_HOSTS",
            envoy.GetEndpoint("http", network).Property(endpointProperty));
    }
}
```

Notes:

- `AddDockerfile` builds the `proxy/` directory as a Docker image
  instead of using a stock image with bind-mounts.
- `WithEntrypoint` + `WithArgs` overrides the Envoy image's default
  entrypoint to run `entrypoint.sh`.
- Dev mode adds `--add-host=host.docker.internal:host-gateway` so
  clusters can reach host processes (Appendix A).
- Publish mode marks the HTTP endpoint as external for cloud ingress
  (e.g. Azure Container Apps).
- The admin endpoint (`9901`) is `isProxied: false` so Aspire doesn't
  add a proxy layer, and gets a health check on `/ready`.
- `WithCorsOriginExact` handles the publish-mode HTTPS scheme override
  (cloud ingress terminates TLS, so the actual client origin uses
  HTTPS even though the client's internal endpoint is HTTP).
- `WithClusterEndpoint` is generic — it injects `{NAME}_HOST` and
  `{NAME}_PORT` for any upstream cluster. The `name` argument must
  match the `__NAME__` prefix in `envoy.yaml.tmpl`.
- `WithCorsOriginSubdomainRegex` is only active in dev mode; in
  publish mode it's a no-op (subdomain CORS is not yet supported in
  cloud deployments).
- `WithAllowedHosts` uses `HostAndPort` in dev (includes port) but
  `Host` only in publish (cloud ingress strips the port from the Host
  header).

## 6g. Register Envoy in `apphost/Program.cs`

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
    .WithClusterEndpoint(builder, "ADMIN", adminEndpoint)
    .WithClusterEndpoint(builder, "AUTH", auth.GetEndpoint("http"))
    .WithClusterEndpoint(builder, "PAYMENTS", payments.GetEndpoint("http"))
    .WithClusterEndpoint(builder, "API", api.GetEndpoint("http"));
```

Notes:

- `adminEndpoint` is the `EndpointReference` captured from the admin
  registration in Step 4f.
- `WaitFor` ensures services are healthy before Envoy starts
  receiving traffic.
- Cluster names in `WithClusterEndpoint` must match the `__NAME__`
  prefixes in `envoy.yaml.tmpl` (e.g. `"AUTH"` → `__AUTH_HOST__`,
  `__AUTH_PORT__`).

## 6h. Full `apphost/Program.cs` at this point

```csharp
using «ProjectName».AppHost.EnvoyProxy;

var builder = DistributedApplication.CreateBuilder(args);

var auth     = builder.AddProject<Projects.«ProjectName»_Auth>("auth");
var payments = builder.AddProject<Projects.«ProjectName»_Payments>("payments");
var api      = builder.AddProject<Projects.«ProjectName»_Api>("api");

EndpointReference adminEndpoint;
if (builder.ExecutionContext.IsPublishMode)
{
    adminEndpoint = builder.AddDockerfile("admin", "../clients/admin")
        .WithHttpEndpoint(env: "PORT")
        .WithExternalHttpEndpoints()
        .GetEndpoint("http");
}
else
{
    adminEndpoint = builder.AddJavaScriptApp("admin", "../clients/admin", "start")
        .WithHttpEndpoint(env: "PORT")
        .GetEndpoint("http");
}

var envoy = builder.AddEnvoyProxy("envoy")
    .WaitFor(auth)
    .WaitFor(payments)
    .WaitFor(api);

envoy
    .WithCorsOriginExact(builder, adminEndpoint)
    .WithCorsOriginSubdomainRegex(builder, adminEndpoint)
    .WithAllowedHosts(builder);

envoy
    .WithClusterEndpoint(builder, "ADMIN", adminEndpoint)
    .WithClusterEndpoint(builder, "AUTH", auth.GetEndpoint("http"))
    .WithClusterEndpoint(builder, "PAYMENTS", payments.GetEndpoint("http"))
    .WithClusterEndpoint(builder, "API", api.GetEndpoint("http"));

builder.Build().Run();
```
