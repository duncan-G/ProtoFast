#!/usr/bin/env bash
# Derive an Amazon SES SMTP password from an IAM secret access key.
#
# SES SMTP auth does NOT use the raw IAM secret access key: the password is a
# SigV4 signature derived from it (AWS docs: "Obtaining Amazon SES SMTP
# credentials by converting existing AWS credentials"). The SMTP *username* is
# just the IAM access key ID.
#
# We derive it locally rather than letting Terraform generate it, so no secret
# value ever lands in Terraform state (see infra/secrets.tf). The SES sender IAM
# user itself is also created out of band by an OrgAdmin (the permissions boundary
# bars Terraform/CI from minting IAM users + keys — see infra/ses.tf runbook).
# Create an access key for that user (name: terraform output ses_smtp_iam_user),
# then:
#
#   scripts/ses-smtp-password.sh <SECRET_ACCESS_KEY> [REGION]
#
# and store the two values in the app secret:
#
#   scripts/populate-secrets.sh \
#     Auth_Smtp__Host="$(terraform -chdir=infra output -raw ses_smtp_endpoint)" \
#     Auth_Smtp__From="$(terraform -chdir=infra output -raw ses_from_address)" \
#     Auth_Smtp__User="<access-key-id>" \
#     Auth_Smtp__Password="$(scripts/ses-smtp-password.sh <secret-access-key>)"
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
