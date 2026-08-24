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
      expression  = "(http.request.uri.path.extension in {\"js\" \"mjs\" \"css\" \"woff2\" \"woff\" \"ttf\" \"png\" \"jpg\" \"jpeg\" \"webp\" \"avif\" \"svg\" \"ico\" \"gif\"})"
      action      = "set_cache_settings"
      action_parameters = {
        cache = true
        edge_ttl = {
          mode    = "override_origin"
          default = 31536000
          # This rule matches on file extension alone, so an origin error for an asset-looking
          # path lands here too. Without this carve-out a transient 4xx/5xx is pinned at the edge
          # for the full year (0 = no-cache: keep revalidating with the origin instead).
          status_code_ttl = [
            {
              status_code_range = {
                from = 400
                to   = 599
              }
              value = 0
            }
          ]
        }
        # respect_origin, not override_origin: the SSR host already serves hashed assets with
        # `Cache-Control: public, max-age=31536000` (express.static maxAge '1y'), so nothing is
        # lost — while an error response keeps its own headers instead of being cached in the
        # browser for a year, where no cache purge can reach it. Browser TTL has no per-status
        # setting, so deferring to the origin is the only way to scope it.
        browser_ttl = {
          mode = "respect_origin"
        }
      }
    },
  ]
}
