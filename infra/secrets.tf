# Application secrets (§4.4). A SINGLE Secrets Manager secret holds every secret
# the platform needs, as a ';'-separated list of `Service_Key=value` pairs:
#
#   Infra_KcDbPassword=...;Auth_DbPassword=...;Shared_RedisPassword=...;Payments_...
#
# Prefixes scope a value to a service: Infra_, Auth_, Payments_, Api_, Shared_.
# A host/service pulls the one secret and keeps only the pairs whose key starts
# with its own prefix (plus Shared_), stripping the prefix.
#
# The values are populated OUT OF BAND (scripts/populate-secrets.sh) so cleartext
# never lands in Terraform state — TF owns only the empty shell. There is exactly
# one copy of each secret, living solely in Secrets Manager. ignore_changes keeps
# routine `terraform apply` runs from clobbering the live values back to the
# placeholder. NOTE: because the value is not in state, if this secret is ever
# REPLACED (e.g. its name changes, or it is tainted/destroyed) the values are gone
# and must be re-created by re-running scripts/populate-secrets.sh.
#
# special chars: values flow through a ';'/'=' delimited blob (and downstream
# through a Postgres connection string + JDBC URL), so generated secrets must
# avoid ';', '=' and shell/URL metacharacters. populate-secrets.sh enforces this.

resource "aws_secretsmanager_secret" "app" {
  name                    = "${var.project}/app"
  description             = "ProtoFast platform secrets (';'-separated Service_Key=value pairs). Populated out-of-band; see scripts/populate-secrets.sh."
  recovery_window_in_days = 7
}

resource "aws_secretsmanager_secret_version" "app" {
  secret_id     = aws_secretsmanager_secret.app.id
  secret_string = "PLACEHOLDER" # real values written post-apply by scripts/populate-secrets.sh
  lifecycle {
    ignore_changes = [secret_string]
  }
}
