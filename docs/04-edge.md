# 04 · The edge (Envoy)

*How the proxy's configuration is produced, what it routes, and what it enforces.
Self-contained.*

> **Looking for the map?** [`docs/edge-map/`](../edge-map/index.html) is an interactive
> version of this page — entry, listeners, virtual hosts, routes and upstreams as a board you
> click through, with each hop's settings shown next to it. Open `index.html` in a browser.

## Envoy's config is generated at container start

There is no checked-in `envoy.yaml`. [`proxy/entrypoint.sh`](../../proxy/entrypoint.sh)
renders one from templates using environment variables, then execs Envoy:

```
proxy/envoy.yaml.tmpl              base config: clusters, tracing, admin
proxy/envoy.listener.yaml.tmpl     one per listener
proxy/envoy.rds.yaml.tmpl          route config wrapper
proxy/envoy.vhost.yaml.tmpl        the client virtual host (routes below)
proxy/envoy.keycloak-vhost.yaml.tmpl  the optional Keycloak allowlist vhost
proxy/envoy.cluster.yaml.tmpl      one per upstream
```

The entrypoint fails fast: every required variable is checked, and an unknown
`ENVOY_MODE` aborts the container.

## The three modes

| `ENVOY_MODE` | Used by | Shape |
|---|---|---|
| `dev` | `aspire run` | one HTTPS listener per client on `CLIENT_<NAME>_LISTENER_PORT`; catch-all → that client's `ng serve` |
| `dev-host` | `SsrHost__Dev=true aspire run` | same listeners; catch-all → the unified SSR host with an `x-client` header |
| `publish` | production | one listener on `PORT` (8443); one virtual host per `CLIENT_<NAME>_DOMAIN`, all → the SSR host |

In `publish` mode the `DEFAULT_CLIENT`'s virtual host also claims `*`, so an
unmatched Host header still gets a sensible site.

## Environment contract

| Variable | Meaning |
|---|---|
| `ENVOY_MODE` | `dev` \| `dev-host` \| `publish` |
| `CLIENTS`, `DEFAULT_CLIENT` | which clients exist; which one is the fallback |
| `PORT` (publish) / `CLIENT_<NAME>_LISTENER_PORT` (dev) | listener ports |
| `CLIENT_<NAME>_DOMAIN` (publish) | the vhost domain for that client |
| `CLIENT_<NAME>_HOST/_PORT` (dev) | that client's dev server |
| `CLIENTS_HOST_HOST/_PORT` | the unified SSR host (publish and dev-host) |
| `AUTH_HOST/_PORT`, `PAYMENTS_…`, `API_…` | backend upstreams |
| `KEYCLOAK_HOST/_PORT/_DOMAIN` | **optional** — setting `KEYCLOAK_HOST` switches the Keycloak vhost on |
| `OTEL_GRPC_HOST/_PORT`, `OTEL_HTTP_HOST/_PORT`, `OTEL_INSTANCE_ID` | tracing + the `/otlp` ingest route |
| `ENVOY_TLS_CERT`, `ENVOY_TLS_KEY` | listener certificate (dev: the Aspire dev cert) |
| `ENVOY_ADMIN_PORT` | admin interface (9901); `/ready` is the health check |

## What a client virtual host routes

Route order matters — the first match wins.

| Match | Cluster | ext_authz | Notes |
|---|---|---|---|
| `/otlp/v1/…` | otel-collector (HTTP) | off | rewritten to `/v1/…`; tracing suppressed |
| `^/(signin\|signup\|signin-oidc\|signout\|reset\|add-passkey)(\?…)?$` | `auth` | off | the OIDC browser flow — an **allowlist**, not a prefix |
| `/account/` | `auth` | off | account JSON; these endpoints resolve the session themselves |
| `/assets/`, and any `*.js/.css/.woff2/...` | web | off | cacheable, identity-free; tracing suppressed |
| `/payments/`, `/api/` | those services | on | prefix stripped, no timeout (streaming-safe) |
| `/` | web | on | SSR |

Three things about this list are load-bearing:

- **`/backchannel-logout` is deliberately absent.** `auth` serves it, Keycloak
  posts logout tokens to it over the private network, and it deletes sessions on
  the strength of that token. Widening the sign-in regex to a prefix would publish
  a session-deletion endpoint to the internet.
- **ext_authz annotates; it does not authorize.** Where it is on, Envoy calls
  `auth`'s gRPC `Check`, which turns the `pf_session` cookie into `x-user-id`-style
  headers. Enforcement happens in the backends (internal JWT) and in the SSR
  protected-path gate.
- **`x-client` is set at the vhost level**, so every route — static assets included
  — carries it. Without that, one client would serve another's hashed bundles.

Each vhost also sets CORS (exact origin in publish, a localhost regex in dev,
`allow_credentials: true`) and an `alt-svc` header advertising HTTP/3.

## The Keycloak vhost (production only)

When `KEYCLOAK_HOST` is set, Envoy adds a virtual host for `KEYCLOAK_DOMAIN` that
forwards to Keycloak on Host B. It is the only thing between the internet and
everything else Keycloak serves on that port, so it is an **allowlist**:

- non-`GET/HEAD/POST/OPTIONS` → `405`;
- `/realms/master…` → `404` (that realm fronts the bootstrap admin);
- allowed: the realm's `openid-connect/auth` and `logout`, `login-actions/…`,
  `broker/{alias}/login|endpoint`, and `/resources/…` theme assets;
- anything else → `404`, never reaching Keycloak.

The admin console, the admin REST API and the account console are therefore not
reachable from the internet at all — `auth` talks to Keycloak over the private
network, and operators go through the private IP.

The vhost re-asserts `x-forwarded-proto: https`, because the listener's scheme
transformation (which keeps `:scheme` matching the cleartext gRPC upstreams) would
otherwise tell Keycloak the request was plaintext and the realm's
`sslRequired: external` would answer `ssl_required`.

## TLS

- **Dev**: the AppHost hands Envoy the ASP.NET developer certificate; upstream dev
  servers are also HTTPS, and Envoy accepts their untrusted chain.
- **Production**: Cloudflare terminates the public TLS. `cloudflared` dials
  `https://envoy:8443` inside the Docker network with `noTLSVerify` against the
  image's baked certificate. Traffic never crosses a network in cleartext outside
  the host, and Cloudflare's zone setting is Full + Always-Use-HTTPS.

## Changing routing

Edit the templates under `proxy/`, then deploy the `envoy` component (layer 07) —
the whole directory is the content hash for its image, so any template change
produces a new artefact. Two constraints to respect:

1. **Keep regexes small.** Envoy rejects a route config whose RE2 program size
   exceeds 100, and a rejected RDS update leaves the vhost with *no* routes.
2. **New public paths on `auth` must be added deliberately** — the sign-in
   allowlist exists to keep everything else on that service private.
