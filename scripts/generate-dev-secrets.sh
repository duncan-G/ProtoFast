#!/usr/bin/env bash
# Generate the local protofast/dev secrets and write them via populate-secrets.sh.
#
# Fills the keys local .NET services read from Secrets Manager (JWT pair + Keycloak
# client secrets, including ThePlot's realm). Provider API keys and Stripe are not
# generated — pass them as extra Key=value args. SES SMTP and the production DB
# passwords are not generated — smtp4dev and Aspire Postgres cover those on the laptop.
#
# Usage:
#   scripts/generate-dev-secrets.sh
#   scripts/generate-dev-secrets.sh Payments_StripeKey=sk_test_...
#   scripts/generate-dev-secrets.sh Seg_Providers__anthropic__ApiKey=sk-...
#
# Re-run to rotate the JWT pair. Extra Key=value args are forwarded and win.
# Env: AWS_PROFILE (use `developer`), AWS_REGION, SECRET_ID — same as populate-secrets.sh.
set -euo pipefail

if ! command -v openssl >/dev/null; then
  echo "openssl is required to generate the internal JWT key pair" >&2
  exit 1
fi

script_dir=$(cd "$(dirname "$0")" && pwd)
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

openssl genpkey -algorithm EC -pkeyopt ec_paramgen_curve:P-256 -out "$tmp/jwt-private.pem" 2>/dev/null
openssl pkey -in "$tmp/jwt-private.pem" -pubout -out "$tmp/jwt-public.pem"

"$script_dir/populate-secrets.sh" \
  Auth_Keycloak__ClientSecretProtofastWeb=dev-protofast-web-secret \
  Auth_Keycloak__ClientSecretAdmin=dev-admin-secret \
  Auth_Keycloak__ClientSecretTheplotWeb=dev-theplot-web-secret \
  Auth_Keycloak__AdminClientSecret=dev-account-admin-secret \
  Auth_Keycloak__AdminClientSecretByRealm__theplot=dev-theplot-account-admin-secret \
  Auth_InternalJwt__PrivateKeyPem="$(cat "$tmp/jwt-private.pem")" \
  Shared_InternalJwt__PublicKeyPem="$(cat "$tmp/jwt-public.pem")" \
  Auth_InternalJwt__KeyId=dev-1 \
  "$@"
