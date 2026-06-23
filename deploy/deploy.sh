#!/usr/bin/env bash
#
# On-instance, per-component deploy + scoped health-check + rollback for the
# ProtoFast stack. Invoked by the reusable GitHub deploy workflow
# (.github/workflows/_component-deploy.yml) via AWS SSM Run Command, or by hand
# over an SSM session. No SSH, no inbound ports.
#
# Usage:  deploy.sh apply <component>=<tag> [<component>=<tag> ...]
#
#   component ∈ auth | payments | api | envoy | otel-collector | clients-host
#               | aspire-dashboard
#               | client-<name>            (e.g. client-admin, client-protofast)
#
# Contract (docs/independent-deployment-plan.md §5):
#   /opt/protofast/                      deploy root (APP_DIR)
#     docker-compose.yml                 synced by the deploy job
#     deploy.sh                          this script, synced by the deploy job
#     .env                               STABLE seed: ECR, client domains,
#                                        CLIENTS, DEFAULT_CLIENT, ASSETS_BUCKET,
#                                        AWS_REGION. Seeded by cloud-init / first
#                                        deploy; this script never rewrites it
#                                        (except to persist ECR, see below).
#     versions.env                       VERSION MANIFEST: one *_TAG per
#                                        component — the source of truth for what
#                                        is running. This script rewrites only
#                                        the line(s) for the component(s) applied.
#     versions.env.prev                  pre-apply snapshot; per-component rollback
#     versions.env.lock                  flock target — serialises manifest writes
#     last-good                          (legacy; unused by the per-component flow)
#     tunnel-token                       root-only Cloudflare tunnel token
#
# Each component is the atomic unit: applying one rewrites only its manifest line
# and recreates only its container (a client re-creates the unified host, which
# re-pulls every pinned client from S3 — plan §7). Mixed-version states across
# components are normal. A flock on versions.env.lock serialises concurrent
# instance writes; per-workflow concurrency keeps two deploys of the SAME
# component from racing.
set -euo pipefail

APP_DIR="${APP_DIR:-/opt/protofast}"
ENV_FILE="${APP_DIR}/.env"
VERSIONS_FILE="${APP_DIR}/versions.env"
VERSIONS_PREV="${APP_DIR}/versions.env.prev"
LOCK_FILE="${APP_DIR}/versions.env.lock"
COMPOSE_FILE="${APP_DIR}/docker-compose.yml"
PROJECT="protofast"
NETWORK="${PROJECT}_default"
HEALTH_DEADLINE_SECS="${HEALTH_DEADLINE_SECS:-90}"
KEEP_RELEASES="${KEEP_RELEASES:-5}"

cd "$APP_DIR"

log() { echo "[deploy $(date -u +%H:%M:%S)] $*"; }

# Compose reads BOTH env files: .env (stable seed) for ${ECR}, domains, CLIENTS,
# ASSETS_BUCKET; versions.env (manifest) for every *_TAG image reference. Without
# the manifest file the *_TAG variables render blank and image refs collapse to
# "<ecr>/protofast-<name>:" — which Docker rejects as an invalid reference.
compose() {
  docker compose -p "$PROJECT" \
    --env-file "$ENV_FILE" --env-file "$VERSIONS_FILE" \
    -f "$COMPOSE_FILE" "$@"
}

# Rewrite (or append) a KEY=VALUE line in $file without disturbing the others.
set_env() {
  local file="$1" key="$2" value="$3"
  touch "$file"
  if grep -q "^${key}=" "$file"; then
    sed -i "s|^${key}=.*|${key}=${value}|" "$file"
  else
    echo "${key}=${value}" >> "$file"
  fi
}

# Read a KEY from $file (empty string if absent).
get_env() { [ -f "$1" ] && grep -E "^${2}=" "$1" | head -n1 | cut -d= -f2- || true; }

# Uppercase a component/client name into its manifest-key form: 'client-admin'
# stays a name; the caller composes CLIENT_<UPPER>_TAG. '-' -> '_'.
upper() { printf '%s' "$1" | tr '[:lower:]-' '[:upper:]_'; }

