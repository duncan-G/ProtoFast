#!/usr/bin/env bash
#
# On-instance deploy + health-check + rollback for the ProtoFast stack
# Invoked by the GitHub deploy workflow via AWS SSM
# Run Command, or by hand over an SSM session. No SSH, no inbound ports.
#
# Usage:  deploy.sh <git-sha>
#
# Contract:
#   /opt/protofast/                      deploy root (APP_DIR)
#     docker-compose.yml                 synced by the deploy job
#     deploy.sh                          this script, synced by the deploy job
#     .env                               ECR + client domains + TAG (this script
#                                        rewrites only TAG; the rest is seeded by
#                                        cloud-init / the first deploy)
#     last-good                          last SHA that passed health checks
#     tunnel-token                       root-only Cloudflare tunnel token
#
# The atomic unit is the image TAG set: one TAG for every image, so the stack
# rolls forward and back as a whole. Old images stay on disk (pruned beyond
# KEEP_RELEASES), so rollback is a local `compose up -d` that needs no registry.
set -euo pipefail

APP_DIR="${APP_DIR:-/opt/protofast}"
ENV_FILE="${APP_DIR}/.env"
LAST_GOOD_FILE="${APP_DIR}/last-good"
COMPOSE_FILE="${APP_DIR}/docker-compose.yml"
PROJECT="protofast"
NETWORK="${PROJECT}_default"
HEALTH_DEADLINE_SECS="${HEALTH_DEADLINE_SECS:-90}"
KEEP_RELEASES="${KEEP_RELEASES:-5}"
SERVICES_GRPC="auth payments api"

NEW_SHA="${1:-}"
if [ -z "$NEW_SHA" ]; then
  echo "usage: $0 <git-sha>" >&2
  exit 2
fi

cd "$APP_DIR"

log() { echo "[deploy $(date -u +%H:%M:%S)] $*"; }

compose() { docker compose -p "$PROJECT" --env-file "$ENV_FILE" -f "$COMPOSE_FILE" "$@"; }

# Rewrite (or append) a KEY=VALUE line in .env without disturbing the others.
set_env() {
  local key="$1" value="$2"
  touch "$ENV_FILE"
  if grep -q "^${key}=" "$ENV_FILE"; then
    sed -i "s|^${key}=.*|${key}=${value}|" "$ENV_FILE"
  else
    echo "${key}=${value}" >> "$ENV_FILE"
  fi
}

# Read a KEY from .env (after it is populated).
get_env() { grep -E "^${1}=" "$ENV_FILE" | head -n1 | cut -d= -f2-; }

bring_up() {
  local tag="$1"
  set_env TAG "$tag"
  log "pulling images for TAG=${tag}"
  compose pull
  log "starting stack for TAG=${tag}"
  compose up -d --remove-orphans
}

# Health-check the real routing path: each client vhost through Envoy (catching
# SSR-host and Envoy-config breakage) plus gRPC health on the three services.
# All checks run inside the compose network — nothing is published to the host.
health_check() {
  local admin_domain protofast_domain deadline rc
  admin_domain="$(get_env CLIENT_ADMIN_DOMAIN)"
  protofast_domain="$(get_env CLIENT_PROTOFAST_DOMAIN)"
  : "${admin_domain:=admin.example.com}"
  : "${protofast_domain:=protofast.example.com}"

  deadline=$(( $(date +%s) + HEALTH_DEADLINE_SECS ))
  while :; do
    rc=0

    # Per-domain vhost via Envoy's publish listener (self-signed → -k).
    for domain in "$admin_domain" "$protofast_domain"; do
      if ! docker run --rm --network "$NETWORK" curlimages/curl:latest \
            -ksS -o /dev/null -w '' --max-time 5 \
            -H "Host: ${domain}" "https://envoy:8443/"; then
        rc=1
        log "vhost not ready: ${domain}"
      fi
    done

    # gRPC health on each service (probe binary is bind-mounted into the image).
    for svc in $SERVICES_GRPC; do
      if ! compose exec -T "$svc" /usr/local/bin/grpc_health_probe -addr=localhost:8080; then
        rc=1
        log "grpc health not serving: ${svc}"
      fi
    done

    if [ "$rc" -eq 0 ]; then
      log "all health checks passed"
      return 0
    fi

    if [ "$(date +%s)" -ge "$deadline" ]; then
      log "health checks still failing after ${HEALTH_DEADLINE_SECS}s"
      return 1
    fi
    sleep 5
  done
}

# Keep only the most recent KEEP_RELEASES tags per repo so rollback stays local
# while disk stays bounded (deployment-plan §4.3). Never prunes the running TAG
# or last-good (they are among the most recent, and still in use anyway).
prune_old_images() {
  local ecr keep
  ecr="$(get_env ECR)"
  [ -n "$ecr" ] || return 0
  for repo in protofast-envoy protofast-clients-host protofast-auth \
              protofast-payments protofast-api protofast-otel-collector; do
    # Newest-first list of "<created-ts> <ref>"; drop the first KEEP, rm the rest.
    docker image ls "${ecr}/${repo}" --format '{{.CreatedAt}}\t{{.Repository}}:{{.Tag}}' \
      | sort -r \
      | awk -v keep="$KEEP_RELEASES" 'NR>keep {print $NF}' \
      | xargs -r docker image rm -f >/dev/null 2>&1 || true
  done
}

# --- deploy ---
log "deploying ${NEW_SHA}"
bring_up "$NEW_SHA"

if health_check; then
  echo "$NEW_SHA" > "$LAST_GOOD_FILE"
  log "recorded last-good=${NEW_SHA}"
  prune_old_images
  log "deploy ${NEW_SHA} succeeded"
  exit 0
fi

# --- rollback ---
if [ -s "$LAST_GOOD_FILE" ]; then
  PREV="$(cat "$LAST_GOOD_FILE")"
  log "ROLLING BACK to last-good=${PREV}"
  bring_up "$PREV"
  if health_check; then
    log "rollback to ${PREV} healthy; failing the run for the bad deploy ${NEW_SHA}"
  else
    log "WARNING: rollback to ${PREV} did not pass health checks"
  fi
else
  log "no last-good recorded; leaving the failed ${NEW_SHA} up for inspection"
fi
exit 1
