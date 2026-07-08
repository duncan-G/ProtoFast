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
#                                        AWS_REGION, HOST_ROLE, peer host IP.
#                                        Seeded by cloud-init / first deploy; this
#                                        script never rewrites it (except to persist
#                                        ECR + the cross-host peer IP, see below).
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

# Make an unexpected abort self-describing. Under `set -e` a failing command (a bad
# `compose pull`, an aws call, a missing binary) otherwise exits with only its own
# stderr and no locator — which the SSM/GitHub layer then surfaces as the opaque
# "failed to run commands: exit status 1". This ERR trap prints the failing line,
# exit code and command as the LAST stderr line, so the root cause is unmissable
# even in the capped inline view. Deliberately NO `errtrace`: without it the trap
# stays silent for failures inside the `set +e` apply_kind rc-capture and for
# anything guarded by `if`/`||` (health checks, get_env), firing only on a genuine
# abort. COMPONENT is empty until the apply loop sets it (safe under `set -u`).
trap 'rc=$?; echo "[deploy] ABORT (exit ${rc}) at line ${LINENO}: ${BASH_COMMAND}${COMPONENT:+ [component=${COMPONENT}]}" >&2' ERR

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

# The single Secrets Manager secret (§4.4); overridable for tests/alt projects.
APP_SECRET_ID="${APP_SECRET_ID:-${PROJECT}/app}"

