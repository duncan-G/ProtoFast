#!/bin/sh
set -e

# Renders the Envoy config from templates based on ENVOY_MODE:
#   dev      — one HTTPS listener per client (CLIENT_<NAME>_LISTENER_PORT) serving
#              its CLIENT_<NAME>_DOMAIN, each routing API prefixes to the services
#              and its catch-all to that client's Angular dev server
#              (CLIENT_<NAME>_HOST/PORT).
#   dev-host — same per-client listeners, but every catch-all routes to the
#              unified SSR host (CLIENTS_HOST_HOST/PORT) with an x-client header.
#   publish  — a single listener on PORT with one virtual host per client domain
#              (CLIENT_<NAME>_DOMAIN), all routing to the unified SSR host.

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
require_env ENVOY_ADMIN_PORT
require_env OTEL_INSTANCE_ID
require_env OTEL_HTTP_HOST
require_env OTEL_HTTP_PORT
require_env OTEL_GRPC_HOST
require_env OTEL_GRPC_PORT
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
# The client whose host the admin consoles live on; its vhost carries the console CSP.
ADMIN_CLIENT="${ADMIN_CLIENT:-admin}"

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

render_vhost() { # name domains alt_svc_port web_cluster cors_file out_file console_upstream_tls
  local consoles_file="/tmp/console_routes_$1.yaml" headers_file="/tmp/response_headers_$1.yaml"
  render_console_routes "$1" "$4" "$7" "$consoles_file"
  : > "$headers_file"
  if [ "$1" = "$ADMIN_CLIENT" ]; then
    case "$ENVOY_MODE" in
      # The dev server's HMR socket falls back to its own localhost port.
      dev*) sed -e "s|connect-src 'self'|connect-src 'self' wss://localhost:*|" "$TMPL/envoy.admin-csp.yaml.tmpl" > "$headers_file" ;;
      *) cat "$TMPL/envoy.admin-csp.yaml.tmpl" > "$headers_file" ;;
    esac
  fi
  sed \
    -e "s|__VHOST_NAME__|$1|g" \
    -e "s|__DOMAINS__|$2|g" \
    -e "s|__ALT_SVC_PORT__|$3|g" \
    -e "s|__WEB_CLUSTER__|$4|g" \
    -e "/^__CORS_ALLOW_ORIGIN_MATCHES__$/r $5" \
    -e "/^__CORS_ALLOW_ORIGIN_MATCHES__$/d" \
    -e "/^__CONSOLE_ROUTES__$/r $consoles_file" \
    -e "/^__CONSOLE_ROUTES__$/d" \
    -e "/^__RESPONSE_HEADERS_EXTRA__$/r $headers_file" \
    -e "/^__RESPONSE_HEADERS_EXTRA__$/d" \
    "$TMPL/envoy.vhost.yaml.tmpl" >> "$6"
}

# App consoles mounted under a client's host at /<console>/ (CLIENT_<NAME>_CONSOLES), each with
# its own upstream (CONSOLE_<CONSOLE>_HOST/PORT) — docs/design/admin-consoles.md.
render_console_routes() { # client web_cluster upstream_tls_file out_file
  local consoles console cu c_host c_port
  : > "$4"
  eval "consoles=\${CLIENT_$(upper "$1")_CONSOLES:-}"
  for console in $(echo "$consoles" | tr ',' ' '); do
    case "$console" in
      *[!a-z0-9-]*) echo "Error: console name '$console' must be lowercase letters, digits and '-'." >&2; exit 1 ;;
    esac
    cu=$(upper "$console")
    require_env "CONSOLE_${cu}_HOST"
    require_env "CONSOLE_${cu}_PORT"
    eval "c_host=\$CONSOLE_${cu}_HOST"
    eval "c_port=\$CONSOLE_${cu}_PORT"
    render_cluster "console_${console}" "$c_host" "$c_port" "$3"
    sed -e "s|__CONSOLE__|$console|g" -e "s|__WEB_CLUSTER__|$2|g" \
      "$TMPL/envoy.console-routes.yaml.tmpl" >> "$4"
  done
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
    if [ "$ENVOY_MODE" = "dev-host" ]; then
      require_env CLIENTS_HOST_HOST
      require_env CLIENTS_HOST_PORT
      render_cluster clients_host "$CLIENTS_HOST_HOST" "$CLIENTS_HOST_PORT" /dev/null
    fi

    for CLIENT in $CLIENT_LIST; do
      U=$(upper "$CLIENT")
      require_env "CLIENT_${U}_LISTENER_PORT"
      require_env "CLIENT_${U}_DOMAIN"
      eval "LISTENER_PORT=\$CLIENT_${U}_LISTENER_PORT"
      eval "DOMAIN=\$CLIENT_${U}_DOMAIN"

      if [ "$ENVOY_MODE" = "dev" ]; then
        require_env "CLIENT_${U}_HOST"
        require_env "CLIENT_${U}_PORT"
        eval "UP_HOST=\$CLIENT_${U}_HOST"
        eval "UP_PORT=\$CLIENT_${U}_PORT"
        WEB_CLUSTER="client_${CLIENT}"
        render_cluster "$WEB_CLUSTER" "$UP_HOST" "$UP_PORT" "$DEV_UPSTREAM_TLS"
        CONSOLE_TLS="$DEV_UPSTREAM_TLS"
      else
        WEB_CLUSTER=clients_host
        CONSOLE_TLS=/dev/null
      fi

      CORS_FILE="/tmp/cors_${CLIENT}.yaml"
      cat > "$CORS_FILE" <<EOF
          allow_origin_string_match:
            - exact: "https://${DOMAIN}:${LISTENER_PORT}"