# Resolve a component id to its manifest key, compose service, and kind. Sets the
# globals KEY, SVC, KIND (and CLIENT_NAME for client kinds). Unknown → exit 2.
#   KIND ∈ service | envoy | otel | host | client | aspire | edge
resolve() {
  local component="$1"
  CLIENT_NAME=""
  case "$component" in
    auth|payments|api)
      KEY="$(upper "$component")_TAG"; SVC="$component"; KIND="service" ;;
    envoy)
      KEY="ENVOY_TAG"; SVC="envoy"; KIND="envoy" ;;
    otel-collector)
      KEY="OTEL_TAG"; SVC="otel-collector"; KIND="otel" ;;
    aspire-dashboard)
      KEY="ASPIRE_TAG"; SVC="aspire-dashboard"; KIND="aspire" ;;
    cloudflared)
      KEY="CLOUDFLARED_TAG"; SVC="cloudflared"; KIND="edge" ;;
    clients-host)
      KEY="CLIENTS_HOST_TAG"; SVC="clients"; KIND="host" ;;
    client-*)
      CLIENT_NAME="${component#client-}"
      KEY="CLIENT_$(upper "$CLIENT_NAME")_TAG"; SVC="clients"; KIND="client" ;;
    *)
      echo "unknown component: ${component}" >&2; exit 2 ;;
  esac
}

# The configured client set (manifest/host source of truth) and each client's
# vhost domain (used by the Envoy curl health checks).
clients_list() {
  local c; c="$(get_env "$ENV_FILE" CLIENTS)"; echo "${c:-admin,protofast}"
}
domain_for() {
  local name="$1" d
  d="$(get_env "$ENV_FILE" "CLIENT_$(upper "$name")_DOMAIN")"
  echo "${d:-${name}.example.com}"
}

# --- health-check building blocks (all run inside the compose network; nothing
# is published to the host) -------------------------------------------------

# One client vhost end-to-end through Envoy's publish listener to the SSR host
# (self-signed → -k). Used by host/client checks, where SSR readiness is the
# point. NOT used for envoy: it would gate an envoy deploy on the clients-host
# upstream rendering /, which is out of scope for a --no-deps envoy rollout.
vhost_ok() {
  local domain="$1"
  docker run --rm --network "$NETWORK" curlimages/curl:latest \
    -ksS -o /dev/null -w '' --max-time 5 \
    -H "Host: ${domain}" "https://envoy:8443/"
}

# Envoy-scoped checks via the admin API (binds 0.0.0.0:ENVOY_ADMIN_PORT, reached
# as envoy:9901 on the compose network). These confirm Envoy itself is serving
# without traversing to any upstream.

# Listeners initialised and server live.
envoy_ready() {
  docker run --rm --network "$NETWORK" curlimages/curl:latest \
    -fsS -o /dev/null --max-time 5 "http://envoy:9901/ready"
}

# A client domain is present in Envoy's loaded (RDS) route config, i.e. the vhost
# is configured and routable — independent of whether its SSR upstream is up.
vhost_configured() {
  local domain="$1"
  docker run --rm --network "$NETWORK" curlimages/curl:latest \
    -fsS --max-time 5 "http://envoy:9901/config_dump" \
    | grep -q "\"${domain}\""
}

# gRPC health in a service container (probe binary is bind-mounted into the image).
grpc_ok() {
  compose exec -T "$1" /usr/local/bin/grpc_health_probe -addr=localhost:8080
}

# otel-collector readiness extension (config.yaml: health_check on :13133).
otel_ok() {
  docker run --rm --network "$NETWORK" curlimages/curl:latest \
    -fsS -o /dev/null --max-time 5 "http://otel-collector:13133/"
}

# Aspire Dashboard frontend is serving (the UI cloudflared proxies on :18888).
# Internal only; auth is enforced at the Cloudflare edge, so a plain GET suffices.
aspire_ok() {
  docker run --rm --network "$NETWORK" curlimages/curl:latest \
    -fsS -o /dev/null --max-time 5 "http://aspire-dashboard:18888/"
}