# Re-seed the root-only secret files that back the compose `secrets:` bind mounts
# (Infra_KcDbPassword -> kc-db-password for the Postgres superuser + Keycloak,
# Auth_DbPassword -> auth-db-password for auth's role). cloud-init seeds these at
# first boot, but that is a one-shot: if the SM secret had no value yet at boot —
# Terraform creates the shell EMPTY and scripts/populate-secrets.sh writes the value
# out-of-band afterward — get-secret-value fails, cloud-init aborts (set -e), the
# files are never created, and cloud-init never re-runs. compose then can't bind the
# mounts and the apply dies before the container starts. So, exactly like ECR and
# the peer IP below, every apply re-asserts them here from Secrets Manager, making
# the deploy self-sufficient regardless of seed ordering. Scoped by the caller to
# the services host (Host B); the edge host's compose references no secret files.
ensure_secret_files() {
  local region secret kc auth
  region="$(get_env "$ENV_FILE" AWS_REGION)"; region="${region:-${AWS_REGION:-}}"
  secret="$(aws secretsmanager get-secret-value --secret-id "$APP_SECRET_ID" \
    ${region:+--region "$region"} --query SecretString --output text 2>/dev/null || true)"
  if [ -z "$secret" ] || [ "$secret" = PLACEHOLDER ]; then
    echo "ensure_secret_files: cannot read app secret '${APP_SECRET_ID}' (region '${region:-unset}')" >&2
    exit 1
  fi
  # Look up a key in the secret. The canonical layout is a JSON key/value map (the
  # native Secrets Manager format the console produces); fall back to the legacy
  # ';'-separated Service_Key=value blob so either layout works during migration
  # (§4.4). The blob is passed via env, never argv, so it can't leak through ps.
  _secret_get() {
    SECRET_BLOB="$secret" python3 - "$1" <<'PY'
import json, os, sys
key = sys.argv[1]
raw = os.environ.get("SECRET_BLOB", "")
try:
    obj = json.loads(raw)
    if isinstance(obj, dict) and key in obj:
        v = obj[key]
        sys.stdout.write(v if isinstance(v, str) else str(v))
        sys.exit(0)
except ValueError:
    pass
for pair in raw.split(";"):
    if pair.startswith(key + "="):
        sys.stdout.write(pair[len(key) + 1:])
        break
PY
  }
  kc="$(_secret_get Infra_KcDbPassword || true)"
  auth="$(_secret_get Auth_DbPassword || true)"
  if [ -z "$kc" ] || [ -z "$auth" ]; then
    echo "ensure_secret_files: app secret '${APP_SECRET_ID}' is missing Infra_KcDbPassword and/or Auth_DbPassword" >&2
    exit 1
  fi
  ( umask 077
    printf '%s\n' "$kc"   > "${APP_DIR}/kc-db-password"
    printf '%s\n' "$auth" > "${APP_DIR}/auth-db-password" )
  chmod 600 "${APP_DIR}/kc-db-password" "${APP_DIR}/auth-db-password"
  # The same first-boot abort can leave AUTH_DB_PASSWORD out of .env (interpolated
  # into auth's ConnectionStrings__auth); re-assert it from the same value.
  set_env "$ENV_FILE" AUTH_DB_PASSWORD "$auth"

  # Auth-svc + Keycloak realm secrets (single SM secret, Auth_/Shared_ prefixes — auth §8.2).
  # The internal-JWT PEM keys go to root-only files (newlines don't fit .env); the files are
  # ALWAYS created (empty if the secret is absent) so the compose `secrets:` bind mounts are valid
  # and a missing key fails only the service that needs it — auth crashes without its private key;
  # api/payments stay up but fail-closed without the public key — never an unrelated component.
  local jwt_priv jwt_pub v
  jwt_priv="$(_secret_get Auth_InternalJwt__PrivateKeyPem || true)"
  jwt_pub="$(_secret_get Shared_InternalJwt__PublicKeyPem || true)"
  ( umask 077; printf '%s' "$jwt_priv" > "${APP_DIR}/auth-internal-jwt-key" )
  chmod 600 "${APP_DIR}/auth-internal-jwt-key"
  printf '%s' "$jwt_pub" > "${APP_DIR}/internal-jwt-pub"
  chmod 644 "${APP_DIR}/internal-jwt-pub"

  # Single-line values → .env for compose interpolation (only when present). These
  # are all optional, so an absent value must be a no-op — NOT `[ -n "$v" ] && ...`,
  # whose bare test returns 1 when $v is empty and, as the function's final command,
  # would make ensure_secret_files return non-zero and abort the whole deploy (set -e).
  v="$(_secret_get Auth_Keycloak__ClientSecretProtofastWeb || true)"; if [ -n "$v" ]; then set_env "$ENV_FILE" PROTOFAST_WEB_CLIENT_SECRET "$v"; fi
  v="$(_secret_get Auth_Keycloak__ClientSecretAdmin || true)";        if [ -n "$v" ]; then set_env "$ENV_FILE" ADMIN_CLIENT_SECRET "$v"; fi
  v="$(_secret_get Auth_InternalJwt__KeyId || true)";                 if [ -n "$v" ]; then set_env "$ENV_FILE" INTERNAL_JWT_KEY_ID "$v"; fi
  v="$(_secret_get Auth_Smtp__Password || true)";                     if [ -n "$v" ]; then set_env "$ENV_FILE" SMTP_PASSWORD "$v"; fi
  v="$(_secret_get Auth_Smtp__Host || true)";                         if [ -n "$v" ]; then set_env "$ENV_FILE" SMTP_HOST "$v"; fi
  v="$(_secret_get Auth_Smtp__User || true)";                         if [ -n "$v" ]; then set_env "$ENV_FILE" SMTP_USER "$v"; fi
}

# Uppercase a component/client name into its manifest-key form: 'client-admin'
# stays a name; the caller composes CLIENT_<UPPER>_TAG. '-' -> '_'.
upper() { printf '%s' "$1" | tr '[:lower:]-' '[:upper:]_'; }

