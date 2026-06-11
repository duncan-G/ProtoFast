# Step 6 — Envoy proxy + unified SSR host

Envoy ships as a custom Docker image built from the stock upstream
image. Its config is **generated at container startup** from fragment
templates, driven by an `ENVOY_MODE` env var:

- **`dev`** — one HTTPS listener **per client**, each on its own fixed
  internal port (20000, 20001, …). Each listener routes API prefixes
  (`/auth/`, `/payments/`, `/api/`) to the service clusters and its
  catch-all to that client's Angular dev server. The browser enters
  through the client's listener, so pages and API are same-origin.
- **`dev-host`** — same per-client listeners, but every catch-all
  routes to the **unified SSR host** container with an `x-client`
  header (local smoke test of the publish artifact).
- **`publish`** — a single listener on `PORT` with one virtual host
  per client domain (`CLIENT_«NAME»_DOMAIN`), all routing to the
  unified SSR host with `x-client` headers.

Envoy terminates TLS on a TCP listener (HTTP/2 + HTTP/1.1 via ALPN)
and a UDP listener (HTTP/3 via QUIC) per listener pair, using Aspire's
developer certificate in dev mode. The AppHost injects all dynamic
values (mode, client list, upstream endpoints, TLS cert paths) as
environment variables via extension methods.

The **unified SSR host** (`clients/host/`) is a small Express
dispatcher that serves every client's built SSR bundle from one Node
process, selecting the bundle by the `x-client` header (section 6i).

## 6a. `proxy/` directory structure

```
proxy/
├── Dockerfile
├── envoy.yaml.tmpl           # Base config (admin, clusters, insertion markers)
├── envoy.listener.yaml.tmpl  # One TCP+QUIC listener pair (per client in dev)
├── envoy.rds.yaml.tmpl       # Route-config shell (name + virtual-hosts marker)
├── envoy.vhost.yaml.tmpl     # One virtual host (CORS, API routes, catch-all)
├── envoy.cluster.yaml.tmpl   # One client/web upstream cluster
└── entrypoint.sh             # Renders fragments per mode, launches Envoy
```

## 6b. `proxy/Dockerfile`

```dockerfile
FROM envoyproxy/envoy:v1.34-latest

RUN mkdir -p /etc/envoy/discovery
COPY entrypoint.sh envoy.yaml.tmpl envoy.listener.yaml.tmpl \
     envoy.rds.yaml.tmpl envoy.vhost.yaml.tmpl envoy.cluster.yaml.tmpl /etc/envoy/
RUN chmod +x /etc/envoy/entrypoint.sh
```

The image copies all templates and the entrypoint into `/etc/envoy/`.
The `discovery/` directory is where `entrypoint.sh` writes the
rendered RDS files — Envoy loads them via `path_config_source`. No
bind-mounts needed at runtime.

## 6c. Template conventions

Two placeholder styles, both processed by `entrypoint.sh` with `sed`:

- `__NAME__` inline tokens — substituted with env-var values
  (`s|__NAME__|value|g`).
- Whole-line markers (`__LISTENERS__`, `__CLIENT_CLUSTERS__`,
  `__VIRTUAL_HOSTS__`, `__CORS_ALLOW_ORIGIN_MATCHES__`,
  `__CLUSTER_TLS_BLOCK__`) — replaced by inserting a rendered fragment
  file (`/^__MARKER__$/r file` + `/^__MARKER__$/d`).

Environment variable contract (validated fail-fast by the entrypoint):

