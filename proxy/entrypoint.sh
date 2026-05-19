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