# Resolve a component id to its manifest key, compose service, and kind. Sets the
# globals KEY, SVC, KIND (and CLIENT_NAME for client kinds). Unknown → exit 2.
#   KIND ∈ service | envoy | otel | host | client | aspire | edge | stateful
# stateful (keycloak/postgres/redis — two-instance restructure §5.3) gets its OWN
# apply path, kept separate from the recreate+health+rollback service flow:
# Postgres never auto-rolls-back its tag; Redis is a disposable cache.
resolve() {
  local component="$1"
  CLIENT_NAME=""
  case "$component" in
    auth|payments|api)
      KEY="$(upper "$component")_TAG"; SVC="$component"; KIND="service" ;;
    auth-migrations)
      # Not a long-running container — applying it only publishes the image + pins the tag; the
      # migration RUN happens as a pre-step of the auth apply (run_auth_migrations).
      KEY="AUTH_MIGRATIONS_TAG"; SVC="auth-migrations"; KIND="migrations" ;;
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
    keycloak)
      KEY="KEYCLOAK_TAG"; SVC="keycloak"; KIND="stateful" ;;
    postgres)
      KEY="POSTGRES_TAG"; SVC="postgres"; KIND="stateful" ;;
    redis)
      KEY="REDIS_TAG"; SVC="redis"; KIND="stateful" ;;
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

# Stateful-tier checks (Host B, §5.4). Postgres/Redis are reached via `compose
# exec` (same host); Keycloak's readiness probe is on its management port (9000 in
# Keycloak 26 with KC_HEALTH_ENABLED), hit over the compose network.
keycloak_ok() {
  docker run --rm --network "$NETWORK" curlimages/curl:latest \
    -fsS -o /dev/null --max-time 5 "http://keycloak:9000/health/ready"
}
postgres_ok() { compose exec -T postgres pg_isready -U keycloak >/dev/null 2>&1; }
redis_ok()    { compose exec -T redis redis-cli ping 2>/dev/null | grep -q PONG; }

# Run the component-scoped health check (plan §6) once. 0 = healthy.
health_once() {
  local rc=0 name
  case "$KIND" in
    service)
      grpc_ok "$SVC" || { rc=1; log "grpc health not serving: ${SVC}"; } ;;
    migrations)
      : ;; # one-shot job: no long-running container to probe; the auth apply runs and gates it
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
    stateful)
      case "$SVC" in
        keycloak) keycloak_ok || { rc=1; log "keycloak not ready"; } ;;
        postgres) postgres_ok || { rc=1; log "postgres not accepting connections"; } ;;
        redis)    redis_ok    || { rc=1; log "redis not responding to PING"; } ;;
      esac ;;
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
#
# When the requested tag is already pinned but the running container failed its
# scoped health check (same tag, unhealthy box), the apply loop sets RECREATE to
# --force-recreate: a plain `up -d` with an unchanged image/config is a no-op and
# would leave the sick container in place, so we must force compose to replace it.
# host/client always force-recreate regardless (they re-pull clients from S3).

# Auth schema migrations: a one-shot compose job (profiles: jobs) run BEFORE the auth container is
# recreated. Fail-closed (§3.5.3) — on failure the auth container is NOT recreated, so the old auth
# keeps serving the old (expand/contract-compatible) schema. Pulls the pinned AUTH_MIGRATIONS_TAG.
run_auth_migrations() {
  local tag; tag="$(get_env "$VERSIONS_FILE" AUTH_MIGRATIONS_TAG)"
  if [ -z "$tag" ]; then
    log "AUTH_MIGRATIONS_TAG unset — deploy the auth-migrations component first (§3.5.4); aborting auth apply"
    return 1
  fi
  log "running auth schema migrations (auth-migrations=${tag})"
  compose pull auth-migrations || true
  if ! compose run --rm auth-migrations; then
    log "migrations FAILED — aborting auth apply (auth not recreated)"
    return 1
  fi
  log "auth schema migrations applied"
}