| Env var | Modes | Meaning |
|---|---|---|
| `ENVOY_MODE` | all | `dev`, `dev-host`, or `publish` |
| `CLIENTS` | all | comma-separated client names, registration order |
| `DEFAULT_CLIENT` | dev-host, publish | client answering unmatched hosts (defaults to first) |
| `CLIENT_«NAME»_LISTENER_PORT` | dev, dev-host | the client's listener port (20000 + index) |
| `CLIENT_«NAME»_HOST/PORT` | dev | the client's Angular dev-server upstream |
| `CLIENT_«NAME»_DOMAIN` | publish | the client's public subdomain |
| `CLIENTS_HOST_HOST/PORT` | dev-host, publish | unified SSR host upstream |
| `PORT` | publish | the single public listener port |
| `«SERVICE»_HOST/PORT` | all | one pair per gRPC service (AUTH, PAYMENTS, API) |
| `ENVOY_TLS_CERT/KEY` | all | TLS cert/key file paths |

## 6d. `proxy/envoy.yaml.tmpl` — base config template

```yaml
admin:
  address:
    socket_address: { address: 0.0.0.0, port_value: 9901 }

static_resources:
  listeners:
__LISTENERS__

  clusters:
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
__CLIENT_CLUSTERS__
```

Notes:

- `__LISTENERS__` receives one or more rendered listener pairs (6e).
- `__CLIENT_CLUSTERS__` receives the generated web upstream clusters
  (per-client dev servers, or the single `clients_host`).
- gRPC clusters use `typed_extension_protocol_options` with
  `http2_protocol_options: {}` for h2c upstream — paired with the
  services' Kestrel HTTP/2 configuration.
- Service cluster ports are Aspire-assigned and injected dynamically
  via `WithUpstreamEndpoint` (no hardcoded ports).

## 6e. `proxy/envoy.listener.yaml.tmpl` — listener pair fragment

Rendered once per listener with `__LISTENER_NAME__`,
`__LISTENER_PORT__`, `__RDS_FILE__`, and `__ROUTE_CONFIG_NAME__`
substituted. Indented to sit under `listeners:` in the base template.

```yaml
    - name: __LISTENER_NAME__
      address:
        socket_address: { address: 0.0.0.0, port_value: __LISTENER_PORT__ }
      filter_chains:
        - filters:
            - name: envoy.filters.network.http_connection_manager
              typed_config:
                "@type": type.googleapis.com/envoy.extensions.filters.network.http_connection_manager.v3.HttpConnectionManager
                codec_type: auto
                stat_prefix: __LISTENER_NAME___http
                use_remote_address: true
                scheme_header_transformation:
                  scheme_to_overwrite: http
                rds:
                  config_source:
                    path_config_source:
                      path: /etc/envoy/discovery/__RDS_FILE__
                  route_config_name: __ROUTE_CONFIG_NAME__
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

    - name: __LISTENER_NAME___quic
      address:
        socket_address: { protocol: UDP, address: 0.0.0.0, port_value: __LISTENER_PORT__ }
      udp_listener_config:
        quic_options: {}
      filter_chains:
        - filters:
            - name: envoy.filters.network.http_connection_manager
              typed_config:
                "@type": type.googleapis.com/envoy.extensions.filters.network.http_connection_manager.v3.HttpConnectionManager
                codec_type: HTTP3
                stat_prefix: __LISTENER_NAME___h3
                use_remote_address: true
                scheme_header_transformation:
                  scheme_to_overwrite: http
                rds:
                  config_source:
                    path_config_source:
                      path: /etc/envoy/discovery/__RDS_FILE__
                  route_config_name: __ROUTE_CONFIG_NAME__
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
```

Notes:

- **Two listeners on the same port.** The TCP listener negotiates
  HTTP/2 or HTTP/1.1 via TLS ALPN; the UDP listener serves HTTP/3.
  Browsers discover HTTP/3 via the `alt-svc` response header in the
  virtual host and upgrade on subsequent requests.
- `__LISTENER_NAME___quic` renders correctly because `sed` replaces
  the `__LISTENER_NAME__` substring (e.g. `listener_admin_quic`).
- **`scheme_header_transformation: scheme_to_overwrite: http`** ensures
  upstream services see `http` scheme in the `:scheme` pseudo-header,
  matching their cleartext listeners.

