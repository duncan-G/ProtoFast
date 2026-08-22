# Amazon SES as Keycloak's SMTP provider for transactional auth email (account
# verification, password reset, etc.). Keycloak only speaks SMTP, so we use SES's
# SMTP submission interface (email-smtp.<region>.amazonaws.com:587, STARTTLS).
#
# Terraform stands up the sending IDENTITY only: the domain identity (Easy DKIM),
# a custom MAIL FROM subdomain (SPF/DMARC alignment), and a configuration set.
#
# The SES SMTP credential is a long-lived IAM access key, which needs an IAM user
# — exactly what the permissions boundary bans (infra/bootstrap/boundary.tf,
# "DenyIamUsersAndKeys": no IAM users, no static keys, OIDC/SSO only). That deny
# caps both this CI/infra plane and the PlatformAdmin people, so the SENDER is not
# here: the user and its least-privilege send policy are Terraform resources of
# infra/identity-center (ses-sender.tf), the one root applied by OrgAdmin, who is
# not boundary-capped. That root's ses_sender_zone / ses_from_local_part must match
# var.cloudflare_zone and var.ses_from_local_part below, or the policy's
# ses:FromAddress condition stops matching and Keycloak mail fails to send.
#
# The ACCESS KEY alone is created out of band (OrgAdmin, once per environment),
# which keeps the no-secrets-in-state rule (infra/secrets.tf) intact: no key ever
# passes through CI or lands in any state file. Runbook, with commands, in
# infra/README.md section 4.2 — mint the key, run scripts/ses-smtp-password.sh to
# derive the SMTP password, then store Auth_Smtp__Host / Auth_Smtp__User /
# Auth_Smtp__Password / Auth_Smtp__From via scripts/populate-secrets.sh. deploy.sh
# seeds those into .env, and the Keycloak realm import reads them.
#
# Do not drive that runbook off terraform outputs from this root: CI owns the S3
# state, so a local checkout has none.
#
# The DNS records that prove ownership + enable DKIM/SPF/DMARC live in
# infra/cloudflare.tf (the zone is authoritative in Cloudflare).

locals {
  ses_from_address = "${var.ses_from_local_part}@${var.cloudflare_zone}"
  ses_smtp_host    = "email-smtp.${var.aws_region}.amazonaws.com"
  ses_mail_from    = "${var.ses_mail_from_subdomain}.${var.cloudflare_zone}"
}

# Verify the apex zone with Easy DKIM (2048-bit). The DKIM CNAMEs both prove
# ownership and sign outbound mail, so no separate _amazonses TXT is needed.
resource "aws_sesv2_email_identity" "domain" {
  count          = var.enable_ses ? 1 : 0
  email_identity = var.cloudflare_zone

  dkim_signing_attributes {
    next_signing_key_length = "RSA_2048_BIT"
  }
}

# Custom MAIL FROM subdomain (e.g. bounce.<zone>) so the envelope-from aligns
# under our own domain — required for a clean SPF pass and DMARC alignment.
# REJECT on MX failure fails loudly rather than silently falling back to the
# amazonses.com envelope (which would break DMARC alignment).
resource "aws_sesv2_email_identity_mail_from_attributes" "domain" {
  count                  = var.enable_ses ? 1 : 0
  email_identity         = aws_sesv2_email_identity.domain[0].email_identity
  mail_from_domain       = local.ses_mail_from
  behavior_on_mx_failure = "REJECT_MESSAGE"
}

# Optional config set: turns on reputation metrics (bounce/complaint tracking).
# Keycloak's SMTP client can't name a config set per-send, so this is here for
# account hygiene/observability rather than wired into the send path.
resource "aws_sesv2_configuration_set" "this" {
  count                  = var.enable_ses ? 1 : 0
  configuration_set_name = "${var.project}-ses"

  reputation_options {
    reputation_metrics_enabled = true
  }

  sending_options {
    sending_enabled = true
  }
}

# NOTE: the SES sender IAM user and its send policy are intentionally NOT managed
# here — the permissions boundary bars this plane from minting IAM users. They are
# infra/identity-center/ses-sender.tf; its access key is minted out of band. See
# the file header.
