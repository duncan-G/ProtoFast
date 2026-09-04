# 03 · Services & clients

*How each application process reads its own settings — the .NET services, the
Angular clients, the unified SSR host and the OTel collector. Self-contained.*

## The naming convention

Every configurable value is an **environment variable whose name is the same in
dev and in production**. Only the injector changes (Aspire AppHost vs. compose).

```
Auth_Keycloak__Authority
└─┬─┘ └───┬──┘  └───┬───┘
  │       │         └── property
  │       └── config section
  └── audience prefix: which service may read this value
```

- `__` maps to `:` in .NET configuration (`Keycloak:Authority`).
- The prefix (`Auth_`, `Shared_`, and in Secrets Manager also `Infra_`,
  `Payments_`, `Api_`) says *who the value is for*. It is stripped on read, so
  the service sees plain `Keycloak:Authority`.
- `Shared_` is for values every backend needs — today, the internal-JWT public key.

## .NET services

All three services call `builder.AddServiceDefaults()`
([`services/shared/ServiceDefaults`](../../services/shared/ServiceDefaults)), which
sets up OpenTelemetry (traces, metrics, logs — exported only when
`OTEL_EXPORTER_OTLP_ENDPOINT` is set), health checks over both HTTP and the gRPC
Health protocol, and service discovery.

Configuration sources, in increasing precedence:

1. `appsettings.json` — logging levels, `AllowedHosts`, Kestrel `Protocols: Http2`,
   and for `auth` the Secrets Manager coordinates (`Secrets:SecretId`,
   `Secrets:Prefix`).
2. `appsettings.Development.json` — dev-only values (see [layer 02](02-local-development.md)).
3. Environment variables, including the prefixed ones each service opts into:
   - `auth`: `builder.Configuration.AddEnvironmentVariables("Shared_")` then `("Auth_")`
   - `payments`, `api`: `("Shared_")` only
4. **`auth` only, and only when `ASPNETCORE_ENVIRONMENT=Production`**: the AWS
   Secrets Manager provider, which pulls the `protofast/app` secret and keeps the
   `Auth_`-prefixed keys. See [layer 06](06-secrets.md).

Connection strings arrive the standard way — `ConnectionStrings__redis`,
`ConnectionStrings__auth` — injected by Aspire references in dev and written into
compose in prod.

### Option classes worth knowing

| Section | Class | What it controls |
|---|---|---|
| `Keycloak` | `KeycloakOptions` | back-channel `Authority`, browser-facing `PublicAuthority`, the three client secrets, the `account-admin` service client |
| `Tenants:ByHost` | `TenantOptions` | host → realm/client map, plus `MaxAge`/`AcrValues` for step-up on the admin host |
| `Session` | `SessionPolicyOptions` | cookie name (`pf_session`), 8 h idle TTL, 7 d absolute TTL, id rotation on refresh |
| `InternalJwt` | `InternalJwtOptions` (auth) / `InternalJwtValidationOptions` (backends) | ES256 key material, `kid`, issuer/audience, 5 min lifetime |
| `Smtp` | `SmtpOptions` | the relay `auth` sends its own mail through; unset host disables mail rather than failing startup |
| `Subscriptions` | `SubscriptionOptions` | whether an account must be subscribed before reaching the app (off until billing exists) |

Details of what these mean for sign-in are in [layer 05](05-identity.md).

### The internal JWT, in one paragraph

`auth` holds an EC P-256 **private** key and signs a short-lived JWT for each
authenticated call; `payments` and `api` hold only the matching **public** key and
reject anything unsigned via a gRPC interceptor. So a compromised backend can read
identity but cannot mint it. In dev the AppHost generates the pair per run; in
prod the private key reaches `auth` through Secrets Manager and the public key is
mounted into the other two as a file (`Shared_InternalJwt__PublicKeyPemFile`).

## Angular clients

The clients are built once and configured at **runtime**, not at build time —
there is no `environment.prod.ts` fork.

| Variable | Consumed by | Purpose |
|---|---|---|
| `SERVER_URL` | SSR bootstrap | the origin the browser will use; passed to the browser via Angular `TransferState`, and used as the base for the gRPC-Web transport (`${SERVER_URL}/api`) |
| `NG_ALLOWED_HOSTS` | `src/server.ts` | comma-separated hostnames SSR will answer for. Angular's SSRF guard 400s anything else — an empty list rejects *everything* |
| `NG_TRUST_PROXY_HEADERS` | `src/server.ts` | which `x-forwarded-*` headers to trust (Envoy is the only caller) |
| `SERVER_OTEL_ENDPOINT` | `src/instrumentation.ts` | OTLP HTTP endpoint for the Node side |
| `BROWSER_OTEL_ENDPOINT` | browser telemetry | where the browser posts spans — `/otlp` in prod, routed by Envoy |
| `PORT`, `SSL_CERT`, `SSL_KEY` | `ng serve` (dev only) | Aspire-assigned port and dev certificate |

Two behaviours live in the SSR server itself rather than in config:

- **The protected-area gate.** `/app` and `/subscribe` require an `x-user-id`
  header (set by Envoy's ext_authz); without it SSR issues a server-side redirect
  to `/signin?returnUrl=…`, and those responses are marked `private, no-store`.
- **Static assets** are served with `max-age=1y` because Angular hashes their
  filenames.

gRPC service stubs are generated from the services' `.proto` files by `buf`
(`buf.gen.yaml` → `src/lib/gen`) as part of `npm start` / `npm run build`. `auth`
is deliberately *not* an input: browsers talk to it over plain HTTP.

## The unified SSR host

One Node process serves every client (`clients/host/server.mjs`). No client assets
are baked into the image; the entrypoint pulls them from S3 on every start.

| Variable | Purpose |
|---|---|
| `CLIENTS` | comma-separated client names to load (e.g. `admin,protofast`) |
| `DEFAULT_CLIENT` | which client answers when `x-client` is missing or unknown |
| `ASSETS_BUCKET`, `CLIENT_<NAME>_TAG` | the pinned S3 prefix `clients/<name>/<tag>/` to sync |
| `ASSETS_DIR` | where assets are materialised (default `/assets`) |
| `PORT` | listen port (4000) |
| `NG_ALLOWED_HOSTS`, `NG_TRUST_PROXY_HEADERS`, `*_OTEL_ENDPOINT` | passed through to each client bundle |

Dispatch is by the `x-client` request header, which Envoy sets on every route that
can reach this host. Only the first client bundle to load starts the Node OTel
SDK — the rest share it.

## OTel collector

`otel-collector/config.yaml` is a plain collector config parameterised by
`OTLP_GRPC_PORT`, `OTLP_HTTP_PORT` and `OTEL_EXPORTER_OTLP_ENDPOINT` (the Aspire
dashboard). It exposes a health endpoint on `:13133`, which the production deploy
script uses as that component's readiness gate. Envoy's metrics are relabelled to
`service.name = envoy-proxy` on the way through.
