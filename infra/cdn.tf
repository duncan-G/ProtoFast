# CDN cache rules: cache Angular's hashed,
# immutable bundles aggressively; bypass SSR HTML and the gRPC-Web / OTLP API
# paths. gRPC-Web POSTs are never cached by default, so the bypass rule is
# belt-and-suspenders.
#
# Edge TTL respects origin Cache-Control. The previous override_origin + 1y
# treated every .css/.js on the zone as an immutable hashed Angular bundle,
# including Keycloak theme files whose URL only moves when the Keycloak image
# tag does. Origin headers are the source of truth: hashed SSR assets already
# send max-age=31536000, Keycloak sends whatever spi-theme-static-max-age is.

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
    # Eligible for cache; TTL comes from the origin, not a zone-wide 1y override.
    {
      ref         = "cache_hashed_assets"
      description = "Cache static assets according to origin Cache-Control"
      expression  = "(http.request.uri.path.extension in {\"js\" \"mjs\" \"css\" \"woff2\" \"woff\" \"ttf\" \"png\" \"jpg\" \"jpeg\" \"webp\" \"avif\" \"svg\" \"ico\" \"gif\"})"
      action      = "set_cache_settings"
      action_parameters = {
        cache = true
        edge_ttl = {
          mode = "respect_origin"
          # This rule matches on file extension alone, so an origin error for an
          # asset-looking path lands here too. Without this carve-out a transient
          # 4xx/5xx would follow origin (or be cached) instead of revalidating.
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
        # Same as edge: hashed SSR assets send max-age=1y; unhashed Keycloak
        # theme files send a short max-age. A browser cache of 1y cannot be
        # purged, so the origin header has to be the one that is right.
        browser_ttl = {
          mode = "respect_origin"
        }
      }
    },
  ]
}