apply_kind() {
  case "$KIND" in
    service|envoy|otel|aspire|edge)
      log "pulling ${SVC}"
      compose pull "$SVC"
      # Gate the auth apply on a successful schema migration (rc 4 → manifest restored upstream).
      if [ "$SVC" = auth ]; then
        run_auth_migrations || return 4
      fi
      log "recreating ${SVC}${RECREATE:+ (forced)}"
      # shellcheck disable=SC2086  # RECREATE is intentionally word-split (flag or empty)
      compose up -d --no-deps $RECREATE "$SVC" ;;
    migrations)
      # Publish the image + pin AUTH_MIGRATIONS_TAG only (the manifest line is written by the apply
      # loop). The migration RUN is the auth apply's fail-closed pre-step — not here.
      log "pulling ${SVC} (image only; migrations run during the auth apply)"
      compose pull "$SVC" ;;
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
    stateful)
      # Separate path (§5.3): plain `up -d --no-deps`, never a blind
      # --force-recreate. Postgres major-version bumps are MIGRATIONS, not deploys
      # — a downgrade after a catalog upgrade corrupts data — so gate them behind
      # ALLOW_PG_MAJOR=1 and never auto-roll-back a Postgres tag (handled in the
      # apply loop). Redis is a disposable cache; Keycloak is health-gated.
      if [ "$SVC" = postgres ]; then
        local cur_major new_major
        cur_major="${CUR_TAG%%.*}"; new_major="${NEW_TAG%%.*}"
        if [ -n "$cur_major" ] && [ "$cur_major" != "$new_major" ] && [ "${ALLOW_PG_MAJOR:-0}" != "1" ]; then
          log "REFUSING postgres major bump ${CUR_TAG} -> ${NEW_TAG}: a major upgrade is a migration."
          log "Re-run with ALLOW_PG_MAJOR=1 once you have a pg_dump backup and a migration plan (§5.3/§6.3)."
          return 3
        fi
      fi
      log "pulling ${SVC}"
      compose pull "$SVC"
      # Normally no force-recreate (§5.3). The one exception is an unhealthy
      # same-tag box (RECREATE set): the container is already up but failing its
      # check, so it must be replaced to recover — safe here as the data lives on
      # the EBS volume, not the container, and a same-tag apply can't be a major bump.
      log "recreating ${SVC} (stateful${RECREATE:+: forced, unhealthy})"
      # shellcheck disable=SC2086  # RECREATE is intentionally word-split (flag or empty)
      compose up -d --no-deps $RECREATE "$SVC" ;;
  esac
}

