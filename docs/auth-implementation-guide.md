# Auth Implementation Guide

A step-by-step plan to implement the authentication design in
[`auth-architecture.md`](./auth-architecture.md) for **this** repository.

This is the *how*; the architecture doc is the *what/why*. Read that first.

---

## 0. Scope & decisions

What we are building **now** (everything else in the architecture doc is
deferred, not removed):

| Decision | Choice for this milestone |
| --- | --- |
| Sign-in factors | **Email + password** and **email + passkey (WebAuthn)** only |
| Email verification | **Required** — not in the original doc, added here. Enforced by Keycloak's *Verify Email* required action + SMTP |
| Login / signup / verify UI | **Keycloak-hosted pages** (OIDC redirect model from the architecture doc), *not* a custom Angular form. The `AuthenticationSample` repo drives Cognito from a custom UI — that is **inspiration only**; we follow the doc's redirect/BFF flow |
| Realms / clients | Only the **`protofast`** realm exists, with clients **`protofast-web`** and **`admin`**. `myfitness` / `theplot` are designed-for but **not created** (no code, no realm) |
| Browser credential | Opaque, HttpOnly session cookie. Browser never sees a Keycloak token (BFF) |
| Session store | **Redis** (already provisioned in the AppHost) |
| ext_authz | Envoy `ext_authz` gRPC filter → `auth-svc` `Authorization/Check` |
| Tenant→realm map | auth-svc data (config for now, DB row later). Resolved from `Host` |

### Why Keycloak-hosted UI (not the sample's custom UI)

The architecture doc's sequence diagrams are explicit redirect flows
(`302 → /realms/protofast/auth?client_id=…`, `prompt=create` for registration,
`/signin-oidc?code=…` for the callback). That means **Keycloak renders
login / register / verify-email / passkey-enrollment**, and auth-svc only:

1. kicks off the OIDC Authorization-Code-with-PKCE flow,
2. handles the callback (`code → tokens` back-channel exchange),
3. issues/validates the opaque session cookie, and
4. answers Envoy's ext_authz `Check`.

This keeps password hashing, WebAuthn ceremonies, verification emails, lockout,
and password reset **inside Keycloak** — we configure them, we don't reimplement
them. The Cognito sample reimplements all of that because Cognito has no hosted
flow we wanted; ignore that part.

### What the sample *is* good for

Borrow these patterns, not the Cognito code. The sample lives in a **sibling
repo**, `~/Projects/AuthenticationSample/microservices/Auth` (paths below are
relative to that root):

- The Envoy ext_authz contract: `src/Auth.Grpc/Protos/internal/authz.proto` and `src/Auth.Grpc/Services/Internal/InternalAuthorizationService.cs` — copy the proto verbatim, it's the real Envoy v3 API.
- Opaque-cookie → Redis session resolution with refresh-on-expiry (`ResolveSessionAsync` in `src/Auth.Core/Identity/IdentityService.cs`).
- FluentValidation gRPC interceptor + Redis rate limiting + `appsettings.Testing.json` integration-test layout.

---

## 1. Current state (what's already scaffolded)

Know your starting point before writing code:

