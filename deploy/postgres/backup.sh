#!/usr/bin/env bash
# Scheduled logical backup of Host B's Postgres (§6.3 — the durable restore path;
# clean shutdown protects the volume, backups protect against everything else).
# pg_dump the keycloak + auth DBs through the running container and upload each
# gzip to s3://<assets-bucket>/backups/postgres/<db>/<utc-timestamp>.sql.gz.
#
# Run by the protofast-pgbackup.timer systemd unit (installed by Host B cloud-init)
# and safe to run by hand over SSM. Uses the instance profile for S3 (backups/*
# write is scoped in infra/iam.tf).
set -euo pipefail

APP_DIR="${APP_DIR:-/opt/protofast}"
ENV_FILE="${APP_DIR}/.env"
PROJECT="protofast"
COMPOSE_FILE="${APP_DIR}/docker-compose.yml"

cd "$APP_DIR"
log() { echo "[pgbackup $(date -u +%H:%M:%SZ)] $*"; }

get_env() { [ -f "$1" ] && grep -E "^${2}=" "$1" | head -n1 | cut -d= -f2- || true; }

BUCKET="$(get_env "$ENV_FILE" ASSETS_BUCKET)"
REGION="$(get_env "$ENV_FILE" AWS_REGION)"
[ -n "$BUCKET" ] || { log "ASSETS_BUCKET unset; cannot upload backup"; exit 1; }

compose() {
  docker compose -p "$PROJECT" --env-file "$ENV_FILE" -f "$COMPOSE_FILE" "$@"
}

ts="$(date -u +%Y%m%dT%H%M%SZ)"
rc=0
for db in keycloak auth; do
  key="backups/postgres/${db}/${ts}.sql.gz"
  log "dumping ${db} -> s3://${BUCKET}/${key}"
  # pg_dump as the keycloak superuser over the container's local socket (trust),
  # gzip in-stream, pipe straight to S3 — no dump file ever lands on the host disk.
  if compose exec -T postgres pg_dump -U keycloak -d "$db" \
       | gzip \
       | aws s3 cp - "s3://${BUCKET}/${key}" ${REGION:+--region "$REGION"}; then
    log "backed up ${db}"
  else
    log "WARNING: backup of ${db} failed"
    rc=1
  fi
done

exit "$rc"
