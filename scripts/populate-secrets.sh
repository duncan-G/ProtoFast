#!/usr/bin/env bash
# Populate / rotate the single ProtoFast app secret (§4.4).
#
# Secret values live ONLY in Secrets Manager — never in Terraform state. Terraform
# creates the empty shell with NO version (the CI role is denied the value APIs);
# this script writes the first and every subsequent version out of band — so the
# secret has no version at all until you run it. Run it once after the first
# `terraform apply`, and
# again whenever you add a key or rotate a value. It is additive + idempotent:
# existing keys are preserved unless you pass them explicitly, and any auto-managed
# key that is still missing is generated.
#
# Format: a JSON key/value map — the native Secrets Manager layout the console
# produces and that the instances read (deploy.sh, cloud-init). Keys are prefixed
# to scope a value to a service: Infra_, Auth_, Payments_, Api_, Shared_. A legacy
# ';'-separated blob from an older version is auto-migrated to a map on the next run.
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

# Pull the current value (treat the placeholder / a brand-new secret as empty).
current=$(aws secretsmanager get-secret-value --secret-id "$SECRET_ID" \
  --query SecretString --output text 2>/dev/null || true)
[ "$current" = "PLACEHOLDER" ] && current=""

# Merge in python so values with arbitrary characters round-trip safely: start from
# the existing map, apply explicit Key=value overrides, then fill any missing managed
# key with a fresh 32-char (A-Za-z0-9) password. A legacy ';'-separated blob is
# parsed and migrated to a map. Result is emitted as a compact JSON object.
blob=$(CURRENT="$current" MANAGED="$MANAGED_KEYS" python3 - "$@" <<'PY'
import json, os, secrets, string, sys

raw = os.environ.get("CURRENT", "")
try:
    kv = json.loads(raw) if raw else {}
    if not isinstance(kv, dict):
        kv = {}
except ValueError:
    # legacy ';'-separated Key=value blob -> migrate to a map
    kv = {}
    for pair in raw.split(";"):
        if "=" in pair:
            k, v = pair.split("=", 1)
            if k:
                kv[k] = v

for arg in sys.argv[1:]:
    if "=" in arg:
        k, v = arg.split("=", 1)
        kv[k] = v
    else:
        sys.stderr.write("ignoring malformed arg (expected Key=value): %s\n" % arg)

alphabet = string.ascii_letters + string.digits
for k in os.environ.get("MANAGED", "").split():
    if not kv.get(k):
        kv[k] = "".join(secrets.choice(alphabet) for _ in range(32))
        sys.stderr.write("generated %s\n" % k)

json.dump(kv, sys.stdout, sort_keys=True, separators=(",", ":"))
PY
)

aws secretsmanager put-secret-value --secret-id "$SECRET_ID" --secret-string "$blob" >/dev/null
echo "wrote $(printf '%s' "$blob" | python3 -c 'import json,sys; print(len(json.load(sys.stdin)))') keys to $SECRET_ID"
