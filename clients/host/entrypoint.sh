#!/bin/sh
# Unified SSR host entrypoint. The image carries NO client assets — each client
# is built and uploaded to S3 by its own workflow (deploy-client-<name>.yml).
# On every (re)start this pulls each pinned client's assets from S3 into
# /assets/<name>/, then execs the Node host (server.mjs), which discovers the
# same client set from CLIENTS and dispatches by the x-client header.
#
# Inputs (injected by docker-compose via env_file = .env + versions.env):
#   CLIENTS              comma-separated client names (e.g. "admin,protofast")
#   ASSETS_BUCKET        S3 bucket holding clients/<name>/<tag>/...
#   CLIENT_<NAME>_TAG    pinned content-hash tag per client (uppercased name)
#   ASSETS_DIR           where to materialise assets (default /assets)
#
# The CLI uses its defaults (standard S3 endpoint, IMDS at 169.254.169.254) on
# the dual-stack host; only AWS_REGION is injected by compose. Reaching IMDS
# from inside this container needs the instance IMDS hop limit >= 2 (compute.tf).
set -eu

ASSETS_DIR="${ASSETS_DIR:-/assets}"

: "${CLIENTS:?CLIENTS env var is required}"
: "${ASSETS_BUCKET:?ASSETS_BUCKET env var is required}"

# Translate a client name to its CLIENT_<NAME>_TAG variable form: uppercase,
# with '-' -> '_' (matches the manifest keys written by deploy.sh).
tag_var() {
  printf '%s' "$1" | tr '[:lower:]-' '[:upper:]_'
}

IFS=','
for name in $CLIENTS; do
  name="$(printf '%s' "$name" | tr -d '[:space:]')"
  [ -n "$name" ] || continue
  var="CLIENT_$(tag_var "$name")_TAG"
  tag="$(eval "printf '%s' \"\${$var:-}\"")"
  if [ -z "$tag" ]; then
    echo "entrypoint: missing $var for client '$name'" >&2
    exit 1
  fi
  src="s3://${ASSETS_BUCKET}/clients/${name}/${tag}/"
  dst="${ASSETS_DIR}/${name}/"
  echo "entrypoint: syncing ${name}@${tag} from ${src}"
  mkdir -p "$dst"
  aws s3 sync --no-progress --delete "$src" "$dst"
done
unset IFS

exec node server.mjs