EOF

      VHOSTS_FILE="/tmp/vhosts_${CLIENT}.yaml"
      : > "$VHOSTS_FILE"
      render_vhost "$CLIENT" "[\"${DOMAIN}\", \"${DOMAIN}:${LISTENER_PORT}\"]" "$LISTENER_PORT" "$WEB_CLUSTER" "$CORS_FILE" "$VHOSTS_FILE" "$CONSOLE_TLS"
      # Any other Host (an old https://localhost:2000x bookmark) is sent to the client's own
      # hostname rather than served there, where its cookie would not be the client's.
      cat >> "$VHOSTS_FILE" <<EOF
    - name: ${CLIENT}_canonical_host
      domains: ["*"]
      typed_per_filter_config:
        envoy.filters.http.ext_authz:
          "@type": type.googleapis.com/envoy.extensions.filters.http.ext_authz.v3.ExtAuthzPerRoute
          disabled: true
      routes:
        - match: { prefix: "/" }
          redirect:
            scheme_redirect: https
            host_redirect: "${DOMAIN}"
            port_redirect: ${LISTENER_PORT}
            response_code: FOUND

EOF
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
      render_vhost "$CLIENT" "$DOMAINS" "$PORT" clients_host "$CORS_FILE" "$VHOSTS_FILE" /dev/null
    done

    # Optional Keycloak edge vhost. Only the two-instance prod topology routes
    # Keycloak through Envoy (KEYCLOAK_HOST = Host B's private IP); dev reaches
    # Keycloak directly via Aspire, so leaving KEYCLOAK_HOST unset skips this.
    if [ -n "${KEYCLOAK_HOST:-}" ]; then
      require_env KEYCLOAK_PORT
      require_env KEYCLOAK_DOMAIN
      # HTTP/1.1 upstream (Keycloak is not h2c) — the generic cluster template.
      render_cluster keycloak "$KEYCLOAK_HOST" "$KEYCLOAK_PORT" /dev/null
      sed -e "s|__KEYCLOAK_DOMAIN__|${KEYCLOAK_DOMAIN}|g" \
        "$TMPL/envoy.keycloak-vhost.yaml.tmpl" >> "$VHOSTS_FILE"
      echo "" >> "$VHOSTS_FILE"
    fi

    render_rds service_routes "$VHOSTS_FILE" /etc/envoy/discovery/envoy.rds.yaml
    render_listener http_listener "$PORT" envoy.rds.yaml service_routes
    ;;

  *)
    echo "Error: unknown ENVOY_MODE '$ENVOY_MODE' (expected dev, dev-host, or publish)." >&2
    exit 1
    ;;
esac

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

# --- Assemble the main config: insert listeners, client clusters, OTel TLS block ---
sed \
  -e "/^__LISTENERS__$/r ${LISTENERS_FILE}" \
  -e "/^__LISTENERS__$/d" \
  -e "/^__CLIENT_CLUSTERS__$/r ${CLIENT_CLUSTERS_FILE}" \
  -e "/^__CLIENT_CLUSTERS__$/d" \
  -e "/^__OTEL_GRPC_TLS_BLOCK__$/r ${OTEL_GRPC_TLS_BLOCK_FILE}" \
  -e "/^__OTEL_GRPC_TLS_BLOCK__$/d" \
  "$TMPL/envoy.yaml.tmpl" > /tmp/envoy.yaml.tmpl

sed \
  -e "s|__ENVOY_ADMIN_PORT__|${ENVOY_ADMIN_PORT}|g" \
  -e "s|__OTEL_INSTANCE_ID__|${OTEL_INSTANCE_ID}|g" \
  -e "s|__OTEL_HTTP_HOST__|${OTEL_HTTP_HOST}|g" \
  -e "s|__OTEL_HTTP_PORT__|${OTEL_HTTP_PORT}|g" \
  -e "s|__OTEL_GRPC_HOST__|${OTEL_GRPC_HOST}|g" \
  -e "s|__OTEL_GRPC_PORT__|${OTEL_GRPC_PORT}|g" \
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