# Is the resolved compose service backed by a RUNNING container? Used to decide
# whether an "already at <tag>" manifest line is truly a no-op. The manifest is
# the record of what SHOULD run, not proof that it is: a replaced box, a crashed
# service, or a stateful tier that never came up all leave *_TAG pinned at the
# requested value while nothing is actually up. Gating the skip on this is what
# turns a silent "postgres already at 17; nothing to do" into a real bring-up.
# `compose ps -aq` lists the service's container (incl. exited); inspect confirms
# it is running. No container, or a stopped one → not running → there IS work.
svc_running() {
  local cid
  cid="$(compose ps -aq "$SVC" 2>/dev/null | head -n1)"
  [ -n "$cid" ] || return 1
  [ "$(docker inspect -f '{{.State.Running}}' "$cid" 2>/dev/null)" = true ]
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
# Per-host bring-up sets (two-instance restructure §6.1): each host's deploy.sh
# acts only on the components in ITS compose file. SERVICE_TAGS are services whose
# image ref has NO compose default (a blank *_TAG would collapse the ref), so they
# start only once their tag is present. ALWAYS_UP services carry compose defaults
# (cloudflared/aspire on edge; the pinned upstream postgres/redis/keycloak on B),
# so their refs never collapse and they start even on a partial manifest.
host_bringup_sets() {
  case "$(get_env "$ENV_FILE" HOST_ROLE)" in
    services)
      SERVICE_TAGS="auth:AUTH_TAG payments:PAYMENTS_TAG api:API_TAG"
      ALWAYS_UP="postgres redis keycloak" ;;
    *) # edge (also the default for a pre-split .env that predates HOST_ROLE)
      SERVICE_TAGS="envoy:ENVOY_TAG clients:CLIENTS_HOST_TAG otel-collector:OTEL_TAG"
      ALWAYS_UP="cloudflared aspire-dashboard" ;;
  esac
}
bootstrap() {
  if [ ! -f "$VERSIONS_FILE" ]; then
    log "no versions.env; nothing to bootstrap (first deploy will converge)"
    return 0
  fi
  local missing="" svc tagvar pair
  host_bringup_sets
  local ready="$ALWAYS_UP"
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

# --- drain: graceful Host B teardown (§6.2) --------------------------------
# Stop the WRITERS first (so nothing is still sending data), then Postgres, then
# unmount — the order that avoids force-detaching a dirty filesystem and stopping
# writes mid-transaction. Invoked by the systemd ExecStop on every Host B shutdown
# (incl. Terraform terminate) and by an explicit pre-apply SSM drain. Best-effort:
# Postgres is crash-safe (WAL), so a partial drain never corrupts data.
drain() {
  log "draining Host B: writers -> postgres -> unmount"
  compose stop auth payments api keycloak || true   # no new sessions/realm writes
  compose stop postgres || true                      # fast shutdown (image STOPSIGNAL=SIGINT)
  sync
  umount /mnt/pgdata 2>/dev/null || log "WARN: /mnt/pgdata busy or not mounted; ext4 journal covers it"
}

# --- argument parsing ------------------------------------------------------
usage() { echo "usage: $0 apply <component>=<tag> [...]   |   $0 bootstrap   |   $0 drain" >&2; exit 2; }
MODE="${1:-}"
case "$MODE" in
  apply)     shift; [ "$#" -ge 1 ] || usage ;;
  bootstrap) shift; [ "$#" -eq 0 ] || usage ;;
  drain)     shift; [ "$#" -eq 0 ] || usage ;;
  *)         usage ;;
esac

# Serialise manifest writes across concurrent deploys (plan §8 invariant).
exec 9>"$LOCK_FILE"
flock 9

# drain only stops containers + unmounts — no manifest write, no ECR needed.
if [ "$MODE" = drain ]; then
  drain
  exit 0
fi

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

# Cross-host peer IP, same self-sufficiency rationale as ECR above. cloud-init
# seeds it into .env at first boot (HOST_A_IP on Host B / HOST_B_IP on Host A), but
# Host B is pinned against re-provisioning (user_data_replace_on_change=false, to
# protect the pgdata EBS volume — infra/compute.tf), so a box that first booted
# before the two-instance split has no peer-IP line and Terraform never adds one.
# compose then interpolates a blank host — Host B's OTLP endpoint collapses to
# "http://:4317" (unparseable URI → the .NET tier crash-loops), Host A's Envoy
# upstreams lose their host. The deploy job now passes the sibling's private IP so
# every apply re-seeds it. Persist whichever was passed (only one is, per host).
if [ -n "${HOST_A_IP:-}" ]; then set_env "$ENV_FILE" HOST_A_IP "$HOST_A_IP"; fi
if [ -n "${HOST_B_IP:-}" ]; then set_env "$ENV_FILE" HOST_B_IP "$HOST_B_IP"; fi

# Re-seed the root-only secret files (see ensure_secret_files) before any compose
# action — covers both bootstrap and apply. Gate on whether THIS host's compose
# actually declares the secret bind mounts rather than on HOST_ROLE: HOST_ROLE is
# itself seeded only by cloud-init, so the same first-boot abort that drops the
# secret files can drop HOST_ROLE too — gating on it would skip exactly the host
# that needs seeding. The edge host's compose references no secret files, so it is
# skipped here (and so spared an SM read its instance role may not be granted).
if grep -q 'kc-db-password' "$COMPOSE_FILE" 2>/dev/null; then
  ensure_secret_files
fi

