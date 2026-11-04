#!/usr/bin/env bash
# Fills in ProtoFast's keys in an environment's secret, protofast/<environment>
# (docs/06-secrets.md): prod's before its first deploy, which an Org Admin runs, and dev's,
# protofast/dev, which `aspire run` reads, once for the team, which a Developer runs. It runs on
# their own machine: it makes every password and key itself, in a private temporary directory,
# and hands them to ProtoStrap's set-secret.sh as files, so no value is shown, typed, logged or
# put on a command line. Everything goes in one run, one version of the secret.
#
#   scripts/set-secrets.sh prod                       # sets every key the secret lacks
#   scripts/set-secrets.sh prod --rotate redis        # makes new values for a group of keys
#   scripts/set-secrets.sh dev                        # the keys the processes read under aspire run
#
# Dev's secret holds only what the processes read: the internal JWT, and every model API key,
# empty until you set yours with set-secret.sh. The local resources' keys, the connection
# strings and PostgreSQL's, Redis's and Keycloak's, are Aspire's, so a run on dev removes any it
# finds: a process would take the secret's over Aspire's, as auth would Keycloak's client secrets.
#
# Groups for --rotate: postgres (the superuser), keycloak-db, auth-db, protofast-db, redis,
# clients (the five Keycloak client secrets), internal-jwt (the key pair and its kid). Each
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

environment=${1:?usage: scripts/set-secrets.sh <environment: prod, or dev for aspire run> [--rotate group...]}
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
dev() { [[ $environment == dev ]]; }

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
if ! dev && { ! has Postgres_POSTGRES_PASSWORD || rotating postgres; }; then
  password postgres
  put Postgres_POSTGRES_PASSWORD postgres
  connection postgres-cs postgres postgres postgres
  put PostgresConfig_ConnectionStrings__postgres postgres-cs
fi

# Each database's user: postgres-config gives it the password in its connection string, before
# the components that use it get the new one.
for db in keycloak auth protofast; do
  dev && break
  key="Shared_ConnectionStrings__$db"
  if ! has "$key" || rotating "$db-db"; then
    password "$db"
    connection "$db-cs" "$db" "$db" "$db"
    put "$key" "$db-cs"
  fi
done

if ! dev && { ! has Redis_REDIS_PASSWORD || rotating redis; }; then
  password redis
  put Redis_REDIS_PASSWORD redis
  printf 'redis:6379,password=%s' "$(cat "$dir/redis")" >"$dir/redis-cs"
  put Shared_ConnectionStrings__redis redis-cs
fi

if ! dev && ! has Keycloak_KC_BOOTSTRAP_ADMIN_CLIENT_SECRET; then
  password keycloak-admin
  put Keycloak_KC_BOOTSTRAP_ADMIN_CLIENT_SECRET keycloak-admin
fi

# The realms' confidential clients: keycloak-config sets each on its client, and auth, which
# waits for keycloak-config, signs in with it.
for client in ClientSecretProtofastWeb ClientSecretTheplotWeb ClientSecretOfftheloopWeb ClientSecretAdmin AdminClientSecret; do
  dev && break
  key="Shared_Keycloak__$client"
  if ! has "$key" || rotating clients; then
    password "$client"
    put "$key" "$client"
  fi
done

# The internal JWT: auth signs with the private key, payments and the api verify with the
# public one. A new pair gets a new kid, so the backends' cached key is never mistaken for it.
# Each verifier has its own copy of the public key, so a rotation restarts it: a Shared_ key
# restarts only the components whose spec lists it. A missing copy makes a new pair, since this
# never reads a value.
if ! has Auth_InternalJwt__PrivateKeyPem || ! has Api_InternalJwt__PublicKeyPem \
  || ! has Payments_InternalJwt__PublicKeyPem || rotating internal-jwt; then
  openssl ecparam -name prime256v1 -genkey -noout 2>/dev/null | openssl pkcs8 -topk8 -nocrypt -out "$dir/jwt-private" 2>/dev/null
  openssl ec -in "$dir/jwt-private" -pubout -out "$dir/jwt-public" 2>/dev/null
  printf '%s-%s' "$environment" "$(date -u +%Y%m%d%H%M)" >"$dir/jwt-kid"
  put Auth_InternalJwt__PrivateKeyPem jwt-private
  put Api_InternalJwt__PublicKeyPem jwt-public
  put Payments_InternalJwt__PublicKeyPem jwt-public
  put Auth_InternalJwt__KeyId jwt-kid
  has Shared_InternalJwt__PublicKeyPem && args+=("-Shared_InternalJwt__PublicKeyPem")
fi

if dev; then
  while IFS= read -r key; do
    case $key in Postgres_* | Redis_* | Keycloak_* | Shared_Keycloak__* | *ConnectionStrings__*) args+=("-$key") ;; esac
  done <<<"$have"
  # The model APIs' keys, empty until a Developer sets theirs.
  for key in DocumentImport_Providers__{deepseek,gemini,anthropic}__ApiKey Api_Providers__{deepseek,gemini}__ApiKey; do
    if ! has "$key"; then
      : >"$dir/$key"
      put "$key" "$key"
    fi
  done
fi

if [[ ${#args[@]} -eq 0 ]]; then
  echo "$secret has every key already; name a group with --rotate to make new values"
  exit 0
fi
bash "$source_dir/scripts/set-secret.sh" "$secret" "${args[@]}"
echo "Set ${#args[@]} keys in $secret: ${args[*]%%=*}"
