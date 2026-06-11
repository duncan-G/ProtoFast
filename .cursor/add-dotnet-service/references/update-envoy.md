# Update Envoy for a new gRPC service

When `proxy/envoy.rds.yaml.tmpl` exists, follow these steps to wire a
new .NET gRPC service into the Envoy proxy.

Placeholders: `«servicename»` (lowercase), `«SERVICENAME»` (UPPERCASE).

---

## 1. Add route to `proxy/envoy.rds.yaml.tmpl`

Insert a new route in the `routes:` list **before** the catch-all
`- match: { prefix: "/" }` entry:

```yaml
        - match: { prefix: "/«servicename»/" }
          route:
            cluster: «servicename»
            prefix_rewrite: "/"
            timeout: 0s
            max_stream_duration:
              grpc_timeout_header_max: 0s
```

- `prefix_rewrite: "/"` strips the `/{service}/` prefix so the backend
  sees native gRPC paths (`/<package>.<Service>/<Method>`).
- `grpc_timeout_header_max: 0s` disables Envoy's default gRPC timeout.

## 2. Add cluster to `proxy/envoy.yaml.tmpl`

Append to the `clusters:` list. gRPC services require
`http2_protocol_options` for h2c upstream:

```yaml
    - name: «servicename»
      type: STRICT_DNS
      lb_policy: ROUND_ROBIN
      typed_extension_protocol_options:
        envoy.extensions.upstreams.http.v3.HttpProtocolOptions:
          "@type": type.googleapis.com/envoy.extensions.upstreams.http.v3.HttpProtocolOptions
          explicit_http_config:
            http2_protocol_options: {}
      load_assignment:
        cluster_name: «servicename»
        endpoints:
          - lb_endpoints:
              - endpoint:
                  address:
                    socket_address:
                      address: __«SERVICENAME»_HOST__
                      port_value: __«SERVICENAME»_PORT__
```

## 3. Update `proxy/entrypoint.sh`

Add `require_env` calls alongside the existing ones:

```bash
require_env «SERVICENAME»_HOST
require_env «SERVICENAME»_PORT
```

Add `sed` substitution lines in the main envoy template `sed` command
block (the one that writes `/tmp/envoy.yaml`):

```bash
  -e "s|__«SERVICENAME»_HOST__|${«SERVICENAME»_HOST}|g" \
  -e "s|__«SERVICENAME»_PORT__|${«SERVICENAME»_PORT}|g" \
```

## 4. Update Envoy wiring in `apphost/Program.cs`

Add `.WaitFor()` to the existing envoy builder chain:

```csharp
proxy.WaitFor(«servicename»);
```

Add `.WithUpstreamEndpoint()` alongside existing upstream endpoint
calls:

```csharp
proxy.WithUpstreamEndpoint("«SERVICENAME»", «servicename».GetEndpoint("http"));
```

Backend services still expose cleartext `http` endpoints — Envoy
terminates TLS at the edge.

## 5. Rebuild and verify

```bash
dotnet build apphost
```

If the project was already running, restart with `aspire stop` then
`aspire start` (or `aspire run`).