## 6f. `proxy/envoy.rds.yaml.tmpl` and `proxy/envoy.vhost.yaml.tmpl`

The RDS shell, rendered once per route config:

```yaml
resources:
- "@type": type.googleapis.com/envoy.config.route.v3.RouteConfiguration
  name: __ROUTE_CONFIG_NAME__
  virtual_hosts:
__VIRTUAL_HOSTS__
```

The virtual-host fragment, rendered once per client. `__VHOST_NAME__`
is the client name (also the `x-client` value), `__DOMAINS__` is a
JSON array, `__WEB_CLUSTER__` is the catch-all target:

```yaml
    - name: __VHOST_NAME__
      domains: __DOMAINS__
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
            value: 'h3=":__ALT_SVC_PORT__"; ma=86400'
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
          route: { cluster: __WEB_CLUSTER__ }
          request_headers_to_add:
            - header:
                key: "x-client"
                value: "__VHOST_NAME__"
              append_action: OVERWRITE_IF_EXISTS_OR_ADD
```

RDS notes:

- Service routes live **here**, in the vhost fragment — every client's
  listener/domain gets the full API route set automatically. A new
  gRPC service means one new route in this file (see
  `add-dotnet-service/references/update-envoy.md`).
- gRPC routes use `prefix_rewrite: "/"` to strip the `/<service>/`
  prefix so the backend sees native gRPC paths
  (`/<package>.<Service>/<Method>`).
- `max_stream_duration.grpc_timeout_header_max: 0s` disables Envoy's
  default gRPC timeout for long-running streams.
- The catch-all sets `x-client` (`OVERWRITE_IF_EXISTS_OR_ADD` so
  clients cannot spoof it) — the unified SSR host dispatches on it.
- `__CORS_ALLOW_ORIGIN_MATCHES__` is replaced with a generated
  `allow_origin_string_match` block: a local-origin regex in dev
  modes, or `exact: "https://«domain»"` in publish. Traffic is
  same-origin in the normal path, so CORS is a safety net.

## 6g. `proxy/envoy.cluster.yaml.tmpl` — web cluster fragment

```yaml
    - name: __CLUSTER_NAME__
      type: STRICT_DNS
      lb_policy: ROUND_ROBIN
__CLUSTER_TLS_BLOCK__
      load_assignment:
        cluster_name: __CLUSTER_NAME__
        endpoints:
          - lb_endpoints:
              - endpoint:
                  address:
                    socket_address:
                      address: __CLUSTER_HOST__
                      port_value: __CLUSTER_PORT__
```

`__CLUSTER_TLS_BLOCK__` is filled with an upstream TLS context for
dev-mode `ng serve` upstreams (they serve HTTPS with the Aspire dev
cert; Envoy accepts it untrusted) and left empty for the cleartext
unified SSR host.

## 6h. `proxy/entrypoint.sh` — startup rendering

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

upper() {
  echo "$1" | tr 'a-z-' 'A-Z_'
}

require_env ENVOY_MODE
require_env CLIENTS
require_env AUTH_HOST
require_env AUTH_PORT
require_env PAYMENTS_HOST
require_env PAYMENTS_PORT
require_env API_HOST
require_env API_PORT
require_env ENVOY_TLS_CERT
require_env ENVOY_TLS_KEY

CLIENT_LIST=$(echo "$CLIENTS" | tr ',' ' ')
FIRST_CLIENT=${CLIENT_LIST%% *}
DEFAULT_CLIENT="${DEFAULT_CLIENT:-$FIRST_CLIENT}"

TMPL=/etc/envoy
LISTENERS_FILE=/tmp/listeners.yaml
CLIENT_CLUSTERS_FILE=/tmp/client_clusters.yaml
: > "$LISTENERS_FILE"
: > "$CLIENT_CLUSTERS_FILE"

