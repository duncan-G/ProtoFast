# 04 · The edge

*What routes each request, what it asks auth first, and what it enforces. Self-contained.*

## ProtoStrap's edge, not ProtoFast's

ProtoFast runs no proxy of its own. In an account the edge is ProtoStrap's: one container per
zone on the ingress VM, holding cloudflared, which runs the Cloudflare tunnel, and Envoy.
Locally `aspire run` runs the same Envoy as the `protostrap-edge` container on port 20000,
rendered by the same code from the same routes
([ProtoStrap's edge](https://github.com/duncan-G/protostrap/blob/v0.1.12/docs/protostrap.md#edge),
[the local edge](https://github.com/duncan-G/protostrap/blob/v0.1.12/docs/protostrap-aspire.md#the-local-edge)).

There is no Envoy config in this repository. The routes are each service's `Routes(...)` in
[`apphost/Program.cs`](../apphost/Program.cs), written into the spec's `routes`, and the
authorizer is its `Authorize(...)`.

What the edge does with them:

- **A route is a host's name and a path prefix,** such as `admin/theplot/`. The host's name
  becomes this account's hostname. The longest prefix wins.
- **A prefix is plain text,** not a path segment: `/signin` also matches `/signin-oidc`.
  There is no prefix rewrite, no regex and no header matching. The service sees the path as
  the browser sent it.
- **gRPC-Web is translated** to gRPC for `grpc` services (the api, payments), so browsers call
  `/<package>.<Service>/<Method>` on their own origin.
- **Each route can have a rate** in requests a second.
- **Requests go only to ready replicas,** found by component name on the overlay.
- **A request no route matches gets 404** and never reaches a service.

## The routes

| Host        | Prefix                                                                            | Service                  | Authorized by auth |
| ----------- | --------------------------------------------------------------------------------- | ------------------------ | ------------------ |
| `protofast` | `/`                                                                               | `clients`                | yes                |
| `theplot`   | `/`                                                                               | `clients`                | yes                |
| `admin`     | `/`                                                                               | `clients`                | yes                |
| `admin`     | `/theplot/`                                                                       | `clients-admin-theplot`  | yes                |
| `protofast`, `theplot`, `admin` | `/signin`, `/signup`, `/signout`, `/reset`, `/add-passkey`, `/account/` | `auth`        | no                 |
| `protofast`, `theplot`, `admin` | `/document.`, `/document_upload.`, `/story.`                      | `api`                    | yes                |
| `admin`     | `/admin.`                                                                         | `api`                    | yes                |
| `protofast`, `theplot`, `admin` | `/greet.`                                                         | `payments`               | yes                |
| `protofast`, `theplot`, `admin` | `/otlp/`, 20 requests a second                                    | `otel-collector`         | no                 |
| `auth`      | `/realms/<realm>/protocol/openid-connect/auth`, `/realms/<realm>/protocol/openid-connect/logout`, `/realms/<realm>/login-actions/`, `/realms/<realm>/broker/` for `protofast`, `theplot` and `operators`; `/resources/` | `keycloak` | no |

Under `aspire run` the dev servers `protofast`, `theplot`, `admin` and `admin-theplot` take the
clients' routes, with the same authorizer.

Three things about this table are load-bearing:

- **`/backchannel-logout` and `/ext-authz` are never routed.** auth serves both. Keycloak
  posts logout tokens to `/backchannel-logout` on the overlay (`http://auth:8080`), and it
  deletes sessions on the strength of them; `/ext-authz` is the edge's own question. auth's
  routes are a list of prefixes for exactly this reason: routing all of auth would publish
  both. A request for either on a client's host goes to the clients, not auth.
- **The auth host is an allowlist.** Only Keycloak's browser sign-in pages, its identity
  provider callbacks and its theme files are routed. The admin console, the admin REST API,
  the account console, the token endpoint and the `master` realm aren't reachable from the
  internet at all; auth and keycloak-config reach Keycloak at `http://keycloak:8080` on the
  overlay.
- **Every client route is authorized, except static files.** See below.

## auth's answer to every request

The clients, the api and payments name auth as their authorizer:

```yaml
authorize:
  by: auth
  path: /ext-authz
  headers: [x-user-id, x-tenant, x-roles, x-internal-jwt, x-authenticated]
  forward: [cookie]
  respond: [set-cookie]
  open: true
  timeout: 500ms
  skip: [/assets/, "*.js", "*.css", ...]   # the clients and the dev servers, not the theplot console
```

For each request on one of their routes, the edge:

1. normalises its path (`..`, `%2e%2e`, `//`), so the route it takes and the path auth is asked
   about are the same;
2. removes the client's own copies of the five `headers`, so a browser can't send them;
3. sends a static file (`skip`) on as it is, without asking;
4. asks auth at `/ext-authz` followed by the request's path and query, with its method, Host,
   cookie and any `Authorization` header (Envoy always sends that one), but not its body
   ([`ExtAuthzEndpoint`](../services/auth/src/ProtoFast.Auth.Api/Endpoints/ExtAuthzEndpoint.cs));
5. on a 200, adds auth's five headers to the request and sends it on, then adds auth's
   `set-cookie`, if any, to the response;
6. on any other answer, gives the browser auth's answer as it is.

auth's answers:

| Request                                                   | auth answers                                                       |
| --------------------------------------------------------- | ------------------------------------------------------------------ |
| A live session                                            | 200 with `x-user-id`, `x-tenant`, `x-roles`, `x-internal-jwt`, `x-authenticated: true` |
| No session, or one that can't be resolved                 | 200 with `x-authenticated: false`                                  |
| A session whose id was just rotated                       | the same, plus `set-cookie` with the new `pf_session`, whatever the status |
| A console path on the admin host (`/theplot/…`), no session | 302 to `/signin?returnUrl=<path and query>`                       |
| A console path, without the console's role (`admin-theplot`) | 302 to `/forbidden`                                             |

So it **annotates and doesn't authorize**, apart from the consoles. Enforcement is downstream:
the api and payments accept only a valid internal JWT, and the SSR servers redirect anonymous
requests for protected pages. The consoles are mapped in auth's tenant config
(`Tenants:ByHost:admin:Consoles`, `theplot` → `admin-theplot`), so auth lets only holders of
the role through to `clients-admin-theplot`'s process, assets included
([design](design/admin-consoles.md)).

If auth doesn't answer within half a second, or has no ready replica, the edge sends the
request on without auth's headers (`open`): it arrives anonymous. The api and payments refuse
it for want of the internal JWT, the SSR servers treat the browser as signed out, and a
console's own server sends it to sign in. auth's readiness doesn't check PostgreSQL, which
answering the edge never reads. auth's sign-in routes aren't authorized, so they don't depend
on that question; they resolve the session themselves.

## What isn't at the edge

- **No CORS.** Pages, gRPC-Web, auth's endpoints and telemetry share each client's origin.
- **No client header.** The clients host picks the app by the request's Host.
- **No CSP at the edge.** The clients host sets the admin consoles' Content-Security-Policy
  on their responses, and the admin dev servers set the same header
  ([layer 03](03-services-and-clients.md#the-clients-host)).

## TLS and forwarded headers

- **In an account** Cloudflare terminates TLS and redirects plain HTTP to HTTPS; cloudflared
  sends each request to Envoy in the same container, and Envoy to the services over the
  overlay, all plain HTTP inside the VPC
  ([Requests in](https://github.com/duncan-G/protostrap/blob/v0.1.12/docs/network.md#requests-in)).
- **Cloudflare's own rules** are the AppHost's `AddCloudflareCdn`: a WAF rule blocks
  scanners' probes (`/wp-admin/`, `/.env`, `/.git/` and the like), and two cache rules keep
  out of Cloudflare's cache the ThePlot console (`/theplot/` on the admin host), whose
  bundles are behind its role yet sent with `max-age=1y`, and the auth host, whose Keycloak
  theme files keep their URLs when a theme changes
  ([Keycloak](../services/keycloak/README.md)). Other static files cache by Cloudflare's
  default, for their own `max-age`, in browsers too. There's no per-client rate limit yet
  (`WithRateLimit`): it waits on real traffic, and a Free-plan zone refuses one, as
  ProtoStrap scopes it by host.
- **Locally** everything is plain HTTP on port 20000.
- Keycloak (`KC_PROXY_HEADERS=xforwarded`), the SSR servers (`x-forwarded-for`,
  `x-forwarded-proto`) and the .NET services (`ASPNETCORE_FORWARDEDHEADERS_ENABLED`) trust the
  forwarded headers they receive. auth ignores the scheme: it builds its OIDC `redirect_uri`
  and post-logout URL from the request's host's `ORIGIN_<NAME>` (`https://` in an account,
  the local edge's `http://` origin locally), and assumes `https://<Host>` only for a host
  that isn't in the spec.

## Known gaps

- **Unverified in an account.** Nothing has run on AWS yet, in particular what
  `x-forwarded-proto` Keycloak and the SSR servers see behind Cloudflare and cloudflared.
- **The browsers' collector route is open.** Anyone can post to `/otlp/`; its rate, size limit
  and `service.name` filter are what bound it.

## Changing routing

Edit the service's `Routes(...)` or `Authorize(...)` in the AppHost, then write the spec
(`aspire do protostrap-spec`) and commit it.
A route change reloads the edge's routes without restarting it. Two rules:

1. **New public paths on auth are added deliberately,** one prefix at a time: everything else
   on that service stays private.
2. **Keep prefixes unambiguous.** `protostrap check` refuses two routes for the same host and
   path, and a prefix matches any path that starts with its text.
