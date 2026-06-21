# CDN cache rules: cache Angular's hashed,
# immutable bundles aggressively; bypass SSR HTML and the gRPC-Web / OTLP API
# paths. gRPC-Web POSTs are never cached by default, so the bypass rule is
# belt-and-suspenders.

resource "cloudflare_ruleset" "cache" {
  zone_id = data.cloudflare_zone.this.id
  name    = "${var.project}-cache"
  kind    = "zone"
  phase   = "http_request_cache_settings"

  rules = [
    # Bypass cache for API / gRPC-Web / telemetry ingest and any non-GET.
    {
      ref         = "bypass_dynamic"
      description = "Bypass cache for gRPC-Web, API, OTLP, and non-GET requests"
      expression  = "(starts_with(http.request.uri.path, \"/auth/\")) or (starts_with(http.request.uri.path, \"/payments/\")) or (starts_with(http.request.uri.path, \"/api/\")) or (starts_with(http.request.uri.path, \"/otlp/\")) or (http.request.method ne \"GET\")"
      action      = "set_cache_settings"
      action_parameters = {
        cache = false
      }
    },
    # Cache content-hashed static assets for a year (immutable).
    {
      ref         = "cache_hashed_assets"
      description = "Cache Angular hashed bundles and static assets aggressively"
      expression  = "(http.request.uri.path matches \"\\\\.[0-9a-f]{8,}\\\\.(js|css|mjs|woff2|woff|ttf|png|jpg|jpeg|webp|avif|svg|ico|gif)$\") or (http.request.uri.path matches \"\\\\.(woff2|woff|ttf)$\")"
      action      = "set_cache_settings"
      action_parameters = {
        cache = true
        edge_ttl = {
          mode    = "override_origin"
          default = 31536000
        }
        browser_ttl = {
          mode    = "override_origin"
          default = 31536000
        }
      }
    },
  ]
}
