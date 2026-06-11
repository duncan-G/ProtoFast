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
require_env OTEL_INSTANCE_ID
require_env OTEL_HTTP_HOST
require_env OTEL_HTTP_PORT
require_env OTEL_GRPC_HOST
require_env OTEL_GRPC_PORT
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
    /etc/envoy/envoy.yaml.tmpl > /tmp/envoy.yaml.tmpl

sed \
  -e "s|__PORT__|${PORT}|g" \
  -e "s|__ENVOY_ADMIN_PORT__|${ENVOY_ADMIN_PORT}|g" \
  -e "s|__OTEL_INSTANCE_ID__|${OTEL_INSTANCE_ID}|g" \
  -e "s|__OTEL_HTTP_HOST__|${OTEL_HTTP_HOST}|g" \
  -e "s|__OTEL_HTTP_PORT__|${OTEL_HTTP_PORT}|g" \
  -e "s|__OTEL_GRPC_HOST__|${OTEL_GRPC_HOST}|g" \
  -e "s|__OTEL_GRPC_PORT__|${OTEL_GRPC_PORT}|g" \
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
  /tmp/envoy.yaml.tmpl > /tmp/envoy.yaml

echo "----- /etc/envoy/discovery/envoy.rds.yaml (route config) -----"
cat /etc/envoy/discovery/envoy.rds.yaml
echo "----- end envoy.rds.yaml -----"

echo "----- /tmp/envoy.yaml (full generated config) -----"
cat /tmp/envoy.yaml
echo "----- end /tmp/envoy.yaml -----"

exec envoy -c /tmp/envoy.yaml "$@"