# cloudflared tunnel readiness via its --metrics server (config in compose:
# --metrics 0.0.0.0:2000). GET /ready returns 200 once at least one edge
# connection is registered, 503 otherwise — so this gates on the public edge
# actually being connected, not just the container being up.
cloudflared_ok() {
  docker run --rm --network "$NETWORK" curlimages/curl:latest \
    -fsS -o /dev/null --max-time 5 "http://cloudflared:2000/ready"
}

# Run the component-scoped health check (plan §6) once. 0 = healthy.
health_once() {
  local rc=0 name
  case "$KIND" in
    service)
      grpc_ok "$SVC" || { rc=1; log "grpc health not serving: ${SVC}"; } ;;
    envoy)
      # Envoy-scoped: the listener is up and every client vhost is loaded in the
      # route config. Deliberately does NOT traverse to the SSR upstream — an
      # envoy deploy (--no-deps) must not be gated on clients-host readiness.
      envoy_ready || { rc=1; log "envoy admin not ready"; }
      IFS=','; for name in $(clients_list); do
        name="$(printf '%s' "$name" | tr -d '[:space:]')"; [ -n "$name" ] || continue
        vhost_configured "$(domain_for "$name")" || { rc=1; log "vhost not configured: $(domain_for "$name")"; }
      done; unset IFS ;;
    host)
      # Exercise every client vhost end-to-end through Envoy to the freshly
      # recreated SSR host (this is where SSR readiness genuinely matters).
      IFS=','; for name in $(clients_list); do
        name="$(printf '%s' "$name" | tr -d '[:space:]')"; [ -n "$name" ] || continue
        vhost_ok "$(domain_for "$name")" || { rc=1; log "vhost not ready: $(domain_for "$name")"; }
      done; unset IFS ;;
    client)
      # Only this client's vhost (exercises its freshly pulled assets).
      vhost_ok "$(domain_for "$CLIENT_NAME")" || { rc=1; log "vhost not ready: $(domain_for "$CLIENT_NAME")"; } ;;
    otel)
      otel_ok || { rc=1; log "otel-collector not ready"; } ;;
    aspire)
      aspire_ok || { rc=1; log "aspire-dashboard not ready"; } ;;
    edge)
      cloudflared_ok || { rc=1; log "cloudflared tunnel not ready"; } ;;
  esac
  return "$rc"
}

# Retry the scoped check until it passes or the deadline elapses.
health_check() {
  local deadline; deadline=$(( $(date +%s) + HEALTH_DEADLINE_SECS ))
  while :; do
    if health_once; then
      log "health checks passed for ${COMPONENT}"
      return 0
    fi
    if [ "$(date +%s)" -ge "$deadline" ]; then
      log "health checks for ${COMPONENT} still failing after ${HEALTH_DEADLINE_SECS}s"
      return 1
    fi
    sleep 5
  done
}

# --- apply: recreate ONLY the resolved component's container (plan §5.2) ----
# --no-deps so we touch exactly one container and never evaluate sibling image
# refs (whose tags are irrelevant to this apply). A client/host apply force-
# recreates the host so its entrypoint re-pulls the pinned client set from S3
# even when the host image tag itself is unchanged.
apply_kind() {
  case "$KIND" in
    service|envoy|otel|aspire|edge)
      log "pulling ${SVC}"
      compose pull "$SVC"
      log "recreating ${SVC}"
      compose up -d --no-deps "$SVC" ;;
    host)
      log "pulling ${SVC} (clients-host)"
      compose pull "$SVC"
      log "recreating ${SVC} (re-pulls all clients from S3)"
      compose up -d --no-deps --force-recreate "$SVC" ;;
    client)
      # Image unchanged; only the manifest tag moved. Force-recreate the host so
      # its entrypoint re-syncs the pinned client assets from S3.
      log "recreating clients host for client '${CLIENT_NAME}' (re-pulls from S3)"
      compose up -d --no-deps --force-recreate "$SVC" ;;
  esac
}

