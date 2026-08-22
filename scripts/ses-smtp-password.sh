#!/usr/bin/env bash
# Derive an Amazon SES SMTP password from an IAM secret access key.
#
# SES SMTP auth does NOT use the raw IAM secret access key: the password is a
# SigV4 signature derived from it (AWS docs: "Obtaining Amazon SES SMTP
# credentials by converting existing AWS credentials"). The SMTP *username* is
# just the IAM access key ID.
#
# We derive it locally rather than letting Terraform generate it, so no secret
# value ever lands in Terraform state (see infra/secrets.tf). The sender IAM user
# is Terraform, but in infra/identity-center (ses-sender.tf) — the permissions
# boundary bars the infra/CI plane from minting IAM users. Only the access key is
# manual. Create one for user protofast-ses-smtp, then:
#
#   scripts/ses-smtp-password.sh <SECRET_ACCESS_KEY> [REGION]
#
# and store the values in the app secret. HOST is email-smtp.<region>.amazonaws.com;
# read FROM off the send policy so it matches what the user may send as (the full
# copy-paste block is in infra/README.md section 4.2):
#
#   scripts/populate-secrets.sh \
#     Auth_Smtp__Host="email-smtp.<region>.amazonaws.com" \
#     Auth_Smtp__From="<ses:FromAddress from the ses-send policy>" \
#     Auth_Smtp__User="<access-key-id>" \
#     Auth_Smtp__Password="$(scripts/ses-smtp-password.sh <secret-access-key> <region>)"
#
# REGION defaults to $AWS_REGION, then us-east-1. It MUST be the region the SES
# identity lives in (the SMTP endpoint is region-specific).
set -euo pipefail

SECRET_ACCESS_KEY="${1:-}"
REGION="${2:-${AWS_REGION:-us-east-1}}"

if [ -z "$SECRET_ACCESS_KEY" ]; then
  echo "usage: $0 <IAM_SECRET_ACCESS_KEY> [REGION]" >&2
  exit 2
fi

# The derivation is a fixed SigV4 chain (date '11111111', service 'ses', message
# 'SendRawEmail', terminal 'aws4_request'), version byte 0x04 prepended, base64.
# The secret is passed via env, never argv, so it can't leak through `ps`.
SES_SECRET="$SECRET_ACCESS_KEY" SES_REGION="$REGION" python3 - <<'PY'
import base64, hashlib, hmac, os, sys

secret = os.environ["SES_SECRET"]
region = os.environ["SES_REGION"]

date = "11111111"
service = "ses"
message = "SendRawEmail"
terminal = "aws4_request"
version = 0x04

def sign(key, msg):
    return hmac.new(key, msg.encode("utf-8"), hashlib.sha256).digest()

sig = sign(("AWS4" + secret).encode("utf-8"), date)
sig = sign(sig, region)
sig = sign(sig, service)
sig = sign(sig, terminal)
sig = sign(sig, message)

sys.stdout.write(base64.b64encode(bytes([version]) + sig).decode("utf-8"))
PY