# bootstrap mode brings the whole stack up from the persisted manifest, then exits.
# (No peer-IP gate here: a stale box must still be able to bring up its stateful
# tier, which doesn't reference the peer IP.)
if [ "$MODE" = bootstrap ]; then
  bootstrap
  exit 0
fi

# Fail an apply loudly if this host's peer IP is still missing, rather than after a
# recreate + failed health check + rollback. Scoped by HOST_ROLE so each host gates
# only on the var ITS compose interpolates (Host B → HOST_A_IP, Host A → HOST_B_IP);
# a pre-split .env with no HOST_ROLE skips the gate (single box, no peer concept).
case "$(get_env "$ENV_FILE" HOST_ROLE)" in
  services)
    if [ -z "$(get_env "$ENV_FILE" HOST_A_IP)" ]; then
      echo "HOST_A_IP is not set in ${ENV_FILE} and none was passed to deploy.sh" >&2
      exit 1
    fi ;;
  edge)
    if [ -z "$(get_env "$ENV_FILE" HOST_B_IP)" ]; then
      echo "HOST_B_IP is not set in ${ENV_FILE} and none was passed to deploy.sh" >&2
      exit 1
    fi ;;
esac

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

  # RECREATE is the force-recreate flag honoured by apply_kind. It is set ONLY on
  # the unhealthy-same-tag branch below, where compose would otherwise no-op an
  # unchanged image and leave the sick container running. Reset every iteration.
  RECREATE=""

  # Only skip when the tag matches AND the container is running AND it passes its
  # scoped health check. Matching the manifest alone is not enough on two counts:
  #   - nothing is up (box replaced, crash, never started) — bring it up;
  #   - it is up but UNHEALTHY (crash-looping, gRPC not serving, SSR not
  #     rendering) — a stale "already at <tag>" must not mask a sick container,
  #     so force-recreate it to recover.
  # A healthy, running, same-tag container is the only true no-op. The fall-
  # through apply path is idempotent: `up -d` no-ops a healthy container, starts
  # a down one, and (with RECREATE) replaces an unhealthy one.
  if [ "$CUR_TAG" = "$NEW_TAG" ]; then
    if svc_running; then
      if health_once; then
        log "${COMPONENT} already at ${NEW_TAG} and healthy; nothing to do"
        continue
      fi
      log "${COMPONENT} at ${NEW_TAG} but UNHEALTHY; forcing recreate"
      RECREATE="--force-recreate"
    else
      log "${COMPONENT} pinned at ${NEW_TAG} but not running; bringing it up"
    fi
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

  # apply_kind may refuse a Postgres major bump (rc=3) — capture rather than let
  # set -e abort, so we can undo the manifest tag and move on.
  set +e; apply_kind; apply_rc=$?; set -e
  if [ "$apply_rc" -eq 3 ]; then
    log "restoring manifest: ${COMPONENT} stays at ${CUR_TAG:-<unset>} (apply refused)"
    [ -f "$VERSIONS_PREV" ] && cp "$VERSIONS_PREV" "$VERSIONS_FILE"
    RC=1
    continue
  fi
  if [ "$apply_rc" -eq 4 ]; then
    log "restoring manifest: ${COMPONENT} stays at ${CUR_TAG:-<unset>} (auth migrations failed; auth not recreated)"
    [ -f "$VERSIONS_PREV" ] && cp "$VERSIONS_PREV" "$VERSIONS_FILE"
    RC=1
    continue
  fi

  if health_check; then
    log "${COMPONENT}=${NEW_TAG} healthy"
    prune_old_images
  elif [ "$SVC" = postgres ]; then
    # NEVER auto-rollback a Postgres tag (§5.3/§7): a downgrade after a catalog
    # upgrade corrupts data. Leave it running, flag the run, demand manual action.
    log "postgres ${NEW_TAG} did not pass health checks — NOT auto-rolling-back a Postgres tag (§5.3)."
    log "Investigate manually; restore from a pg_dump backup if needed (§6.3)."
    RC=1
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