# Upstream TLS block for HTTPS dev servers (ng serve with the Aspire dev cert).
DEV_UPSTREAM_TLS=/tmp/dev_upstream_tls.yaml
cat > "$DEV_UPSTREAM_TLS" <<'EOF'
      transport_socket:
        name: envoy.transport_sockets.tls
        typed_config:
          "@type": type.googleapis.com/envoy.extensions.transport_sockets.tls.v3.UpstreamTlsContext
          common_tls_context:
            validation_context:
              trust_chain_verification: ACCEPT_UNTRUSTED
EOF

render_listener() { # name port rds_file route_config_name
  sed \
    -e "s|__LISTENER_NAME__|$1|g" \
    -e "s|__LISTENER_PORT__|$2|g" \
    -e "s|__RDS_FILE__|$3|g" \
    -e "s|__ROUTE_CONFIG_NAME__|$4|g" \
    "$TMPL/envoy.listener.yaml.tmpl" >> "$LISTENERS_FILE"
  echo "" >> "$LISTENERS_FILE"
}

render_cluster() { # name host port tls_block_file
  sed \
    -e "s|__CLUSTER_NAME__|$1|g" \
    -e "s|__CLUSTER_HOST__|$2|g" \
    -e "s|__CLUSTER_PORT__|$3|g" \
    -e "/^__CLUSTER_TLS_BLOCK__$/r $4" \
    -e "/^__CLUSTER_TLS_BLOCK__$/d" \
    "$TMPL/envoy.cluster.yaml.tmpl" >> "$CLIENT_CLUSTERS_FILE"
}

render_vhost() { # name domains alt_svc_port web_cluster cors_file out_file
  sed \
    -e "s|__VHOST_NAME__|$1|g" \
    -e "s|__DOMAINS__|$2|g" \
    -e "s|__ALT_SVC_PORT__|$3|g" \
    -e "s|__WEB_CLUSTER__|$4|g" \
    -e "/^__CORS_ALLOW_ORIGIN_MATCHES__$/r $5" \
    -e "/^__CORS_ALLOW_ORIGIN_MATCHES__$/d" \
    "$TMPL/envoy.vhost.yaml.tmpl" >> "$6"
}

render_rds() { # route_config_name vhosts_file out_file
  sed \
    -e "s|__ROUTE_CONFIG_NAME__|$1|g" \
    -e "/^__VIRTUAL_HOSTS__$/r $2" \
    -e "/^__VIRTUAL_HOSTS__$/d" \
    "$TMPL/envoy.rds.yaml.tmpl" > "$3"
}

case "$ENVOY_MODE" in
  dev|dev-host)
    # Pages and API are same-origin per listener; CORS allows local origins as
    # a safety net.
    DEV_CORS=/tmp/cors_dev.yaml
    cat > "$DEV_CORS" <<'EOF'
          allow_origin_string_match:
            - safe_regex:
                regex: "^https?://(localhost|127\\.0\\.0\\.1)(:[0-9]+)?$"