# Keep only the most recent KEEP_RELEASES image tags per repo so rollback stays
# local while disk stays bounded (plan §4.3 / §5.2). Old S3 client prefixes are
# pruned by the deploy workflow, not here.
prune_old_images() {
  local ecr
  ecr="$(get_env "$ENV_FILE" ECR)"
  [ -n "$ecr" ] || return 0
  for repo in protofast-envoy protofast-clients-host protofast-auth \
              protofast-payments protofast-api protofast-otel-collector; do
    docker image ls "${ecr}/${repo}" --format '{{.CreatedAt}}\t{{.Repository}}:{{.Tag}}' \
      | sort -r \
      | awk -v keep="$KEEP_RELEASES" 'NR>keep {print $NF}' \
      | xargs -r docker image rm -f >/dev/null 2>&1 || true
  done
}

# Publish the version manifest to S3 (deploy/versions.env) so a replaced instance
# can self-bootstrap to last-known-good: cloud-init pulls this manifest plus the
# compose file + this script and brings the whole stack up (user_data.sh.tftpl).
# versions.env is the source of truth for what is running, but it lives on the box
# and dies with it — this is the durable, off-box copy. Reflects the FINAL on-box
# state (post-rollback), so S3 always mirrors what is actually running.
push_manifest() {
  local bucket region
  bucket="$(get_env "$ENV_FILE" ASSETS_BUCKET)"
  region="$(get_env "$ENV_FILE" AWS_REGION)"
  [ -n "$bucket" ] || { log "ASSETS_BUCKET unset; not publishing manifest"; return 0; }
  [ -f "$VERSIONS_FILE" ] || return 0
  if aws s3 cp "$VERSIONS_FILE" "s3://${bucket}/deploy/versions.env" \
       ${region:+--region "$region"} >/dev/null 2>&1; then
    log "published manifest to s3://${bucket}/deploy/versions.env"
  else
    log "WARNING: failed to publish manifest to s3://${bucket}/deploy/versions.env"
  fi
}

# --- bootstrap: bring the whole stack up from the persisted manifest --------
# Run by cloud-init on a fresh/replaced instance, which has the engine + .env but
# nothing running. Unlike `apply` (one --no-deps container), this starts the
# ENTIRE topology — including the edge (cloudflared) and dashboard — and lets
# compose's depends_on/health gating order the bring-up. A complete manifest
# is the normal steady state; with a partial one (not every component deployed
# yet) we start only the services whose image tag resolves, so a blank *_TAG
# never collapses an image ref to "<ecr>/protofast-<name>:". cloudflared and
# aspire-dashboard carry compose defaults (their refs never collapse), so they
# stay in the always-up list below to guarantee the edge even on a partial box.
SERVICE_TAGS="envoy:ENVOY_TAG clients:CLIENTS_HOST_TAG auth:AUTH_TAG payments:PAYMENTS_TAG api:API_TAG otel-collector:OTEL_TAG"
bootstrap() {
  if [ ! -f "$VERSIONS_FILE" ]; then
    log "no versions.env; nothing to bootstrap (first deploy will converge)"
    return 0
  fi
  local missing="" ready="cloudflared aspire-dashboard" svc tagvar pair
  for pair in $SERVICE_TAGS; do
    svc="${pair%%:*}"; tagvar="${pair#*:}"
    if [ -n "$(get_env "$VERSIONS_FILE" "$tagvar")" ]; then
      ready="$ready $svc"
    else
      missing="$missing $svc"
    fi
  done
  if [ -z "$missing" ]; then
    log "bootstrap: full manifest present; bringing entire stack up"
    compose up -d
  else
    log "bootstrap: partial manifest (missing:$missing); bringing up ready services only"
    # shellcheck disable=SC2086  # word-splitting $ready into service args is intended
    compose up -d --no-deps $ready
  fi
}

