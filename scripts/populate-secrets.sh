#!/usr/bin/env bash
# Populate / rotate the single ProtoFast app secret (§4.4).
#
# Secret values live ONLY in Secrets Manager — never in Terraform state. Terraform
# creates the empty shell ("PLACEHOLDER", with ignore_changes); this script writes
# the real values out of band. Run it once after the first `terraform apply`, and
# again whenever you add a key or rotate a value. It is additive + idempotent:
# existing keys are preserved unless you pass them explicitly, and any auto-managed
# key that is still missing is generated.
#
# Format: ';'-separated Service_Key=value pairs. Prefixes scope a value to a
# service: Infra_, Auth_, Payments_, Api_, Shared_. Generated values avoid ';',
# '=' and shell/URL metacharacters so the blob and downstream connection strings
# stay unambiguous.
#
# Usage:
#   scripts/populate-secrets.sh                         # generate any missing managed keys
#   scripts/populate-secrets.sh Payments_StripeKey=sk_live_...   # set/override specific keys
#
# Env:
#   SECRET_ID   (default: <project>/app)  — must match aws_secretsmanager_secret.app.name
#   AWS_REGION  (default: from your AWS config)
set -euo pipefail

PROJECT="${PROJECT:-protofast}"
SECRET_ID="${SECRET_ID:-$PROJECT/app}"

# Keys this script auto-generates a 32-char password for if absent. Manually-managed
# keys (e.g. Payments_StripeKey, third-party API keys) are NOT listed here — pass
# them as CLI args.
MANAGED_KEYS="Infra_KcDbPassword Auth_DbPassword"

gen_pw() {
  # 32 url-safe chars, no ';' '=' or shell/URL metacharacters.
  LC_ALL=C tr -dc 'A-Za-z0-9' </dev/urandom | head -c 32
}

# Pull the current blob (treat the placeholder / a brand-new secret as empty).
current=$(aws secretsmanager get-secret-value --secret-id "$SECRET_ID" \
  --query SecretString --output text 2>/dev/null || true)
[ "$current" = "PLACEHOLDER" ] && current=""

# Load existing pairs into an associative array.
declare -A kv
if [ -n "$current" ]; then
  while IFS='=' read -r k v; do
    [ -n "$k" ] && kv["$k"]="$v"
  done < <(printf '%s' "$current" | tr ';' '\n')
fi

# Apply explicit CLI overrides (Key=value).
for arg in "$@"; do
  case "$arg" in
    *=*) kv["${arg%%=*}"]="${arg#*=}" ;;
    *) echo "ignoring malformed arg (expected Key=value): $arg" >&2 ;;
  esac
done

# Fill in any missing managed keys.
for k in $MANAGED_KEYS; do
  if [ -z "${kv[$k]:-}" ]; then
    kv["$k"]="$(gen_pw)"
    echo "generated $k"
  fi
done

# Reassemble the ';'-separated blob (sorted for stable diffs).
blob=""
for k in $(printf '%s\n' "${!kv[@]}" | sort); do
  blob="${blob:+$blob;}$k=${kv[$k]}"
done

aws secretsmanager put-secret-value --secret-id "$SECRET_ID" --secret-string "$blob" >/dev/null
echo "wrote $(printf '%s' "$blob" | tr ';' '\n' | grep -c '=') keys to $SECRET_ID"