EOF

    if [ "$ENVOY_MODE" = "dev-host" ]; then
      require_env CLIENTS_HOST_HOST
      require_env CLIENTS_HOST_PORT
      render_cluster clients_host "$CLIENTS_HOST_HOST" "$CLIENTS_HOST_PORT" /dev/null
    fi

    for CLIENT in $CLIENT_LIST; do
      U=$(upper "$CLIENT")
      require_env "CLIENT_${U}_LISTENER_PORT"
      eval "LISTENER_PORT=\$CLIENT_${U}_LISTENER_PORT"

      if [ "$ENVOY_MODE" = "dev" ]; then
        require_env "CLIENT_${U}_HOST"
        require_env "CLIENT_${U}_PORT"
        eval "UP_HOST=\$CLIENT_${U}_HOST"
        eval "UP_PORT=\$CLIENT_${U}_PORT"
        WEB_CLUSTER="client_${CLIENT}"
        render_cluster "$WEB_CLUSTER" "$UP_HOST" "$UP_PORT" "$DEV_UPSTREAM_TLS"
      else
        WEB_CLUSTER=clients_host
      fi

      VHOSTS_FILE="/tmp/vhosts_${CLIENT}.yaml"
      : > "$VHOSTS_FILE"
      render_vhost "$CLIENT" '["*"]' "$LISTENER_PORT" "$WEB_CLUSTER" "$DEV_CORS" "$VHOSTS_FILE"
      render_rds "routes_${CLIENT}" "$VHOSTS_FILE" "/etc/envoy/discovery/envoy.rds.${CLIENT}.yaml"
      render_listener "listener_${CLIENT}" "$LISTENER_PORT" "envoy.rds.${CLIENT}.yaml" "routes_${CLIENT}"
    done
    ;;

  publish)
    require_env PORT
    require_env CLIENTS_HOST_HOST
    require_env CLIENTS_HOST_PORT

    render_cluster clients_host "$CLIENTS_HOST_HOST" "$CLIENTS_HOST_PORT" /dev/null

    VHOSTS_FILE=/tmp/vhosts.yaml
    : > "$VHOSTS_FILE"
    for CLIENT in $CLIENT_LIST; do
      U=$(upper "$CLIENT")
      require_env "CLIENT_${U}_DOMAIN"
      eval "DOMAIN=\$CLIENT_${U}_DOMAIN"

      DOMAINS="[\"${DOMAIN}\"]"
      if [ "$CLIENT" = "$DEFAULT_CLIENT" ]; then
        # The default client also answers for unmatched hosts.
        DOMAINS="[\"${DOMAIN}\", \"*\"]"
      fi

      CORS_FILE="/tmp/cors_${CLIENT}.yaml"
      cat > "$CORS_FILE" <<EOF
          allow_origin_string_match:
            - exact: "https://${DOMAIN}"
EOF
      render_vhost "$CLIENT" "$DOMAINS" "$PORT" clients_host "$CORS_FILE" "$VHOSTS_FILE"
    done
    render_rds service_routes "$VHOSTS_FILE" /etc/envoy/discovery/envoy.rds.yaml
    render_listener http_listener "$PORT" envoy.rds.yaml service_routes
    ;;

  *)
    echo "Error: unknown ENVOY_MODE '$ENVOY_MODE' (expected dev, dev-host, or publish)." >&2
    exit 1
    ;;
esac

# --- Assemble the main config: insert listeners and client clusters ---
sed \
  -e "/^__LISTENERS__$/r ${LISTENERS_FILE}" \
  -e "/^__LISTENERS__$/d" \
  -e "/^__CLIENT_CLUSTERS__$/r ${CLIENT_CLUSTERS_FILE}" \
  -e "/^__CLIENT_CLUSTERS__$/d" \
  "$TMPL/envoy.yaml.tmpl" > /tmp/envoy.yaml.tmpl

sed \
  -e "s|__AUTH_HOST__|${AUTH_HOST}|g" \
  -e "s|__AUTH_PORT__|${AUTH_PORT}|g" \
  -e "s|__PAYMENTS_HOST__|${PAYMENTS_HOST}|g" \
  -e "s|__PAYMENTS_PORT__|${PAYMENTS_PORT}|g" \
  -e "s|__API_HOST__|${API_HOST}|g" \
  -e "s|__API_PORT__|${API_PORT}|g" \
  -e "s|__ENVOY_TLS_CERT__|${ENVOY_TLS_CERT}|g" \
  -e "s|__ENVOY_TLS_KEY__|${ENVOY_TLS_KEY}|g" \
  /tmp/envoy.yaml.tmpl > /tmp/envoy.yaml

