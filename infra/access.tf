# Cloudflare Access in front of the Aspire Dashboard
# The dashboard is never exposed unauthenticated — Access enforces SSO/email
# at the edge before the tunnel hostname resolves to it. Enabled only when a
# telemetry domain + allow-list are configured.

resource "cloudflare_zero_trust_access_application" "telemetry" {
  count = local.telemetry_enabled ? 1 : 0

  account_id                = var.cloudflare_account_id
  name                      = "${var.project} telemetry dashboard"
  domain                    = var.telemetry_domain
  type                      = "self_hosted"
  session_duration          = "8h"
  app_launcher_visible      = false
  auto_redirect_to_identity = false

  policies = [{
    id         = cloudflare_zero_trust_access_policy.telemetry[0].id
    precedence = 1
  }]
}

resource "cloudflare_zero_trust_access_policy" "telemetry" {
  count = local.telemetry_enabled ? 1 : 0

  account_id = var.cloudflare_account_id
  name       = "allow-listed emails"
  decision   = "allow"

  include = [for email in var.telemetry_access_emails : {
    email = {
      email = email
    }
  }]
}