- **`services/auth/src/ProtoFast.Auth.Api`** — a gRPC skeleton (only `GreeterService`), .NET 10, `AddServiceDefaults()`, container repo `protofast-auth`. This becomes auth-svc.
- **AppHost** ([`apphost/Program.cs`](../apphost/Program.cs)) already wires: `postgres` with an **`auth`** database (`auth-db`), **`redis`**, **`keycloak`** on `8080`, and the `auth`/`payments`/`api` projects behind Envoy.
- **Envoy** ([`proxy/envoy.rds.yaml.tmpl`](../proxy/envoy.rds.yaml.tmpl)) routes `/auth/` → `auth` cluster (HTTP/2/gRPC), and `/` → web cluster. **No ext_authz filter yet.** Filters present: cors, grpc_web, router.
- **Keycloak prod** ([`deploy/docker-compose.host-b.yml`](../deploy/docker-compose.host-b.yml)) runs `kc.sh start --import-realm`, `KC_PROXY_HEADERS=xforwarded`, `KC_HOSTNAME=${KEYCLOAK_DOMAIN}`, imports from `deploy/keycloak/realms`. Connection string already points services at `http://keycloak:8080/realms/protofast`.
- **Postgres** seeds a durable `auth` DB + `auth` role ([`deploy/postgres/initdb/01-auth.sh`](../deploy/postgres/initdb/01-auth.sh)).
- **`infra/keycloak/realms/`** — empty. The canonical realm export goes here (dev import), copied to `deploy/keycloak/realms/` for prod (see that dir's [README](../deploy/keycloak/realms/README.md)).
- **Shared libs** — `services/shared/Database*` (EF Core repository/UoW), `ServiceDefaults`, `Exceptions`.
- **Secrets** — one Secrets Manager secret, prefix-filtered. Auth secrets use the **`Auth_`** prefix; populated out-of-band, never in TF state.

---

## 2. Phase 1 — Keycloak realm configuration

All of this is **config**, captured as a committed realm export so dev (Aspire
import) and prod (`--import-realm`) are identical.

### 2.1 Create the realm export

1. Bring up Keycloak locally (the AppHost already runs it). Open the admin
   console, create realm **`protofast`**.
2. Configure the realm (sections 2.2–2.6 below) **in the UI**, then **Partial
   export** with *clients* and *groups/roles* included.
3. Save it as `infra/keycloak/realms/protofast-realm.json` (canonical) and copy
   to `deploy/keycloak/realms/protofast-realm.json` (prod mount). Keep the two
   byte-identical.

> Treat the export as source of truth, but **scrub secrets** before committing —
> client secrets and SMTP passwords must come from the SM secret at runtime, not
> the JSON. See §7.2.

### 2.2 Clients

Create two clients in the `protofast` realm:

| Client | Type | Auth flow | Redirect URIs |
| --- | --- | --- | --- |
| `protofast-web` | **Confidential** (client auth ON) | Standard flow (Authorization Code) + PKCE `S256` | `https://protofast.dev/signin-oidc` (+ `https://localhost:*/signin-oidc` for dev) |
| `admin` | **Confidential** | Standard flow + PKCE `S256` | `https://admin.protofast.dev/signin-oidc` (+ dev localhost) |

- **Both confidential** — auth-svc is the only thing holding the secret (BFF, back-channel exchange). No public/SPA clients.
- Disable Direct Access Grants, Implicit, Service Accounts unless specifically needed.
- Set **Valid post-logout redirect URIs** to each app's origin.
- Web origins: the app origin(s) only.
- These two clients **share the `protofast` realm**, which is what gives staff the silent SSO between `protofast.dev` and `admin.protofast.dev` (Flow A in the doc).

### 2.3 Email verification + SMTP

This is the addition the architecture doc omitted.

1. Realm → **Login** tab → enable **Verify email**.
2. Realm → **Email** tab → configure SMTP (host, port, from, auth). The SMTP
   **password comes from the SM secret** (`Auth_Smtp__Password`), injected as an
   env var, not stored in the realm JSON.
3. Add **Verify Email** to the realm's default required actions so new
   registrations must confirm before the account is usable.

Result: registration via `prompt=create` → Keycloak sends the verification mail
→ user can't complete sign-in until verified. auth-svc needs no verification
code logic; it just won't get a usable session until Keycloak says the user is
verified.

### 2.4 Password policy

Realm → **Authentication → Policies → Password policy**. Set a sane baseline
(length ≥ 12, not-username, not-email, optionally HaveIBeenPwned/“not recently
used”). Configure **brute-force detection** (Realm → Security defenses) for
lockout.

### 2.5 Passkeys (WebAuthn passwordless)

Keycloak provides passkeys natively — do **not** build WebAuthn yourself.

**Decision: use the GA WebAuthn Passwordless policy, *not* the `passkeys`
preview feature.** In `26.0` the dedicated *Passkeys* feature
(`--features=passkeys`, which adds conditional-UI autofill + usernameless) is
**preview** — unsupported and changeable, so we don't enable it in production.
The stable WebAuthn Passwordless policy still issues real passkeys (discoverable,
syncable credentials) when *resident key* is required; we forgo only the autofill
polish for now. (Fast-follow: revisit by bumping `KEYCLOAK_TAG` to a 26.x where
passkeys is GA, rather than shipping a preview flag — see §11 note.)

1. Enable the WebAuthn **passwordless** policy: Realm → Authentication →
   **Policies → WebAuthn Passwordless Policy**:
   - RP name = `ProtoFast`, RP ID = **`protofast.dev`** (the registrable domain).
   - **Require Resident Key = Yes** — this is what makes the credential a real
     passkey (discoverable / syncable).
   - User Verification Requirement = `required`.
   - Signature algorithms = `ES256` (+ `RS256` fallback); attestation
     conveyance = `none` (don't demand attestation unless you have a reason).
2. Enable the **`webauthn-register-passwordless`** required action so users can
   enrol a passkey from the account console / at first login.
3. Build a browser flow that offers passkey **or** password: copy the built-in
   *browser* flow, add a “Passkey or Password” step, and set it as the realm
   browser flow. Login is **email-first** (enter email → authenticate with
   passkey or password); autofill/usernameless is the deferred polish above.
4. **Confirm against the pinned version** (`KEYCLOAK_TAG=26.0`) that the WebAuthn
   Passwordless policy + `webauthn-register-passwordless` action are present as
   described (they are GA, so this is a sanity check, not a feature-flag hunt).

> `RP ID` must be the registrable domain (`protofast.dev`), and the browser must
> reach the app over HTTPS on that domain — which it does, since TLS terminates
> at Cloudflare and the `Host` is preserved through the tunnel. This is exactly
> why the doc's "host preservation" gotcha matters for passkeys too.

### 2.6 Proxy / hostname settings

Already set in the prod compose, just confirm:
`KC_PROXY_HEADERS=xforwarded`, `KC_HOSTNAME=auth.protofast.dev`,
`KC_HTTP_ENABLED=true`. These let Keycloak build `https://` redirect URIs behind
Cloudflare+Envoy. For **dev**, the Aspire `AddKeycloak` resource needs the realm
import wired (see §5).

### 2.7 Session & token lifetimes

Mirror the auth-svc session policy (§3.4) so the two layers agree — the BFF
session must never outlive Keycloak's ability to refresh. Realm → **Sessions** /
**Tokens**:

- **SSO Session Idle = 8h**, **SSO Session Max = 7d** (match `IdleTtl` /
  `AbsoluteTtl`).
- **Access Token Lifespan = 5 min** (default) — the access token is server-side
  only; auth-svc refreshes it silently, so short is fine and limits exposure.
- Refresh-token rotation: leave Keycloak's default (revoke-on-use) on; auth-svc
  stores the latest refresh token per session.

---

## 3. Phase 2 — Build the auth-svc (BFF)

auth-svc is **one ASP.NET Core app** exposing two surfaces:

- **HTTP minimal-API endpoints** for the browser OIDC flow (302 redirects + `Set-Cookie`): `/signin`, `/signup`, `/signin-oidc`, `/signout`, `/reset`.
- **gRPC `Authorization/Check`** for Envoy ext_authz.

Both live in `services/auth/src/ProtoFast.Auth.Api`.

### 3.1 Project layout

ProtoFast's own services are **single-project today** (`ProtoFast.Api`,
`ProtoFast.Payments.Api`, `ProtoFast.Auth.Api`) — the `Core`/`Infrastructure`
clean-architecture split belongs to ThePlot and the Cognito sample, not here. So
we **don't** import those generic layer names. The only hard reason to add a
second project is that the migration runner (Phase 2b) must share the
`AuthDbContext`. That gives a minimal, on-convention split named for what each
project *is*:

```
services/auth/
  src/
    ProtoFast.Auth.Api/             # host: OIDC endpoints + gRPC ext_authz + Keycloak/Redis adapters + DI
    ProtoFast.Auth.Data/            # EF: AuthDbContext, entities, migrations, provisioning — shared with the runner
    ProtoFast.Auth.SchemaMigrations/# migration runner (refs .Data) — see §3.5
  tests/
    ProtoFast.Auth.UnitTests/
    ProtoFast.Auth.IntegrationTests/
```

- Keep **`ProtoFast.Auth.Api`** as the host (already has `AddServiceDefaults`,
  container settings, OTel). The session/OIDC/ext_authz logic lives here, the
  same way the other ProtoFast services keep their logic in `.Api`.
- **`ProtoFast.Auth.Data`** holds only persistence — descriptive of its job, and
  the one thing `ProtoFast.Auth.SchemaMigrations` references.

> **If you later want a testable domain boundary**, extract by *responsibility*,
> never as `Core`/`Infrastructure`: e.g. `ProtoFast.Auth.Sessions` (session +
> identity domain, contracts, options) and `ProtoFast.Auth.Keycloak` (the OIDC
> adapter). Names that say what's inside beat layer-cake labels. Start with the
> three above and split only when a test or reuse actually demands it.

### 3.2 NuGet packages (Api unless noted)

- `Grpc.AspNetCore` (already) — for the ext_authz service.
- `Microsoft.AspNetCore.Authentication.OpenIdConnect` **or** a thin manual OIDC client. Prefer **manual** back-channel calls (`/protocol/openid-connect/token`) because the BFF owns the cookie, not ASP.NET cookie-auth — fewer moving parts and matches the doc's "opaque cookie, tokens server-side" model. (Use `Microsoft.IdentityModel.Protocols.OpenIdConnect` for discovery + JWKS.)
- `StackExchange.Redis` (session store).
- `Microsoft.IdentityModel.Tokens` / `System.IdentityModel.Tokens.Jwt` (validate Keycloak access tokens; mint the internal JWT).
- `FluentValidation` (Api) — optional, for the few JSON-bodied endpoints.
- EF Core + Npgsql (in `ProtoFast.Auth.Data`) for first-login provisioning + the `AuthDbContext`.

### 3.3 Configuration / options (`ProtoFast.Auth.Api`)

```csharp
public sealed class TenantOptions            // Host -> realm/client map (config now, DB later)
{
    public Dictionary<string, TenantConfig> ByHost { get; init; } = new();
}
public sealed record TenantConfig(string Realm, string ClientId);

public sealed class KeycloakOptions
{
    public string Authority { get; init; } = "";   // http://keycloak:8080  (per-realm path built from tenant)
    public string ClientSecretProtofastWeb { get; init; } = "";  // from Auth_ secret
    public string ClientSecretAdmin { get; init; } = "";
}

public sealed class SessionOptions
{
    public string CookieName { get; init; } = "pf_session";
    public TimeSpan IdleTtl { get; init; } = TimeSpan.FromHours(8);   // sliding; reset on each resolve/refresh
    public TimeSpan AbsoluteTtl { get; init; } = TimeSpan.FromDays(7); // hard cap from createdAt → full re-auth
    public bool RotateIdOnRefresh { get; init; } = true;             // new opaque id + Set-Cookie on refresh
    public string CookieDomainStrategy { get; init; } = "host-only"; // see §3.6
}

public sealed class InternalJwtOptions
{
    // ES256 (asymmetric). auth-svc holds the EC PRIVATE key (signs); backends hold
    // only the PUBLIC key (verify) — so a compromised api/payments can't forge.
    public string PrivateKeyPem { get; init; } = "";   // auth only, from Auth_ secret
    public string KeyId { get; init; } = "";           // `kid`, to support rotation
    public string Issuer { get; init; } = "protofast-auth";
    public string Audience { get; init; } = "protofast-internal";
    public TimeSpan Lifetime { get; init; } = TimeSpan.FromMinutes(5);
}
```

Seed `TenantOptions` for the two hosts that exist:

```jsonc
"Tenants": {
  "ByHost": {
    "protofast.dev":       { "Realm": "protofast", "ClientId": "protofast-web" },
    "admin.protofast.dev": { "Realm": "protofast", "ClientId": "admin" }
  }
}
```

Adding `myfitness` later = one more entry (or a DB row), no code change. If a
`Host` isn't in the map → 404/route to public, never guess a realm.

### 3.4 Session store (`ProtoFast.Auth.Api`)

Opaque cookie → Redis. Model after the sample's `IdentityService` session
handling but simplified to a single opaque id:

- On successful callback, mint `sessionId = 32 random bytes, base64url`.
- Store in Redis `sess:{sessionId}` → JSON `{ sub, email, realm, clientId, roles, accessToken, refreshToken, accessExp, refreshExp, createdAt, cachedInternalJwt, internalJwtExp }`.
- **`ISessionStore`**: `Create`, `Get`, `Delete`, `Replace` (after refresh).
- Never put the Keycloak tokens in the cookie; only the opaque id.

**Lifetime policy (resolved decision).** Sliding idle, bounded by an absolute cap:

- **Redis key TTL = `IdleTtl` (8h)**, reset on every successful resolve/refresh —
  this *is* the sliding idle window (no activity for 8h → key gone → re-auth).
- **Absolute cap = `AbsoluteTtl` (7d)** from `createdAt`. On resolve, if
  `now - createdAt > AbsoluteTtl`, treat as expired and force full re-auth even if
  the key is still warm. Clamp the key TTL so it never outlives the cap.
- **Never exceed Keycloak's refresh-token lifetime** — once the refresh token
  can't renew the access token, the session is dead regardless of the TTLs above.
  Mirror these in Keycloak (§2.7) so the two layers agree: SSO Session Idle = 8h,
  SSO Session Max = 7d.
- **Rotate the opaque id on refresh** (`RotateIdOnRefresh = true`): when `Check`
  refreshes the access token and rewrites the entry, write it under a **new**
  `sessionId`, `Set-Cookie` the new id (via the ext_authz OK response), and let
  the old key expire with a short grace (~30s) so concurrent in-flight requests
  carrying the old id don't fail. Shrinks the window a stolen cookie is usable.

> These are secure-leaning defaults for a starter that ships with payments + an
> admin console; all three knobs are config. Loosen `IdleTtl`/`AbsoluteTtl` for a
> more consumer "stay-signed-in" feel, or tighten for staff-only realms.

### 3.5 OIDC gateway (`ProtoFast.Auth.Api`)

`IKeycloakGateway`:

- `BuildAuthorizeUrl(realm, clientId, redirectUri, state, codeChallenge, prompt?)` — `prompt=create` for `/signup`.
- `ExchangeCodeAsync(realm, clientId, secret, code, redirectUri, codeVerifier)` → tokens (back-channel POST to `/realms/{realm}/protocol/openid-connect/token`).
- `RefreshAsync(realm, clientId, secret, refreshToken)` → tokens.
- `EndSessionUrl(realm, idTokenHint, postLogoutRedirect)` — for `/signout`.
- `GetValidationParameters(realm)` — discovery + JWKS, cached, to validate the access token in `Check`.

Use **PKCE (S256)** and **`state`** (CSRF) on every authorize request; store the
`state`+`code_verifier`+target-return-url in a short-lived correlation cookie or
Redis keyed by `state` (the sample uses a "correlation" cookie — see Flow B).

### 3.6 HTTP endpoints (`ProtoFast.Auth.Api`, minimal API)

Map these as plain HTTP (they 302 + `Set-Cookie`; they are **not** gRPC):

| Endpoint | Behaviour |
| --- | --- |
| `GET /signin` | Resolve realm/client from `Host`. Set correlation (state+PKCE). `302 → {authority}/realms/{realm}/protocol/openid-connect/auth?...` |
| `GET /signup` | Same as `/signin` but `prompt=create` (Keycloak registration page) |
| `GET /signin-oidc` | OIDC **callback**. Verify `state`, exchange `code`→tokens, **provision user** (§3.8), create session, `Set-Cookie` (host-only, `Secure; HttpOnly; SameSite=Lax`), `302 →` original target or `/app` |
| `GET /signout` | Delete Redis session, clear cookie, `302 →` Keycloak end-session, back to `/` |
| `GET /reset` | `302 →` Keycloak's reset-credentials flow |

**Cookie attributes** (architecture doc gotcha #5): `Secure; HttpOnly;
SameSite=Lax`. *Lax is required* so the cookie survives the top-level redirect
back from Keycloak; `Strict` drops it. Cookie is **host-only** (no `Domain=`
attribute) so a `myfitness` session can never be presented to `theplot` — realm
isolation (Flow B).

### 3.7 gRPC ext_authz `Authorization/Check`

1. Copy the proto verbatim from the sample
   (`src/Auth.Grpc/Protos/internal/authz.proto`)
   → `Protos/internal/authz.proto`, `csharp_namespace = "Envoy.Service.Auth.V3"`.
   Register it in the `.csproj` `<Protobuf>` group, `GrpcServices="Server"`.
2. Implement `AuthorizationService : Authorization.AuthorizationBase` modelled on
   the sample's `src/Auth.Grpc/Services/Internal/InternalAuthorizationService.cs`:
   - Read the `cookie` header from `request.Attributes.Request.Http.Headers` (case-insensitive).
   - Parse the opaque session id → load Redis session.
   - **Validate** the Keycloak access token (signature via JWKS, issuer = realm, `azp`/`aud` = expected client). On expiry, **refresh** using the stored refresh token, replace the Redis entry.
   - Resolve the **tenant** from the `Host`/`:authority` header and confirm it matches the session's realm — reject cross-tenant cookie replay.
   - **Check never denies.** It returns `CheckResponse` OK on *every* request and
     lets the app decide — this is what lets one origin serve both anonymous and
     authenticated pages (resolved Q1). Two cases:
     - **Valid session** → OK with injected request headers:
       `x-user-id` (sub), `x-tenant` (realm), `x-roles`, **`x-internal-jwt`** (the
       ES256 token from the session cache, re-minted only near expiry — §3.9).
     - **No / invalid session** → OK with **no identity headers** (optionally an
       explicit `x-authenticated: false`). No 302, no 401 from the filter.
   - **Always strip** any client-supplied copies of the identity headers
     (`headers_to_remove`) on *every* request — authenticated or not — so a
     client can't smuggle `x-internal-jwt`/`x-user-id`/etc. (The backend's ES256
     signature check is the real guard; stripping is defence in depth.)
   - **Enforcement moves off the edge:** SSR/SPA redirect or render-anonymous for
     HTML (§7); the gRPC backend rejects any call lacking a valid `x-internal-jwt`
     (§6). The edge only *annotates* identity.
3. `MapGrpcService<AuthorizationService>()` in `Program.cs` (replace `GreeterService`).

### 3.8 First-login provisioning (EF, shared `Database`)

On `/signin-oidc` success, **upsert** the user into the `auth` Postgres DB
(`sub`, email, realm, created/last-login). This is the doc's "first-login
provisioning" step (Flow B step: *upsert user in DB*). The `AuthDbContext` and
its **schema/migrations are owned by a dedicated migration runner** — see
[§3.5 — Database schema & migrations](#35--database-schema--migrations-phase-2b).
The `auth` DB and `auth` role already exist (created once by
[`deploy/postgres/initdb/01-auth.sh`](../deploy/postgres/initdb/01-auth.sh));
the runner creates the **tables** inside it.

### 3.9 Internal JWT (API trust)

The architecture doc has Envoy/auth inject **`x-internal-jwt`** that the
gRPC/API backend trusts. It carries `sub`, `tenant`, `roles`.

**Decision: ES256 (asymmetric).** auth-svc signs with an EC **private** key
(`Auth_InternalJwt__PrivateKeyPem`, P-256); `api`/`payments` verify with only the
**public** key (§6). A compromised backend can read but **cannot forge** identity
tokens — proper least-privilege across the mesh. ES256 is chosen over RS256 for
cheaper signing and smaller tokens; over HS256 because a shared symmetric secret
would let any verifier mint tokens. Include a `kid` header so the public key can
be rotated (static config now; a JWKS endpoint on auth-svc later).

**Don't mint per request.** Sign the token **once and cache it in the Redis
session** with its own short TTL (≈ `InternalJwtOptions.Lifetime`, ≤5 min); in
`Check`, reuse the cached token until it nears expiry, then re-mint and rewrite
the session entry. This removes per-request EC signing from the hot path while
keeping tokens short-lived. Re-mint also when the session's roles/identity change
(e.g. after a token refresh that alters claims).

### 3.10 Program.cs wiring (sketch)

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

builder.Configuration
    .AddEnvironmentVariables("Shared_")
    .AddEnvironmentVariables();              // Auth_* secrets land here in prod

builder.Services.AddGrpc();
builder.Services.Configure<TenantOptions>(builder.Configuration.GetSection("Tenants"));
builder.Services.Configure<KeycloakOptions>(builder.Configuration.GetSection("Keycloak"));
builder.Services.Configure<SessionOptions>(builder.Configuration.GetSection("Session"));
builder.Services.Configure<InternalJwtOptions>(builder.Configuration.GetSection("InternalJwt"));

builder.AddRedisClient("redis");            // Aspire wires the connection string
builder.Services.AddAuthDb(builder.Configuration);   // shared Database + auth DbContext
builder.Services.AddSingleton<ISessionStore, RedisSessionStore>();
builder.Services.AddSingleton<IKeycloakGateway, KeycloakGateway>();
builder.Services.AddSingleton<IInternalJwtFactory, InternalJwtFactory>();
builder.Services.AddHttpClient();           // back-channel to Keycloak

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapAuthEndpoints();                      // /signin /signup /signin-oidc /signout /reset
app.MapGrpcService<AuthorizationService>();  // ext_authz Check
app.Run();
public partial class Program { }             // for integration tests
```

---

## 3.5 — Database schema & migrations (Phase 2b)

Inspiration: ThePlot's `src/ThePlot.SchemaMigrations` (a dedicated EF migration
**runner** project + an Aspire `WithSchemaMigrations` command). We adopt that
shape but **diverge on prod**: ThePlot publishes the runner as an *Azure
Container App Job* (`PublishAsAzureContainerAppJob()`), which has no equivalent
here — ProtoFast ships containers to **ECR** and deploys them via
**docker-compose + `deploy.sh` over SSM** (per-component, content-hash tags). So
prod runs the runner as a **one-shot compose job**, gated inside the `auth`
deploy.

### Principles (what we keep / improve over ThePlot)

- **Separate runner, not migrate-on-boot.** The auth service never calls
  `MigrateAsync` at startup (multiple replicas would race). A standalone exe owns
  schema changes. *(ThePlot already does this — keep it.)*
- **Destructive rebuild is dev-only.** `--rebuild-schema` (drop schema +
  `__EFMigrationsHistory`, re-apply) is hard-guarded to non-Production.
  *(Improvement — ThePlot's runner would drop whatever DB it's pointed at.)*
- **Concurrency-safe.** Take a Postgres **advisory lock** around `MigrateAsync`
  so two runners (e.g. an `auth` deploy racing a manual run) can't corrupt
  history. *(Improvement.)*
- **Least privilege.** Migrations run as the **`auth`** role (owns the `auth`
  DB), never the Postgres superuser. DB/role creation stays in
  `01-auth.sh`; the runner only does tables/indexes.
- **Expand/contract discipline.** Migrations must be backward-compatible so the
  ordering between "migrate" and "roll the service" is safe either way.

### 3.5.1 The runner project

`services/auth/src/ProtoFast.Auth.SchemaMigrations` (console exe), referencing
`ProtoFast.Auth.Data` (which holds `AuthDbContext`). Give it a
`ContainerRepository` so CI can publish it to ECR like the other services.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>ProtoFast.Auth.SchemaMigrations</RootNamespace>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <ContainerRepository>protofast-auth-migrations</ContainerRepository>
    <ContainerFamily>noble-chiseled</ContainerFamily>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\ProtoFast.Auth.Data\ProtoFast.Auth.Data.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Aspire.Npgsql" Version="..." />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.Extensions.Hosting" />
  </ItemGroup>
</Project>
```

`Program.cs` — applies migrations; `--rebuild-schema` (dev) drops first:

```csharp
using Microsoft.EntityFrameworkCore;
using Npgsql;
// ...

var builder = Host.CreateApplicationBuilder(args);

// Reads ConnectionStrings__auth (dev: Aspire reference; prod: compose env).
builder.AddNpgsqlDataSource("auth");
builder.Services.AddDbContext<AuthDbContext>((sp, o) =>
    o.UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>())
     .UseSnakeCaseNamingConvention());