for RDS_FILE in /etc/envoy/discovery/*.yaml; do
  echo "----- ${RDS_FILE} (route config) -----"
  cat "$RDS_FILE"
  echo "----- end ${RDS_FILE} -----"
done

echo "----- /tmp/envoy.yaml (full generated config) -----"
cat /tmp/envoy.yaml
echo "----- end /tmp/envoy.yaml -----"

exec envoy -c /tmp/envoy.yaml "$@"
```

The script: validates env vars fail-fast → renders per-mode fragments
(listeners, RDS files with virtual hosts, web clusters) → assembles
the main config → substitutes the remaining inline tokens → dumps
configs to stdout for debugging → `exec envoy`.

## 6i. Unified SSR host — `clients/host/`

A minimal Express dispatcher serving every client's built SSR bundle
from one Node process. Used in publish mode and the dev-host smoke
test; in dev mode clients run their own `ng serve`.

`clients/host/package.json`:

```json
{
  "name": "host",
  "version": "0.0.0",
  "private": true,
  "type": "module",
  "scripts": { "start": "node server.mjs" },
  "dependencies": { "express": "^5.1.0" }
}
```

Run `npm install --package-lock-only` inside `clients/host/` to
generate the lockfile (`npm ci` in the Dockerfile needs it).

`clients/host/server.mjs`:

```javascript
import express from 'express';

// Add new clients here (the add-angular-client skill does this).
const clientLoaders = {
  admin: () => import('./admin/dist/admin/server/server.mjs'),
};

const defaultClient =
  process.env['DEFAULT_CLIENT'] || Object.keys(clientLoaders)[0];

const handlers = new Map();
for (const [name, load] of Object.entries(clientLoaders)) {
  const { reqHandler } = await load();
  handlers.set(name, reqHandler);
}

const app = express();

app.use((req, res, next) => {
  const requested = req.headers['x-client'];
  const handler =
    (typeof requested === 'string' && handlers.get(requested)) ||
    handlers.get(defaultClient);
  handler(req, res, next);
});

const port = process.env['PORT'] || 4000;
app.listen(port);
```

Each client's built `server.mjs` exports `reqHandler` and only calls
`listen()` when run as the main module, so importing is safe.

`clients/host/Dockerfile` — built with the **repo root** as context
(so client builds can run buf codegen against `services/*/Protos`).
One build/deps stage pair per client:

```dockerfile
# --- admin: build ---
FROM node:22-alpine AS admin-build
WORKDIR /repo/clients/admin
COPY clients/admin/package*.json ./
RUN npm ci
COPY services /repo/services
COPY clients/admin/ ./
RUN npm run build

# --- admin: prod deps ---
FROM node:22-alpine AS admin-deps
WORKDIR /deps
COPY clients/admin/package*.json ./
RUN npm ci --omit=dev

# --- unified host ---
FROM node:22-alpine
WORKDIR /app
ENV NODE_ENV=production
COPY clients/host/package*.json ./
RUN npm ci --omit=dev
COPY clients/host/server.mjs ./

COPY --from=admin-build /repo/clients/admin/dist ./admin/dist
COPY --from=admin-deps /deps/node_modules ./admin/node_modules

EXPOSE 4000
CMD ["node", "server.mjs"]
```

Each client gets its own `node_modules` next to its `dist/` so the SSR
bundle's ESM imports resolve against its own dependency tree. Also
create a repo-root `.dockerignore` excluding `.git`,
`**/node_modules`, `**/dist`, `**/.angular`, `**/bin`, `**/obj`,
`apphost`, and `proxy`.

## 6j. `apphost/EnvoyProxy/EnvoyProxyResourceBuilderExtensions.cs`

```csharp
namespace «ProjectName».AppHost.EnvoyProxy;

public static class EnvoyProxyResourceBuilderExtensions
{
    private const string EnvoyConfigPath = "../proxy";
    private const int FirstClientListenerPort = 20000;

