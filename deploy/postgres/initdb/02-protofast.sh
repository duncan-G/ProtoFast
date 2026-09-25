#!/bin/sh
# Creates (or re-asserts) the api's `protofast` DB + owning `protofast` role.
#
# Unlike 01-auth.sh the password is not a mounted secret: adding one would mean
# recreating the running Postgres container just to mount it. deploy.sh instead
# execs this against the live cluster with PROTOFAST_DB_PASSWORD passed through
# `compose exec -e` (never on the host's argv), on every api apply, postgres
# apply and bootstrap. On a first init the variable is absent, so the script
# skips rather than failing the whole initdb — the next of those runs creates it.
# Keep the body in sync with deploy.sh write_protofast_initdb.
set -eu

if [ -z "${PROTOFAST_DB_PASSWORD:-}" ]; then
  echo "02-protofast.sh: PROTOFAST_DB_PASSWORD not set; skipping (deploy.sh ensure_protofast_db creates it)"
  exit 0
fi

# :'pw' + format(%L) so the password is a safely-quoted SQL literal (no heredoc
# interpolation). \gexec runs each produced statement. CREATE DATABASE cannot
# run inside a transaction/DO block, so it is a separate \gexec.
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  --set=pw="$PROTOFAST_DB_PASSWORD" <<'SQL'
SELECT format('CREATE ROLE protofast LOGIN PASSWORD %L', :'pw')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'protofast')
UNION ALL
SELECT format('ALTER ROLE protofast WITH PASSWORD %L', :'pw')
WHERE EXISTS (SELECT FROM pg_roles WHERE rolname = 'protofast');
\gexec

SELECT format('CREATE DATABASE protofast OWNER protofast')
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'protofast');
\gexec
SQL
