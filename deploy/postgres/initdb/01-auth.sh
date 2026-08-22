#!/bin/sh
# Creates (or re-asserts) auth's durable `auth` DB + owning `auth` role.
#
# The official Postgres image runs scripts in /docker-entrypoint-initdb.d only
# on an EMPTY data dir. The pgdata EBS volume persists, so first-init is not
# enough: deploy.sh also execs this against a live cluster on every Host B apply
# (password rotation, boxes whose first init happened before this script was
# mounted). Idempotent: CREATE ROLE/DATABASE if missing, ALTER ROLE password
# from the mounted secret so AUTH_DB_PASSWORD and the role never drift.
set -eu

AUTH_PASSWORD="$(tr -d '\n' < /run/secrets/auth-db-password)"
[ -n "$AUTH_PASSWORD" ] || { echo "01-auth.sh: auth-db-password is empty" >&2; exit 1; }

# :'pw' + format(%L) so the password is a safely-quoted SQL literal (no heredoc
# interpolation). \gexec runs each produced statement. CREATE DATABASE cannot
# run inside a transaction/DO block, so it is a separate \gexec.
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  --set=pw="$AUTH_PASSWORD" <<'SQL'
SELECT format('CREATE ROLE auth LOGIN PASSWORD %L', :'pw')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'auth')
UNION ALL
SELECT format('ALTER ROLE auth WITH PASSWORD %L', :'pw')
WHERE EXISTS (SELECT FROM pg_roles WHERE rolname = 'auth');
\gexec

SELECT format('CREATE DATABASE auth OWNER auth')
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'auth');
\gexec
SQL