    public static IResourceBuilder<ContainerResource> AddEnvoyProxy(
        this IDistributedApplicationBuilder builder,
        string name,
        bool useSsrHostInDev = false)
    {
        var envoy = builder
            .AddDockerfile(name, EnvoyConfigPath)
            .WithEntrypoint("/bin/sh")
            .WithArgs("/etc/envoy/entrypoint.sh");

        var clientsAnnotation = new EnvoyClientsAnnotation();
        envoy.Resource.Annotations.Add(clientsAnnotation);

        var mode = builder.ExecutionContext.IsPublishMode
            ? "publish"
            : useSsrHostInDev ? "dev-host" : "dev";

        envoy
            .WithEnvironment("ENVOY_MODE", mode)
            .WithEnvironment(ctx =>
            {
                ctx.EnvironmentVariables["CLIENTS"] = string.Join(',', clientsAnnotation.Clients);
            });

        if (builder.ExecutionContext.IsPublishMode)
        {
            envoy
                .WithHttpsEndpoint(targetPort: FirstClientListenerPort, env: "PORT", isProxied: false)
                .WithEndpoint("https", e => e.IsExternal = true);
        }
        else
        {
            // Per-client listeners are added by WithClient; no base listener in dev.
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

    /// <summary>
    /// Registers a client with the proxy. In run mode this adds a dedicated HTTPS listener
    /// endpoint for the client (the browser's entry point) and returns it; in publish mode
    /// this wires a «client»-domain parameter into the client's virtual host.
    /// </summary>
    public static EndpointReference WithClient(
        this IResourceBuilder<ContainerResource> envoy,
        IDistributedApplicationBuilder applicationBuilder,
        string clientName)
    {
        var clientsAnnotation = envoy.Resource.Annotations
            .OfType<EnvoyClientsAnnotation>()
            .Single();
        var listenerPort = FirstClientListenerPort + clientsAnnotation.Clients.Count;
        clientsAnnotation.Clients.Add(clientName);

        var envName = clientName.ToUpperInvariant().Replace('-', '_');

        if (applicationBuilder.ExecutionContext.IsPublishMode)
        {
            var domain = applicationBuilder.AddParameter(
                $"{clientName}-domain", $"{clientName}.example.com", publishValueAsDefault: true);
            envoy.WithEnvironment($"CLIENT_{envName}_DOMAIN", domain);
            return envoy.GetEndpoint("https");
        }

        var endpointName = $"{clientName}-web";
        envoy
            .WithHttpsEndpoint(targetPort: listenerPort, name: endpointName, isProxied: false)
            .WithEnvironment($"CLIENT_{envName}_LISTENER_PORT", listenerPort.ToString())
            .WithUrlForEndpoint(endpointName, u => u.DisplayText = $"{clientName} (web)");

        return envoy.GetEndpoint(endpointName);
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

    private sealed class EnvoyClientsAnnotation : IResourceAnnotation
    {
        public List<string> Clients { get; } = [];
    }
}
```

Notes:

- `AddDockerfile` builds the `proxy/` directory as a Docker image.
- The deferred `WithEnvironment(ctx => …)` callback materializes
  `CLIENTS` after all `WithClient` registrations have run.
- `WithClient` assigns listener target ports in registration order
  (20000, 20001, …) — `isProxied: false` publishes the container port
  directly, so dev URLs are stable (`https://localhost:20000`, …).
- **Dev mode** uses `WithHttpsCertificateConfiguration` to inject
  Aspire's developer certificate paths as `ENVOY_TLS_CERT` and
  `ENVOY_TLS_KEY` environment variables, and adds
  `--add-host=host.docker.internal:host-gateway` so clusters can
  reach host processes (Appendix A).
- **Publish mode** has the single external HTTPS endpoint on `PORT`.
- The admin endpoint (`9901`) uses plain HTTP (`isProxied: false`) and
  gets a health check on `/ready`.

## 6k. `AddClientHost` in `apphost/ClientApp/ClientAppResourceBuilderExtensions.cs`

Alongside the dev-only `AddClientApp` (see
`add-angular-client/references/angular-setup.md` 2e), add the unified
host registration:

```csharp
public static EndpointReference AddClientHost(
    this IDistributedApplicationBuilder builder,
    string name,
    string defaultClient,
    EndpointReference? clientOtelEndpoint = null,
    EndpointReference? clientServerOtelEndpoint = null)
{
    var host = builder.AddDockerfile(name, "..", "clients/host/Dockerfile")
        .WithHttpEndpoint(targetPort: 4000, env: "PORT")
        .WithEnvironment("DEFAULT_CLIENT", defaultClient);

    // host.WithOtelEndpoints(...) — added by the add-opentelemetry skill.

    return host.GetEndpoint("http");
}
```

The context path `".."` is the repo root (relative to `apphost/`);
the host serves cleartext HTTP — Envoy fronts it.

## 6l. Register in `apphost/Program.cs`

```csharp
using «ProjectName».AppHost.ClientApp;
using «ProjectName».AppHost.EnvoyProxy;

var builder = DistributedApplication.CreateBuilder(args);

var auth     = builder.AddProject<Projects.«ProjectName»_Auth_Api>("auth");
var payments = builder.AddProject<Projects.«ProjectName»_Payments_Api>("payments");
var api      = builder.AddProject<Projects.«ProjectName»_Api>("api");

// The unified SSR host serves every client in publish mode. Set SsrHost__Dev=true
// to smoke-test it locally instead of per-client dev servers.
var useSsrHost = builder.ExecutionContext.IsPublishMode
    || bool.TryParse(builder.Configuration["SsrHost:Dev"], out var ssrHostDev) && ssrHostDev;

var proxy = builder.AddEnvoyProxy("envoy", useSsrHost)
    .WaitFor(auth)
    .WaitFor(payments)
    .WaitFor(api);

// Clients: each gets its own Envoy listener (dev) or domain virtual host (publish).
var adminWeb = proxy.WithClient(builder, "admin");

if (useSsrHost)
{
    var clientsHost = builder.AddClientHost("clients", defaultClient: "admin");
    proxy
        .WithUpstreamEndpoint("CLIENTS_HOST", clientsHost)
        .WithEnvironment("DEFAULT_CLIENT", "admin");
}
else
{
    var adminDev = builder.AddClientApp("admin", "../clients/admin", adminWeb);
    proxy.WithUpstreamEndpoint("CLIENT_ADMIN", adminDev);
}

proxy
    .WithUpstreamEndpoint("AUTH", auth.GetEndpoint("http"))
    .WithUpstreamEndpoint("PAYMENTS", payments.GetEndpoint("http"))
    .WithUpstreamEndpoint("API", api.GetEndpoint("http"));

builder.Build().Run();
```

Notes:

- `proxy.WithClient(builder, "admin")` must run **before**
  `AddClientApp` — its return value is the client's `SERVER_URL`.
  Circular endpoint references between envoy and the client are fine
  (both are lazy).
- `WaitFor` ensures services are healthy before Envoy starts
  receiving traffic.
- Upstream names in `WithUpstreamEndpoint` must match the env-var
  prefixes the entrypoint expects (`AUTH` → `__AUTH_HOST__`,
  `CLIENT_ADMIN` → `CLIENT_ADMIN_HOST`, `CLIENTS_HOST` →
  `CLIENTS_HOST_HOST`).
- Backend services expose `http` endpoints — Envoy terminates TLS at
  the edge. Dev `ng serve` upstreams are HTTPS (Aspire dev cert);
  Envoy's generated client clusters carry an `ACCEPT_UNTRUSTED`
  upstream TLS context for them.
- Optionally add an `https-ssr-host` launch profile to
  `apphost/Properties/launchSettings.json` (copy of `https` plus
  `"SsrHost__Dev": "true"`) for one-click dev-host smoke tests.
