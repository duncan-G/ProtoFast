# Application secrets (§4.4). A SINGLE Secrets Manager secret holds every secret
# the platform needs, as a ';'-separated list of `Service_Key=value` pairs:
#
#   Infra_KcDbPassword=...;Auth_DbPassword=...;Shared_RedisPassword=...;Payments_...
#
# Prefixes scope a value to a service: Infra_, Auth_, Payments_, Api_, Shared_.
# A host/service pulls the one secret and keeps only the pairs whose key starts
# with its own prefix (plus Shared_), stripping the prefix.
#
# Terraform (run by the GitHub Actions infra role) creates ONLY the empty secret
# shell — it never manages a version, so no secret value, not even a placeholder,
# ever passes through CI or lands in Terraform state. The infra role is explicitly
# denied secretsmanager:GetSecretValue / PutSecretValue (see infra/bootstrap
# roles.tf "AppSecretShell" + "DenyAppSecretValues"); the CI plane can create/
# describe/tag/delete the resource but can neither read nor write its contents.
#
# The first (and every subsequent) version is written OUT OF BAND — via the
# Secrets Manager console or scripts/populate-secrets.sh — by an operator whose
# identity carries the boundary but whose own grants include the value APIs. Run
# the script once after the first `terraform apply`, and again to add/rotate keys.
# There is exactly one copy of each secret, living solely in Secrets Manager.
# NOTE: because no value is in state, if this secret is ever REPLACED (its name
# changes, or it is tainted/destroyed) the values are gone and must be re-created
# by re-running scripts/populate-secrets.sh.
#
# special chars: values flow through a ';'/'=' delimited blob (and downstream
# through a Postgres connection string + JDBC URL), so generated secrets must
# avoid ';', '=' and shell/URL metacharacters. populate-secrets.sh enforces this.

resource "aws_secretsmanager_secret" "app" {
  name                    = "${var.project}/app"
  description             = "ProtoFast platform secrets"
  recovery_window_in_days = 7
}
