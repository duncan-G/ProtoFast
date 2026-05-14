# Step 6 — Envoy proxy

Envoy ships as a stock upstream image with a static config file
bind-mounted in. Ports are fixed and aligned with the services'
`launchSettings.json` (principle 4).

## 6a. `proxy/envoy.yaml` — static config

```yaml
admin:
  address:
    socket_address: { address: 0.0.0.0, port_value: 9901 }

static_resources:
  listeners:
    - name: listener_0
      address:
        socket_address: { address: 0.0.0.0, port_value: 10000 }
      filter_chains:
        - filters:
            - name: envoy.filters.network.http_connection_manager
              typed_config:
                "@type": type.googleapis.com/envoy.extensions.filters.network.http_connection_manager.v3.HttpConnectionManager
                stat_prefix: ingress_http
                route_config:
                  name: local_route
                  virtual_hosts:
                    - name: «projectname»
                      domains: ["*"]
                      routes:
                        - match: { prefix: "/auth/" }
                          route: { cluster: auth, prefix_rewrite: "/" }
                        - match: { prefix: "/payments/" }
                          route: { cluster: payments, prefix_rewrite: "/" }
                        - match: { prefix: "/api/" }
                          route: { cluster: api, prefix_rewrite: "/" }
                        - match: { prefix: "/" }
                          route: { cluster: admin }
                http_filters:
                  - name: envoy.filters.http.grpc_web
                    typed_config:
                      "@type": type.googleapis.com/envoy.extensions.filters.http.grpc_web.v3.GrpcWeb
                  - name: envoy.filters.http.router
                    typed_config:
                      "@type": type.googleapis.com/envoy.extensions.filters.http.router.v3.Router

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
                    socket_address: { address: host.docker.internal, port_value: 4200 }
    - name: auth
      type: STRICT_DNS
      lb_policy: ROUND_ROBIN
      http2_protocol_options: {}
      load_assignment:
        cluster_name: auth
        endpoints:
          - lb_endpoints:
              - endpoint:
                  address:
                    socket_address: { address: host.docker.internal, port_value: 5001 }
    - name: payments
      type: STRICT_DNS
      lb_policy: ROUND_ROBIN
      http2_protocol_options: {}
      load_assignment:
        cluster_name: payments
        endpoints:
          - lb_endpoints:
              - endpoint:
                  address:
                    socket_address: { address: host.docker.internal, port_value: 5002 }
    - name: api
      type: STRICT_DNS
      lb_policy: ROUND_ROBIN
      http2_protocol_options: {}
      load_assignment:
        cluster_name: api
        endpoints:
          - lb_endpoints:
              - endpoint:
                  address:
                    socket_address: { address: host.docker.internal, port_value: 5003 }
```

YAML notes:

- gRPC clusters set `http2_protocol_options: {}` so Envoy speaks
  h2c upstream — paired with the services' Kestrel HTTP/2
  configuration.
- `prefix_rewrite: "/"` strips the `/<service>/` prefix so the
  service sees its native gRPC path
  (`/<package>.<Service>/<Method>`).
- Cluster ports match `launchSettings.json` (Step 3b).

## 6b. Register Envoy in `apphost/apphost.cs`

Use `AddContainer` with the stock Envoy image and bind-mount the
static config:

```csharp
builder.AddContainer("envoy", "envoyproxy/envoy", "v1.31-latest")
    .WithBindMount("../proxy/envoy.yaml", "/etc/envoy/envoy.yaml", isReadOnly: true)
    .WithContainerRuntimeArgs("--add-host=host.docker.internal:host-gateway")
    .WithHttpEndpoint(port: 8080, targetPort: 10000, name: "http")
    .WithHttpEndpoint(port: 9901, targetPort: 9901, name: "admin");
```

Notes:

- `--add-host=host.docker.internal:host-gateway` makes
  `host.docker.internal` resolve to the host's IP inside the
  container (Appendix A).
- The bind-mount path is relative to `apphost/`.

## 6c. Full `apphost/apphost.cs` at this point

```csharp
#:package Aspire.Hosting.JavaScript@13.3.2
#:sdk Aspire.AppHost.Sdk@13.3.2

#pragma warning disable ASPIRECSHARPAPPS001

var builder = DistributedApplication.CreateBuilder(args);

var auth     = builder.AddCSharpApp("auth",     "../services/auth/«ProjectName».Auth.csproj");
var payments = builder.AddCSharpApp("payments", "../services/payments/«ProjectName».Payments.csproj");
var api      = builder.AddCSharpApp("api",      "../services/api/«ProjectName».Api.csproj");

var admin = builder.AddJavaScriptApp("admin", "../clients/admin", "start");

builder.AddContainer("envoy", "envoyproxy/envoy", "v1.31-latest")
    .WithBindMount("../proxy/envoy.yaml", "/etc/envoy/envoy.yaml", isReadOnly: true)
    .WithContainerRuntimeArgs("--add-host=host.docker.internal:host-gateway")
    .WithHttpEndpoint(port: 8080, targetPort: 10000, name: "http")
    .WithHttpEndpoint(port: 9901, targetPort: 9901, name: "admin");

builder.Build().Run();
```

`#:package` and `#:sdk` versions must match what the template and
`aspire add javascript` produced — don't hand-bump.
