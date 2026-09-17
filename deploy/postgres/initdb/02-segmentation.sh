#!/bin/sh
# Creates (or re-asserts) the segmentation service's durable `segmentation` DB + owning role.
#
# Same shape and same reasoning as 01-auth.sh: the official Postgres image runs scripts in
# /docker-entrypoint-initdb.d only on an EMPTY data dir, and the pgdata EBS volume persists — so
# first-init is not enough. deploy.sh also execs this against a live cluster on every Host B
# apply, which covers password rotation and boxes whose first init predates this file.
#
# Idempotent: CREATE ROLE/DATABASE if missing, ALTER ROLE password from the mounted secret so
# SEGMENTATION_DB_PASSWORD and the role never drift.
set -eu

SEGMENTATION_PASSWORD="$(tr -d '\n' < /run/secrets/segmentation-db-password)"
[ -n "$SEGMENTATION_PASSWORD" ] || { echo "02-segmentation.sh: segmentation-db-password is empty" >&2; exit 1; }

# :'pw' + format(%L) so the password is a safely-quoted SQL literal (no heredoc interpolation).
# \gexec runs each produced statement. CREATE DATABASE cannot run inside a transaction/DO block,
# so it is a separate \gexec.
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  --set=pw="$SEGMENTATION_PASSWORD" <<'SQL'
SELECT format('CREATE ROLE segmentation LOGIN PASSWORD %L', :'pw')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'segmentation')
UNION ALL
SELECT format('ALTER ROLE segmentation WITH PASSWORD %L', :'pw')
WHERE EXISTS (SELECT FROM pg_roles WHERE rolname = 'segmentation');
\gexec

SELECT format('CREATE DATABASE segmentation OWNER segmentation')
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'segmentation');
\gexec
SQL
