#!/usr/bin/env bash
# Fills protofast/prod with every key prod needs: the generated ones (scripts/set-secrets.sh),
# the edge's tunnel token, and the model APIs' keys, typed without echo. Safe to re-run: it
# only sets what the secret lacks. Run from the ProtoFast repo root as an Org Admin.
#
#   AWS_PROFILE=<org-admin> CLOUDFLARE_ACCOUNT_ID=... CLOUDFLARE_API_TOKEN=... scripts/fill-prod-secret.sh
#
# Env: AWS_PROFILE, AWS_REGION (default us-west-2), PROTOSTRAP_SOURCE (default ../ProtoStrap),
# CLOUDFLARE_ACCOUNT_ID + CLOUDFLARE_API_TOKEN (Tunnel:Edit) for the tunnel token.
set -euo pipefail

export AWS_REGION=${AWS_REGION:-us-west-2}
secret=protofast/prod
root=$(git rev-parse --show-toplevel)
protostrap=${PROTOSTRAP_SOURCE:-$root/../ProtoStrap}
export PROTOSTRAP_SOURCE=$protostrap

for tool in jq openssl aws curl; do
  command -v "$tool" >/dev/null || { echo "$tool isn't installed" >&2; exit 1; }
done
[[ -f "$protostrap/scripts/set-secret.sh" ]] || { echo "no ProtoStrap checkout at $protostrap" >&2; exit 1; }
aws sts get-caller-identity --query Arn --output text >/dev/null || { echo "no AWS credentials: aws sso login --profile \$AWS_PROFILE" >&2; exit 1; }
aws secretsmanager describe-secret --secret-id "$secret" >/dev/null || { echo "$secret doesn't exist: apply infra/prod first" >&2; exit 1; }

keys() { aws secretsmanager get-secret-value --secret-id "$secret" --query SecretString --output text 2>/dev/null | jq -r 'keys[]' 2>/dev/null || true; }
has() { grep -qx "$1" <<<"$(keys)"; }
# set-secret.sh rewrites the whole secret from a read, and a read just after a write can be the
# old version, so each step waits until its key shows before the next one writes.
settle() { local i; for i in $(seq 30); do has "$1" && return 0; sleep 2; done; echo "$1 didn't appear in $secret" >&2; exit 1; }
ask() { local reply; read -r -p "$1 [y/N] " reply </dev/tty; [[ $reply == [yY]* ]]; }

echo "== 1/3 generated keys (passwords, connection strings, Keycloak clients, internal JWT)"
bash "$root/scripts/set-secrets.sh" prod
settle Auth_InternalJwt__KeyId

echo "== 2/3 edge tunnel token"
if has Edge_TUNNEL_TOKEN; then
  echo "Edge_TUNNEL_TOKEN already set"
elif [[ -n ${CLOUDFLARE_ACCOUNT_ID:-} && -n ${CLOUDFLARE_API_TOKEN:-} ]]; then
  bash "$protostrap/scripts/set-edge-token.sh" protofast prod
  settle Edge_TUNNEL_TOKEN
else
  echo "skipped: set CLOUDFLARE_ACCOUNT_ID and CLOUDFLARE_API_TOKEN and re-run (needs the tunnel from the first infra apply)" >&2
fi

echo "== 3/3 model API keys"
args=()
for provider in deepseek gemini; do
  key="DocumentImport_Providers__${provider}__ApiKey"
  has "$key" || args+=("$key=-")
done
key=DocumentImport_Providers__anthropic__ApiKey
if ! has "$key" && ask "Set the Anthropic API key (only if prod routes work to Anthropic)?"; then
  args+=("$key=-")
fi
if ! has Api_Providers__deepseek__ApiKey && ask "Set Api_Providers__deepseek__ApiKey, the DeepSeek key OffTheLoop's notes write with?"; then
  args+=("Api_Providers__deepseek__ApiKey=-")
fi
if ! has Api_Providers__gemini__ApiKey && ask "Set Api_Providers__gemini__ApiKey, the Gemini key OffTheLoop researches companies and postings with?"; then
  args+=("Api_Providers__gemini__ApiKey=-")
fi
if ! has Reporter_ANTHROPIC_API_KEY && ask "Set Reporter_ANTHROPIC_API_KEY for ProtoStrap's daily report?"; then
  args+=("Reporter_ANTHROPIC_API_KEY=-")
fi
if [[ ${#args[@]} -gt 0 ]]; then
  bash "$protostrap/scripts/set-secret.sh" "$secret" "${args[@]}"
else
  echo "model keys already set"
fi

echo "== check"
have=$(keys)
missing=0
for key in \
  Postgres_POSTGRES_PASSWORD PostgresConfig_ConnectionStrings__postgres \
  Shared_ConnectionStrings__keycloak Shared_ConnectionStrings__auth Shared_ConnectionStrings__protofast \
  Redis_REDIS_PASSWORD Shared_ConnectionStrings__redis Keycloak_KC_BOOTSTRAP_ADMIN_CLIENT_SECRET \
  Shared_Keycloak__ClientSecretProtofastWeb Shared_Keycloak__ClientSecretTheplotWeb \
  Shared_Keycloak__ClientSecretOfftheloopWeb \
  Shared_Keycloak__ClientSecretAdmin Shared_Keycloak__AdminClientSecret \
  Auth_InternalJwt__PrivateKeyPem Auth_InternalJwt__KeyId \
  Api_InternalJwt__PublicKeyPem Payments_InternalJwt__PublicKeyPem \
  Edge_TUNNEL_TOKEN DocumentImport_Providers__deepseek__ApiKey DocumentImport_Providers__gemini__ApiKey; do
  grep -qx "$key" <<<"$have" || { echo "missing: $key" >&2; missing=1; }
done
[[ $missing -eq 0 ]] && echo "$secret has every required key" || exit 1
