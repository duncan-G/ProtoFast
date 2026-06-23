#!/bin/sh
# First-init only (runs solely on an EMPTY data dir — §3.2). POSTGRES_DB creates
# Keycloak's `keycloak` DB; this adds auth's durable `auth` DB + its owning
# `auth` role. Because the EBS volume persists, this fires exactly once: adding an
# auth DB to an already-populated volume means running these by hand.
#
# Implemented as a shell init script (not a static .sql) so the role password can
# be read from the mounted secret rather than baked into a committed file. The
# value MUST match AUTH_DB_PASSWORD in auth's connection string (.env) — both
# are seeded from the same cloud-init secret (§3.2 "keep the two in sync").
set -eu

AUTH_PASSWORD="$(cat /run/secrets/auth-db-password)"

# Connect as the bootstrap superuser ($POSTGRES_USER) to the default DB. Quote the
# password as a literal; CREATE DATABASE cannot run inside a transaction block, so
# keep these as separate top-level statements.
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<SQL
DO \$\$
BEGIN
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'auth') THEN
    CREATE ROLE auth LOGIN PASSWORD '${AUTH_PASSWORD}';
  END IF;
END
\$\$;
SQL

# CREATE DATABASE must be outside a DO/transaction block and is not idempotent, so
# guard it with a separate existence check via the shell.
if ! psql -tAc "SELECT 1 FROM pg_database WHERE datname = 'auth'" \
     --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" | grep -q 1; then
  psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
    -c "CREATE DATABASE auth OWNER auth"
fi