# --- argument parsing ------------------------------------------------------
usage() { echo "usage: $0 apply <component>=<tag> [...]   |   $0 bootstrap" >&2; exit 2; }
MODE="${1:-}"
case "$MODE" in
  apply)     shift; [ "$#" -ge 1 ] || usage ;;
  bootstrap) shift; [ "$#" -eq 0 ] || usage ;;
  *)         usage ;;
esac

# Serialise manifest writes across concurrent deploys (plan §8 invariant).
exec 9>"$LOCK_FILE"
flock 9

# The registry host is the same for every deploy. cloud-init seeds it into .env
# at boot, but the deploy job also passes ECR so the deploy is self-sufficient if
# that seed is missing or stale. Persist it, then fail loudly if we still have no
# registry — an empty ECR renders image refs as "/protofast-<name>:<tag>".
if [ -n "${ECR:-}" ]; then
  set_env "$ENV_FILE" ECR "$ECR"
fi
if [ -z "$(get_env "$ENV_FILE" ECR)" ]; then
  echo "ECR is not set in ${ENV_FILE} and no ECR was passed to deploy.sh" >&2
  exit 1
fi

# bootstrap mode brings the whole stack up from the persisted manifest, then exits.
if [ "$MODE" = bootstrap ]; then
  bootstrap
  exit 0
fi

# --- apply each component=tag pair -----------------------------------------
RC=0
for pair in "$@"; do
  COMPONENT="${pair%%=*}"
  NEW_TAG="${pair#*=}"
  if [ -z "$COMPONENT" ] || [ -z "$NEW_TAG" ] || [ "$COMPONENT" = "$pair" ]; then
    echo "bad component=tag argument: '${pair}'" >&2
    exit 2
  fi

  resolve "$COMPONENT"
  CUR_TAG="$(get_env "$VERSIONS_FILE" "$KEY")"

  if [ "$CUR_TAG" = "$NEW_TAG" ]; then
    log "${COMPONENT} already at ${NEW_TAG}; nothing to do"
    continue
  fi

  # Snapshot the manifest for per-component rollback, then write the new tag.
  log "applying ${COMPONENT}: ${KEY} ${CUR_TAG:-<unset>} -> ${NEW_TAG}"
  [ -f "$VERSIONS_FILE" ] && cp "$VERSIONS_FILE" "$VERSIONS_PREV" || : > "$VERSIONS_PREV"
  set_env "$VERSIONS_FILE" "$KEY" "$NEW_TAG"

  # A client pin made before the clients host exists: there is no host container
  # to recreate, and its image tag is unset so the ref ".../protofast-clients-host:"
  # is unresolvable (Docker would reject it). The build job has already pushed this
  # client's assets to S3 and the pin is now in the manifest, so the next
  # clients-host deploy reads versions.env and pulls this client from S3. Record
  # the pin and skip the on-box rollout rather than failing the deploy.
  if [ "$KIND" = client ] && [ -z "$(get_env "$VERSIONS_FILE" CLIENTS_HOST_TAG)" ]; then
    log "${COMPONENT}=${NEW_TAG} pinned; clients host not deployed yet — assets are in S3, skipping recreate"
    continue
  fi

  apply_kind

  if health_check; then
    log "${COMPONENT}=${NEW_TAG} healthy"
    prune_old_images
  else
    # Restore the manifest and re-apply the previous tag for THIS component.
    log "ROLLING BACK ${COMPONENT} to ${CUR_TAG:-<unset>}"
    if [ -f "$VERSIONS_PREV" ]; then
      cp "$VERSIONS_PREV" "$VERSIONS_FILE"
    fi
    apply_kind
    if health_check; then
      log "rollback of ${COMPONENT} healthy; failing the run for the bad tag ${NEW_TAG}"
    else
      log "WARNING: rollback of ${COMPONENT} did not pass health checks"
    fi
    RC=1
  fi
done

# Mirror the final on-box manifest to S3 so a replaced instance self-heals to it.
push_manifest

exit "$RC"