using var host = builder.Build();
using var scope = host.Services.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

var isProd = builder.Environment.IsProduction();
var rebuild = args.Contains("--rebuild-schema", StringComparer.OrdinalIgnoreCase);
if (rebuild && isProd)
{
    Console.Error.WriteLine("Refusing --rebuild-schema in Production.");
    return 2;
}

try
{
    // Serialize concurrent runners (advisory lock is released on connection close).
    await db.Database.OpenConnectionAsync();
    await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_lock(727274);"); // any app-wide constant

    if (rebuild)
    {
        var schema = db.Model.GetDefaultSchema() ?? "public";
        Console.WriteLine($"Rebuild: dropping schema \"{schema}\" + migrations history…");
#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync($@"DROP SCHEMA IF EXISTS ""{schema}"" CASCADE;");
        await db.Database.ExecuteSqlRawAsync($@"CREATE SCHEMA ""{schema}"";");
        await db.Database.ExecuteSqlRawAsync(@"DROP TABLE IF EXISTS public.""__EFMigrationsHistory"";");
#pragma warning restore EF1002
    }

    Console.WriteLine("Applying migrations…");
    await db.Database.MigrateAsync();
    Console.WriteLine("Migrations applied.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("Migration failed: " + ex);
    return 1;
}
```

Add a `DesignTimeDbContextFactory` (for `dotnet ef migrations add`) pointing at a
localhost connection, exactly like ThePlot's
`src/ThePlot.SchemaMigrations/DesignTimeDbContextFactory.cs`.

> **`AuthDbContext`** lives in `ProtoFast.Auth.Data` with a plain
> `DbContextOptions<AuthDbContext>` constructor (so EF tooling + the runner +
> the service all resolve it the same way). Note the shared
> `AddCoreDatabaseServices<T>` hard-wires `.UseVector()` (pgvector); ProtoFast's
> Postgres (dev container + prod `postgres:17`) does **not** have pgvector
> installed, so for auth either register the context directly as above, or add
> the `vector` extension first. Don't pull in the pgvector-coupled helper by
> default.

### 3.5.2 Dev — Aspire `WithSchemaMigrations`

Adapt ThePlot's extension to ProtoFast's **container** Postgres (the type is
`IResourceWithConnectionString`, not the Azure flexible-server type) and make it
**run-mode only** (prod doesn't deploy via Aspire):

```csharp
public static IResourceBuilder<ProjectResource> WithSchemaMigrations<TProject>(
    this IDistributedApplicationBuilder builder,
    IResourceBuilder<IResourceWithConnectionString> db,
    [ResourceName] string name)
    where TProject : IProjectMetadata, new()
{
    var migrations = builder.AddProject<TProject>(name)
        .WithReference(db)
        .WaitFor(db);

    // Prod images are built/shipped by CI (ECR) + compose, not Aspire publish.
    if (!builder.ExecutionContext.IsPublishMode)
    {
        var dir = Path.GetDirectoryName(new TProject().ProjectPath)!;
        migrations.WithCommand("rebuild-schema", "Rebuild",
            ctx => SchemaMigrationsCommands.ExecuteRebuildSchemaAsync(ctx, dir, db.Resource),
            new CommandOptions { IconName = "ArrowClockwise", IconVariant = IconVariant.Filled, IsHighlighted = true });
    }
    return migrations;
}
```

Reuse ThePlot's `SchemaMigrationsCommands.ExecuteRebuildSchemaAsync` verbatim
(it `dotnet build` + `dotnet run --no-build --rebuild-schema` with
`ConnectionStrings__<db>` injected) — just rename the connection key to `auth`.

Wire it in `apphost/Program.cs` next to the `auth` project, passing the `auth`
database resource:

```csharp
builder.WithSchemaMigrations<Projects.ProtoFast_Auth_SchemaMigrations>(authDb, "auth-migrations");
```

On `dotnet run` the runner applies migrations once and exits (green in the
dashboard); the **Rebuild** command does a destructive reset on demand.

### 3.5.3 Prod — one-shot job via compose + `deploy.sh`

Add a **job service** to [`deploy/docker-compose.host-b.yml`](../deploy/docker-compose.host-b.yml).
`profiles: ["jobs"]` keeps it out of `compose up`; it runs only via
`compose run`. `restart: "no"` so it exits.

```yaml
  auth-migrations:
    image: ${ECR}/protofast-auth-migrations:${AUTH_MIGRATIONS_TAG}
    profiles: ["jobs"]
    restart: "no"
    environment:
      ASPNETCORE_ENVIRONMENT: Production          # hard-blocks --rebuild-schema
      ConnectionStrings__auth: "Host=postgres;Port=5432;Database=auth;Username=auth;Password=${AUTH_DB_PASSWORD}"
    depends_on:
      postgres:
        condition: service_healthy
```

In [`deploy/deploy.sh`](../deploy/deploy.sh), make migrations a **pre-step of the
`auth` apply** — run the job, and only recreate `auth` on exit 0 (fail-closed:
old auth keeps serving the old schema, which expand/contract keeps compatible):

```bash
# inside the auth component branch, before recreating the auth container:
log "running auth schema migrations (auth-migrations=$AUTH_MIGRATIONS_TAG)"
if ! compose run --rm auth-migrations; then
  log "migrations FAILED — aborting auth apply (auth not recreated)"
  exit 1
fi
```

`AUTH_MIGRATIONS_TAG` lives in `versions.env` alongside `AUTH_TAG`.

### 3.5.4 Prod — building & shipping the runner image

**Decision: a dedicated one-shot migration container** (`protofast-auth-migrations`)
that runs, applies migrations, exits, and is done — not an alternate entrypoint on
the auth image. It keeps the service image lean and matches the per-component
deploy model.

Add `deploy-auth-migrations.yml`, a sibling of
[`deploy-auth.yml`](../.github/workflows/deploy-auth.yml), calling the reusable
`_component-deploy.yml` with `build: dotnet`, `target: protofast-auth-migrations`,
`project: services/auth/src/ProtoFast.Auth.SchemaMigrations`, and the same
`hash_paths: "services/auth services/shared"`. This builds/pushes the image and
writes `AUTH_MIGRATIONS_TAG`. Because it shares `services/auth/**` paths with
`deploy-auth.yml`, both fire on the same change — pin the **migrations** deploy to
run/complete before the **auth** deploy (workflow ordering, or simply rely on the
`deploy.sh` pre-step always pulling the current `AUTH_MIGRATIONS_TAG`).

> **Not chosen (recorded for context):** an alternate entrypoint on the auth
> image (`docker run --rm --entrypoint dotnet <auth-image> ProtoFast.Auth.SchemaMigrations.dll`)
> avoids a second ECR repo but bundles EF/design assets into the service image. We
> took the dedicated one-shot container instead.

### 3.5.5 Reconciliations before coding

- **Connection-string name.** AppHost currently registers
  `postgres.AddDatabase("auth-db", databaseName: "auth")` → Aspire injects
  `ConnectionStrings__auth-db`, but prod compose sets `ConnectionStrings__auth`.
  Align them: rename the Aspire resource to **`auth`** (keep `databaseName:
  "auth"`) so dev and prod use the same key. The runner and service both read
  `ConnectionStrings__auth`.
- **initdb vs migrations boundary.** Keep `01-auth.sh` for DB + role creation
  (first-init only). The runner owns tables. Don't duplicate either side.

---

## 4. Phase 3 — Envoy wiring

Two changes to the proxy templates.

### 4.1 Add the ext_authz HTTP filter

In [`proxy/envoy.listener.yaml.tmpl`](../proxy/envoy.listener.yaml.tmpl) add the
filter **before** `envoy.filters.http.router`, in **both** filter chains (TCP and
QUIC):

```yaml
- name: envoy.filters.http.ext_authz
  typed_config:
    "@type": type.googleapis.com/envoy.extensions.filters.http.ext_authz.v3.ExtAuthz
    transport_api_version: V3
    failure_mode_allow: true           # Check never denies anyway (Q1); if auth-svc
                                       # is unreachable, degrade to anonymous rather
                                       # than 403-ing the whole site. Still fail-closed
                                       # at the API: no JWT minted → backend rejects,
                                       # and ES256 means a smuggled token can't verify.
    grpc_service:
      envoy_grpc: { cluster_name: auth }
      timeout: 0.5s
    include_peer_certificate: false
```

The `auth` cluster already exists and is HTTP/2 — reused for the gRPC `Check`.

> Because `Check` is **annotate-only** (§3.7), the filter never blocks a request;
> `failure_mode_allow` only decides behaviour when auth-svc itself is *down*.
> `true` keeps public/anonymous pages up during an auth-svc blip; the API stays
> protected because enforcement lives at the backend, not the edge.

### 4.2 Route buckets + per-route enable/disable

In [`proxy/envoy.rds.yaml.tmpl`](../proxy/envoy.rds.yaml.tmpl) (and the vhost
template) implement the doc's route table. Since `Check` is **annotate-only**
(Q1), there's no "enforce" bucket at the edge — the filter just runs (to inject
identity) or is turned **off** where annotation is pointless or harmful:

- `/assets/*`, hashed `*.js`/`*.css`, `/otlp/` → ext_authz **OFF** (`disabled: true`), CDN-cacheable, nothing to annotate.
- `/signin`, `/signup`, `/signin-oidc`, `/signout`, `/reset` → route to the `auth` cluster as **plain HTTP** (these are browser redirects, not gRPC), ext_authz **OFF** (these *are* the flow).
- `/`, `/pricing` → ext_authz **ON** (annotate). Identity present → SSR personalizes; absent → renders anonymous.
- `/app/*` → web cluster, ext_authz **ON** (annotate), `Cache-Control: private, no-store`. **The SSR/SPA redirects to `/signin` when identity is absent** (§7) — the edge does not.
- `/api/*`, `/payments/*` → backend, ext_authz **ON** (annotate). **The backend rejects calls without a valid `x-internal-jwt`** (§6) — that's the enforcement point.

Per-route disable (for the OFF rows above) looks like:

```yaml
typed_per_filter_config:
  envoy.filters.http.ext_authz:
    "@type": type.googleapis.com/envoy.extensions.filters.http.ext_authz.v3.ExtAuthzPerRoute
    disabled: true
```

Add the new auth-flow routes alongside the existing `/payments/` and `/api/`
entries, and **delete the `/auth/` gRPC-web route** from
[`proxy/envoy.rds.yaml.tmpl`](../proxy/envoy.rds.yaml.tmpl) — nothing calls auth
over gRPC-web anymore (resolved decision). The OIDC HTTP endpoints replace it,
and ext_authz `Check` dials the `auth` **cluster** directly via the filter's
`grpc_service`, bypassing the route table. **Keep the `auth` cluster** — it still
backs both the ext_authz filter and the `/signin…/reset` HTTP routes.

### 4.3 Host preservation

ext_authz realm resolution depends on the original `Host`. Confirm
`cloudflared` ingress does **not** rewrite Host (doc gotcha #1), and that Envoy
forwards `:authority`/`Host` to the `Check` call (it does, via request headers).

---

## 5. Phase 4 — AppHost (dev) wiring

In [`apphost/Program.cs`](../apphost/Program.cs):

1. **Import the realm** into the dev Keycloak resource:
   ```csharp
   var keycloak = builder.AddKeycloak("keycloak", 8080)
       .WithRealmImport("../infra/keycloak/realms");
   ```
2. **Wire auth-svc dependencies** — Redis, the `auth` DB, Keycloak, and the
   `Auth_`/`Tenants` config:
   ```csharp
   var auth = builder.AddProject<Projects.ProtoFast_Auth_Api>("auth")
       .WithOtlpCollectorReference(otel)
       .WithReference(redis)
       .WithReference(authDb)          // postgres.AddDatabase("auth-db", ...)
       .WithReference(keycloak)
       .WaitFor(keycloak);
   ```
   (Capture `redis`/`authDb` from the existing `AddRedis` / `AddDatabase` calls.
   **Rename** the DB resource from `auth-db` to `auth` so the injected key is
   `ConnectionStrings__auth`, matching prod — see [§3.5.5](#355-reconciliations-before-coding).)
3. **Wire the migration runner** (Phase 2b) so the dev `auth` DB is migrated and
   the "Rebuild" command is available:
   ```csharp
   builder.WithSchemaMigrations<Projects.ProtoFast_Auth_SchemaMigrations>(authDb, "auth-migrations");
   ```
4. Provide dev secrets via user-secrets / env: `Auth_Keycloak__ClientSecretProtofastWeb`, `Auth_InternalJwt__PrivateKeyPem` (EC P-256 private key; backends get the matching public key), SMTP creds. Don't commit them.
5. Envoy already has `.WithUpstreamEndpoint("AUTH", auth.GetEndpoint("http"))`; no change needed for the cluster, but make sure the proxy `WaitFor(auth)` stays.

For local SMTP, add a mail catcher (e.g. a `Mailhog`/`smtp4dev` container) and
point Keycloak's SMTP at it so verification emails are visible in dev.

---

## 6. Phase 5 — Backend trust (`api`, `payments`)

The architecture doc has the backend trust an injected **internal JWT**, not the
Keycloak token. In `services/api` and `services/payments`:

1. Add JWT validation for `x-internal-jwt` — **ES256, verified with the public
   key only** (issuer/audience from `InternalJwtOptions`; the backends never hold
   the private key). A small gRPC interceptor or ASP.NET auth handler that reads
   the header, validates signature + `iss`/`aud`/`exp`, and builds the
   `ClaimsPrincipal`. Distribute the public key as static config now (e.g.
   `Shared_InternalJwt__PublicKeyPem`), or fetch it from a JWKS endpoint on
   auth-svc (keyed by `kid`) once rotation is needed.
2. **Reject calls without a valid internal JWT on protected methods.** Since the
   edge only annotates and never denies (Q1), this is the **primary** API
   enforcement point, not just defence in depth — the backend must not trust the
   network. Return `Unauthenticated` so the SPA can react.
3. SSR (`clients/host`) forwards `x-internal-jwt` on its server-side `/api`
   calls (it receives the header from ext_authz; see §7 of the doc's identity
   relay diagram).

---

## 7. Phase 6 — Angular clients

Because login is **Keycloak-hosted**, the client work is small:

1. **Entry points** — “Sign in” → `<a href="/signin">`, “Create account” →
   `<a href="/signup">`, “Sign out” → `<a href="/signout">`. No credential forms
   in Angular.
2. **Protected area — the app enforces, not the edge (Q1).** ext_authz only
   *annotates*; it never blocks. So for `/app/*` the SSR/SPA itself checks for
   identity and, when absent, **redirects to `/signin`** — server-side in the SSR
   handler (preferred: no flash of protected chrome) and/or via an Angular route
   guard. This is exactly what lets the same origin serve anonymous pages (`/`,
   `/pricing`) and authenticated ones without an edge gate fighting you.
3. **SSR identity** — in `clients/host` / `clients/protofast` server code,
   read `x-user-id`, `x-tenant`, `x-roles` from the incoming request (injected by
   ext_authz when a session exists; **absent = anonymous**) to render personalized
   or anonymous HTML, and emit `Cache-Control: private, no-store` on `/app/*`
   responses.
4. **No tokens in the browser** — the SPA never sees a Keycloak token; it relies
   on the session cookie + server-rendered identity. API calls go through Envoy
   (`/api/*`); the **backend** rejects unauthenticated calls (it validates
   `x-internal-jwt`), so a missing session surfaces as a 401 the SPA can handle.

Do this for both existing clients (`protofast`, `admin`). The `admin` client gets
silent SSO (Flow A) for free because it shares the realm.

---

## 8. Phase 7 — Deployment

1. **Realm JSON** — copy `infra/keycloak/realms/protofast-realm.json` →
   `deploy/keycloak/realms/protofast-realm.json` (the prod `--import-realm`
   mount). Keep them identical (per the dir README).
2. **Secrets** (single SM secret, `Auth_` prefix — see memory):
   - `Auth_Keycloak__ClientSecretProtofastWeb`, `Auth_Keycloak__ClientSecretAdmin`
   - `Auth_InternalJwt__PrivateKeyPem` — EC P-256 **private** key, auth-svc only.
   - `Auth_Smtp__Password`
   The matching **public** key goes to `api`/`payments` as `Shared_InternalJwt__PublicKeyPem`
   (non-secret, but ship it alongside). Populated out-of-band; never in TF state;
   never in the realm JSON.
3. **auth-svc env** in [`deploy/docker-compose.host-b.yml`](../deploy/docker-compose.host-b.yml):
   Redis + `auth` DB connection (the DB/role already exist via initdb), Keycloak
   authority, `Tenants__ByHost__*`, and the `Auth_*` secrets mounted as env.
4. **Schema migrations** — add the `auth-migrations` job service to the compose
   file, the `AUTH_MIGRATIONS_TAG` manifest line, the `deploy.sh` pre-step, and
   the `deploy-auth-migrations.yml` build workflow. Full design in
   [§3.5.3–3.5.4](#353-prod--one-shot-job-via-compose--deploysh). The job runs
   before the `auth` container is recreated and fails the apply if it errors.
5. **Keycloak hostname** — set `KEYCLOAK_DOMAIN=auth.protofast.dev` (already
   templated). Confirm `KC_PROXY_HEADERS=xforwarded`.
6. **Cloudflare** — apply the doc's cache rules: `private, no-store` on `/app/*`,
   bypass cache on any `Set-Cookie`, **cache key includes `Host`** (the single
   highest-risk item — tenant cache bleed). Exclude `/signin-oidc` and the
   Keycloak back-channel from WAF/bot/CAPTCHA challenges (gotcha #4).
7. **IMDS / S3 pull** — unrelated to auth, but the split-deploy IMDS hop-limit-2
   fix applies to the host that pulls images; verify on first deploy (see memory
   `split-deploy-imds-risk`).

---

## 9. Phase 8 — Testing

- **Unit** (`ProtoFast.Auth.UnitTests`): session id generation, cookie parsing,
  tenant resolution from Host, internal-JWT minting, `Check` allow/deny logic
  (mock the session store + JWKS).
- **Integration** (`ProtoFast.Auth.IntegrationTests`): spin up Keycloak +
  Redis + Postgres via **Testcontainers**, import the realm, drive the full
  `/signin → Keycloak → /signin-oidc → cookie` flow with a headless client, then
  assert `Check` returns OK + injected headers. Mirror the sample's
  `TestAuthWebApplicationFactory` + `appsettings.Testing.json` pattern.
- **End-to-end**, the two doc flows:
  - **Flow A** — sign in on `protofast.dev`, then hit `admin.protofast.dev/app`; assert **silent** SSO (no second password prompt) because the realm is shared.
  - **Flow B** — sign up on `protofast.dev` (registration → verify email → first-login provisioning → `/app`). Confirm a session cookie from one host is **rejected** if presented with a mismatched tenant.
- **Passkey** — manual: enrol a passkey via the account console, sign in with it, confirm `Check` issues a session identically to password sign-in.

---

## 10. Security checklist

- [ ] Cookie: `Secure; HttpOnly; SameSite=Lax`, **host-only** (no `Domain=`).
- [ ] PKCE `S256` + `state` on every authorize; `state` validated on callback.
- [ ] `Check` validates Keycloak token signature (JWKS), issuer (realm), and `azp`/`aud` (client) — not just "cookie exists".
- [ ] Cross-tenant replay rejected: session realm must match Host-resolved tenant.
- [ ] ext_authz is **annotate-only** (never denies); enforcement is SSR/SPA for HTML and the backend's `x-internal-jwt` check for APIs. `failure_mode_allow: true` (degrade to anonymous on auth-svc outage; API stays fail-closed).
- [ ] Identity headers (`x-user-id`, `x-tenant`, `x-roles`, `x-internal-jwt`) are **stripped from the inbound request on every request** (anti-spoofing), authenticated or not.
- [ ] Backend independently validates `x-internal-jwt` (don't trust the network) — the **only** API enforcement point now that the edge doesn't deny.
- [ ] Internal JWT is **ES256**; the EC **private** key lives only on auth-svc, backends hold the public key only; tokens carry `exp`/`aud`/`iss` and a `kid`.
- [ ] `/app/*` responses: `Cache-Control: private, no-store`. CDN cache key includes `Host`.
- [ ] WAF excludes `/signin-oidc` + Keycloak back-channel.
- [ ] No secrets in realm JSON or TF state; all under the `Auth_` SM prefix.
- [ ] Brute-force / lockout enabled in Keycloak; password policy set.
- [ ] Verify-email enforced; SMTP credentials from secret.
- [ ] Migration runner runs as the `auth` role (not superuser); `--rebuild-schema` hard-blocked in Production; advisory-locked against concurrent runs.

---

## 11. Open questions to confirm before coding

_None outstanding — all resolved below._

### Resolved decisions

- **`/auth/` gRPC-web route** — **removed.** Nothing calls auth over gRPC-web: the
  flow is HTTP redirects, ext_authz `Check` dials the `auth` cluster directly via
  the filter, and business gRPC-web goes to `/api`. The `auth` cluster stays
  (ext_authz + `/signin…/reset` routes); only the `/auth/` route is deleted. See §4.2.

- **Passkeys** — use the **GA WebAuthn Passwordless policy**, not the `passkeys`
  preview feature. Real passkeys via *Require Resident Key*; defer
  autofill/usernameless until passkeys is GA in a pinned 26.x. See §2.5.
- **Internal JWT signing** — **ES256 (asymmetric)**: auth-svc signs with an EC
  P-256 private key, `api`/`payments` verify with the public key only. The token
  is **minted once and cached in the Redis session** (~5-min TTL), re-minted only
  near expiry — not per request. See §3.9 / §6.
- **Deny UX** — ext_authz is **annotate-only; it never denies**. Enforcement
  moves off the edge: the SSR/SPA redirects to `/signin` (or renders anonymous)
  for HTML, and the backend rejects calls without a valid `x-internal-jwt` for
  APIs. Lets one origin serve both anonymous and authenticated pages.
  `failure_mode_allow: true`. See §3.7 / §4 / §6 / §7.
- **Session lifetime** — **sliding 8h idle, hard 7d absolute cap**, never
  exceeding Keycloak's refresh-token lifetime; the opaque session id **rotates on
  refresh** (with a ~30s grace). Mirrored in Keycloak (SSO idle 8h / max 7d).
  Secure-leaning defaults, all config-tunable. See §3.4 / §2.7.
- **Migration delivery** — a **dedicated one-shot container**
  (`protofast-auth-migrations`) that runs, migrates, and exits; invoked by
  `deploy.sh` before the `auth` container is recreated (fail-closed). Not an
  alternate entrypoint on the service image. See §3.5.3 / §3.5.4.

---

## 12. Suggested implementation order

1. Keycloak realm export (§2) — get login/registration/verify/passkey working in dev against the admin console first.
2. AppHost realm import + auth-svc deps (§5).
3. `AuthDbContext` + schema-migrations runner + dev `WithSchemaMigrations` (§3.5) — get the `auth` schema applied in dev.
4. auth-svc: OIDC endpoints + Redis session (§3.1–3.6) — get a cookie issued end-to-end.
5. auth-svc: ext_authz `Check` (§3.7) + Envoy filter (§4) — gate `/app`.
6. First-login provisioning (§3.8) + internal JWT (§3.9, §6).
7. Angular entry points + SSR identity (§7).
8. Prod migration job wiring (§3.5.3–3.5.4).
9. Tests (§9), security pass (§10), then deploy (§8).
