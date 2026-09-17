# Application secrets (§4.4). Two Secrets Manager secrets, same JSON-map layout
# (keys prefixed Infra_ / Auth_ / Payments_ / Api_ / Shared_):
#
#   ${var.project}/app  — production. Read by the instance role (iam.tf) and by
#                         auth in-process. Written by OrgAdmin via
#                         scripts/populate-secrets.sh.
#   ${var.project}/dev  — local AppHost. Read/written by the Developer SSO
#                         permission set (infra/identity-center). The instance
#                         profile does NOT get this secret.
#
# Terraform (run by the GitHub Actions infra role) creates ONLY the empty secret
# shells — it never manages a version, so no secret value, not even a placeholder,
# ever passes through CI or lands in Terraform state. The infra role is explicitly
# denied secretsmanager:GetSecretValue / PutSecretValue (see infra/bootstrap
# roles.tf "AppSecretShell" + "DenyAppSecretValues"); the CI plane can create/
# describe/tag/delete either resource but can neither read nor write its contents.
# The bootstrap allow/deny is scoped to ${var.project}/*, so both names are covered.
#
# The first (and every subsequent) version is written OUT OF BAND — via the
# Secrets Manager console or scripts/populate-secrets.sh — by an operator whose
# identity can call the value APIs (OrgAdmin for /app; Developer SSO for /dev).
# NOTE: because no value is in state, if a secret is ever REPLACED (its name
# changes, or it is tainted/destroyed) the values are gone and must be re-created
# by re-running scripts/populate-secrets.sh.
#
# special chars: values flow through a ';'/'=' delimited blob (and downstream
# through a Postgres connection string + JDBC URL), so generated secrets must
# avoid ';', '=' and shell/URL metacharacters. populate-secrets.sh enforces this.

resource "aws_secretsmanager_secret" "app" {
  name                    = "${var.project}/app"
  description             = "ProtoFast platform secrets (production)"
  recovery_window_in_days = 7
}

resource "aws_secretsmanager_secret" "dev" {
  name                    = "${var.project}/dev"
  description             = "ProtoFast platform secrets (development)"
  recovery_window_in_days = 7
}
