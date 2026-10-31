#!/usr/bin/env bash
# Fills in ProtoFast's keys in a deployed environment's secret, protofast/<environment>
# (protofast/prod), before its first deploy (docs/06-secrets.md). An Org Admin runs it, on their
# own machine: it makes every password and key itself, in a private temporary directory, and
# hands them to ProtoStrap's set-secret.sh as files, so no value is shown, typed, logged or put
# on a command line. Everything goes in one run, one version of the secret.
#
#   scripts/set-secrets.sh prod                       # sets every key the secret lacks
#   scripts/set-secrets.sh prod --rotate redis        # makes new values for a group of keys
#
# Groups for --rotate: postgres (the superuser), keycloak-db, auth-db, protofast-db, redis,
# clients (the four Keycloak client secrets), internal-jwt (the key pair and its kid). Each
# rotates every key that must change with it, in the same run. Keycloak's bootstrap admin
# client isn't rotated: Keycloak makes it only on its first start.
#
# The model APIs' keys aren't made here; set the ones the environment uses yourself, typed
# without echo:
#
#   ../ProtoStrap/scripts/set-secret.sh protofast/prod DocumentImport_Providers__deepseek__ApiKey=-
#
# It calls ProtoStrap's scripts/set-secret.sh at the spec's version: PROTOSTRAP_SOURCE, or
# .protostrap, or ../ProtoStrap. Env: AWS_PROFILE, AWS_REGION. Needs jq, openssl and the AWS CLI.
set -euo pipefail

environment=${1:?usage: scripts/set-secrets.sh <deployed environment, such as prod> [--rotate group...]}
shift
rotate=()
if [[ ${1:-} == --rotate ]]; then
  shift
  rotate=("$@")
fi
secret="protofast/$environment"

root="$(cd "$(dirname "$0")/.." && pwd)"
source_dir=${PROTOSTRAP_SOURCE:-}
if [[ -z "$source_dir" ]]; then
  for candidate in "$root/.protostrap" "$root/../ProtoStrap"; do
    [[ -f "$candidate/scripts/set-secret.sh" ]] && { source_dir=$candidate; break; }
  done
fi
[[ -n "$source_dir" ]] || { echo "no ProtoStrap checkout: set PROTOSTRAP_SOURCE, or clone it at the spec's version into .protostrap" >&2; exit 1; }
for tool in jq openssl aws; do
  command -v "$tool" >/dev/null || { echo "$tool isn't installed" >&2; exit 1; }
done

# The key names the secret has now, never their values.
have=$(aws secretsmanager get-secret-value --secret-id "$secret" --query SecretString --output text 2>/dev/null | jq -r 'keys[]' 2>/dev/null || true)
has() { grep -qx "$1" <<<"$have"; }
rotating() { local g; for g in "${rotate[@]}"; do [[ $g == "$1" ]] && return 0; done; return 1; }

dir=$(mktemp -d)
chmod 700 "$dir"
trap 'rm -rf "$dir"' EXIT
args=()
password() { openssl rand -hex 24 >"$dir/$1"; }
# put writes a key's value from a file of the directory.
put() { args+=("$1=@$dir/$2"); }
# connection writes a PostgreSQL connection string for a database and its user. The .NET images
# are chiseled, without Kerberos, so GSS encryption is off rather than tried first.
connection() { printf 'Host=postgres;Port=5432;Database=%s;Username=%s;Password=%s;GSS Encryption Mode=Disable' "$2" "$3" "$(cat "$dir/$4")" >"$dir/$1"; }

# The superuser: PostgreSQL reads its password as a file, postgres-config logs in with it.
if ! has Postgres_POSTGRES_PASSWORD || rotating postgres; then
  password postgres
  put Postgres_POSTGRES_PASSWORD postgres
  connection postgres-cs postgres postgres postgres
  put PostgresConfig_ConnectionStrings__postgres postgres-cs
fi

# Each database's user: postgres-config gives it the password in its connection string, before
# the components that use it get the new one.
for db in keycloak auth protofast; do
  key="Shared_ConnectionStrings__$db"
  if ! has "$key" || rotating "$db-db"; then
    password "$db"
    connection "$db-cs" "$db" "$db" "$db"
    put "$key" "$db-cs"
  fi
done

if ! has Redis_REDIS_PASSWORD || rotating redis; then
  password redis
  put Redis_REDIS_PASSWORD redis
  printf 'redis:6379,password=%s' "$(cat "$dir/redis")" >"$dir/redis-cs"
  put Shared_ConnectionStrings__redis redis-cs
fi

if ! has Keycloak_KC_BOOTSTRAP_ADMIN_CLIENT_SECRET; then
  password keycloak-admin
  put Keycloak_KC_BOOTSTRAP_ADMIN_CLIENT_SECRET keycloak-admin
fi

# Social sign-in's credentials, which keycloak-config reads as files, so each must exist. The
# providers ship disabled: empty until an Org Admin sets real ones with set-secret.sh.
for social in GOOGLE_CLIENT_ID GOOGLE_CLIENT_SECRET APPLE_CLIENT_ID APPLE_TEAM_ID APPLE_KEY_ID APPLE_PRIVATE_KEY; do
  if ! has "Keycloak_$social"; then
    : >"$dir/$social"
    put "Keycloak_$social" "$social"
  fi
done

# The realms' confidential clients: keycloak-config sets each on its client, and auth, which
# waits for keycloak-config, signs in with it.
for client in ClientSecretProtofastWeb ClientSecretTheplotWeb ClientSecretAdmin AdminClientSecret; do
  key="Shared_Keycloak__$client"
  if ! has "$key" || rotating clients; then
    password "$client"
    put "$key" "$client"
  fi
done

# The internal JWT: auth signs with the private key, payments and the api verify with the
# public one. A new pair gets a new kid, so the backends' cached key is never mistaken for it.
if ! has Auth_InternalJwt__PrivateKeyPem || rotating internal-jwt; then
  openssl ecparam -name prime256v1 -genkey -noout 2>/dev/null | openssl pkcs8 -topk8 -nocrypt -out "$dir/jwt-private" 2>/dev/null
  openssl ec -in "$dir/jwt-private" -pubout -out "$dir/jwt-public" 2>/dev/null
  printf '%s-%s' "$environment" "$(date -u +%Y%m%d%H%M)" >"$dir/jwt-kid"
  put Auth_InternalJwt__PrivateKeyPem jwt-private
  put Shared_InternalJwt__PublicKeyPem jwt-public
  put Auth_InternalJwt__KeyId jwt-kid
fi

if [[ ${#args[@]} -eq 0 ]]; then
  echo "$secret has every key already; name a group with --rotate to make new values"
  exit 0
fi
bash "$source_dir/scripts/set-secret.sh" "$secret" "${args[@]}"
echo "Set ${#args[@]} keys in $secret: ${args[*]%%=*}"
