# Cloudflare is registrar + authoritative DNS + edge. The
# zone already exists — reference it, never create it. Terraform owns the tunnel,
# its ingress config, the per-hostname DNS records, and zone TLS settings.

data "cloudflare_zone" "this" {
  filter = {
    name = var.cloudflare_zone
  }
}

locals {
  # Public hostname → internal origin. Client domains hit Envoy's publish listener;
  # telemetry (if enabled) hits the Aspire Dashboard, gated by Access (access.tf).
  client_hostnames = {
    admin     = var.admin_domain
    protofast = var.protofast_domain
  }

  telemetry_enabled = var.telemetry_domain != "" && length(var.telemetry_access_emails) > 0

  # Keycloak's login/OIDC endpoints, reachable through the tunnel on their own
  # domain. Routed (like the clients) to Envoy's publish listener, where the
  # optional Keycloak vhost forwards to Host B's published Keycloak port (§3.1).
  keycloak_enabled = var.keycloak_domain != ""

  # All hostnames that need a proxied CNAME to the tunnel.
  tunnel_hostnames = merge(
    local.client_hostnames,
    local.telemetry_enabled ? { telemetry = var.telemetry_domain } : {},
    local.keycloak_enabled ? { keycloak = var.keycloak_domain } : {},
  )
}

# Tunnel secret (the tunnel and cloudflared share it; the run token is derived).
resource "random_id" "tunnel_secret" {
  byte_length = 35
}

resource "cloudflare_zero_trust_tunnel_cloudflared" "this" {
  account_id    = var.cloudflare_account_id
  name          = "${var.project}-tunnel"
  tunnel_secret = random_id.tunnel_secret.b64_std
  config_src    = "cloudflare"
}

# Cloudflare provider v5 removed the tunnel's `tunnel_token` attribute, and the
# replacement data source doesn't actually return a token (cloudflare/
# terraform-provider-cloudflare#5149). Construct the run token ourselves: it's
# base64(json({a: account, t: tunnel id, s: secret})), where `s` is the same
# base64 secret fed to the tunnel above.
locals {
  tunnel_token = base64encode(jsonencode({
    a = var.cloudflare_account_id
    t = cloudflare_zero_trust_tunnel_cloudflared.this.id
    s = random_id.tunnel_secret.b64_std
  }))
}

# Ingress: every client hostname → https://envoy:8443 with noTLSVerify (the cert
# is the baked self-signed one; traffic never leaves the Docker network). The
# final catch-all 404 rule is required by Cloudflare.
resource "cloudflare_zero_trust_tunnel_cloudflared_config" "this" {
  account_id = var.cloudflare_account_id
  tunnel_id  = cloudflare_zero_trust_tunnel_cloudflared.this.id

  config = {
    ingress = concat(
      [for hostname in local.client_hostnames : {
        hostname = hostname
        service  = "https://envoy:8443"
        origin_request = {
          no_tls_verify    = true
          http_host_header = hostname
        }
      }],

      # Telemetry hostname → Aspire Dashboard UI (auth enforced at the edge by Access).
      local.telemetry_enabled ? [{
        hostname = var.telemetry_domain
        service  = "http://aspire-dashboard:18888"
      }] : [],

      # Keycloak hostname → Envoy publish listener (its Keycloak vhost forwards to
      # Host B). noTLSVerify + Host header so Envoy matches the keycloak vhost.
      local.keycloak_enabled ? [{
        hostname = var.keycloak_domain
        service  = "https://envoy:8443"
        origin_request = {
          no_tls_verify    = true
          http_host_header = var.keycloak_domain
        }
      }] : [],

      # Catch-all 404 rule (required by Cloudflare).
      [{
        service = "http_status:404"
      }],
    )
  }
}

# Proxied CNAMEs → <tunnel-id>.cfargotunnel.com (orange-cloud: CDN/WAF/TLS at edge).
resource "cloudflare_dns_record" "tunnel" {
  for_each = local.tunnel_hostnames

  zone_id = data.cloudflare_zone.this.id
  name    = each.value
  content = "${cloudflare_zero_trust_tunnel_cloudflared.this.id}.cfargotunnel.com"
  type    = "CNAME"
  proxied = true
  ttl     = 1 # required; must be 1 (automatic) for proxied records
}

# Always-HTTPS + Full TLS (edge validates the tunnel, not a public origin cert).
# v5 manages each zone setting as its own resource.
resource "cloudflare_zone_setting" "always_use_https" {
  zone_id    = data.cloudflare_zone.this.id
  setting_id = "always_use_https"
  value      = "on"
}

resource "cloudflare_zone_setting" "ssl" {
  zone_id    = data.cloudflare_zone.this.id
  setting_id = "ssl"
  value      = "full"
}
