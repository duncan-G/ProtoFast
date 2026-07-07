# Plan: Inject dev secrets into the Aspire AppHost via Aspire parameters

**Scope:** the Aspire AppHost only (`apphost/`). This plan does **not** change how the
auth/api/payments services or the deployed (publish-mode) environments source secrets.

**Hard constraint:** **no secret values committed to git.** Nothing credential-shaped lands in
`appsettings.json`, `Program.cs`, the realm JSON, or any other tracked file.

**Status:** proposal / not yet implemented.

---

## 1. Problem

Today the AppHost injects "dev secrets" as compile-time `const` strings declared in
[`apphost/Auth/AuthDevEnvVars.cs`](../apphost/Auth/AuthDevEnvVars.cs) and wires them into
resources with `WithEnvironment(...)` in [`apphost/Program.cs`](../apphost/Program.cs):

| Constant | Consumed by | Class |
| --- | --- | --- |
| `ProtofastWebClientSecret` | `auth` → `Auth_Keycloak__ClientSecretProtofastWeb`; Keycloak realm import | secret |
| `AdminClientSecret` | `auth` → `Auth_Keycloak__ClientSecretAdmin`; Keycloak realm import | secret |
| `InternalJwtPrivateKeyPem` | `auth` → `Auth_InternalJwt__PrivateKeyPem` | secret (keypair) |
| `InternalJwtPublicKeyPem` | `payments`, `api` → `Shared_InternalJwt__PublicKeyPem` | secret (keypair) |
| `InternalJwtKeyId` | `auth` → `Auth_InternalJwt__KeyId` | **non-secret** |
| `SmtpFromEmail` | `keycloak` (smtp4dev) → `SMTP_FROM` | **non-secret** |

These values are also duplicated as committed fallbacks in the realm import,
`infra/keycloak/realms/protofast-realm.json`:

```json
"secret": "${env.PROTOFAST_WEB_CLIENT_SECRET:dev-protofast-web-secret}",
"secret": "${env.ADMIN_CLIENT_SECRET:dev-admin-secret}",
```

We want these surfaced as **Aspire parameters** (visible/configurable in one place, the
dashboard) **without committing any secret to git** — and the result must run for **every
developer on a fresh clone**, not just whoever set it up.

### Why the obvious approaches fail one goal or the other

- **Commit dev defaults** (the previous draft): works for everyone, but **commits secrets** — rejected.
- **`AddParameter(secret: true)` with no default**: doesn't commit anything, but the value lands
  in **per-user** `~/.microsoft/usersecrets/<id>/secrets.json`. Only the developer who typed it
  in can run; teammates and fresh clones get an unconfigured/ prompting AppHost. Fails "works
  for all users."

We need values that are **not in git** yet **materialise automatically for every developer**.

---

## 2. Design: generate the dev secrets in the AppHost

The dev secrets are local-only throwaway values, so the AppHost can **manufacture them at
startup** rather than store them anywhere tracked. Two generation strategies, by value class:

### 2a. Client secrets → Aspire *generated parameters* (persisted per-user)

Model `keycloak-protofast-web-client-secret` and `keycloak-admin-client-secret` as Aspire
parameters with a **generated default**. On first run, Aspire generates a random value and
persists it to that developer's **user-secrets** store; subsequent runs reuse it. This is the
same mechanism Aspire uses for auto-generated Postgres/Redis passwords.

- **Nothing in git** — the value is random and lives only in the local user-secrets file.
- **Works for every developer** — generation is automatic on first run; zero manual setup.
- **Stable across runs per developer** — required, because the Keycloak realm is imported into a
  **persisted** Postgres volume (`WithDataVolume`); a value that changed every run would drift
  from the already-imported realm.

**Single source of truth keeps Keycloak and auth in sync.** The realm JSON already supports
`${env.PROTOFAST_WEB_CLIENT_SECRET}`. The AppHost injects the *same* generated parameter into
**both** the Keycloak container (for realm import) **and** the auth service, so the secret
Keycloak expects always equals the secret auth presents — by construction, no manual matching.

### 2b. Internal JWT keypair → generated in code each run (ephemeral)

The `auth` private key and the `payments`/`api` public key are a **matched pair** that exists
only between our own services — no external store depends on it. So the AppHost generates an
EC P-256 keypair **in C# at startup**, injects the private half into `auth` and the public half
into `payments`/`api`, and keeps nothing.

- **Nothing in git**, nothing persisted.
- **Always a coherent pair** — generated together in one place.
- **Regenerating each run is fine** — all three consumers restart together and pick up the new
  pair; internal JWTs are short-lived. (Persisting it would add complexity for no benefit since
  nothing external references these keys.)

### 2c. Non-secret config → committed, plain

`internal-jwt-key-id` (`"dev-1"`) and `smtp-from-email` (`"no-reply@protofast.dev"`) are **not
secrets**. Keep them as ordinary committed parameters with default values (or plain
`WithEnvironment`). They can live in `appsettings.json` / `Program.cs` freely.

> **API note:** verify the exact generated-parameter overload for the pinned
> `Aspire.AppHost.Sdk/13.4.3` with `aspire docs search "AddParameter"` /
> `aspire docs search "GenerateParameterDefault"` before coding. The generate-and-persist-to-
> user-secrets behaviour and the `Parameters:<name>` resolution are stable across recent
> versions; the precise method shape is what to confirm.

---

## 3. Implementation steps

### 3.1 Enable user secrets on the AppHost

`apphost/ProtoFast.AppHost.csproj` — needed so Aspire has somewhere to persist generated
parameter values:

```xml
<PropertyGroup>
  <!-- ...existing... -->
  <UserSecretsId>protofast-apphost-dev</UserSecretsId>
</PropertyGroup>
```

Confirm the user-secrets file (and anything under it) is **not** tracked. It lives outside the
repo (`~/.microsoft/usersecrets/...`) by default, so this is automatic; just don't add a copy
inside the repo.

### 3.2 Client secrets as generated parameters

In `Program.cs` (representative — confirm overload names):

```csharp
// Random per-developer dev secret, generated on first run and persisted to *this* developer's
// user-secrets. Never committed. The SAME parameter is fed to Keycloak (realm import) and auth.
var protofastWebClientSecret = builder.AddParameter(
    "keycloak-protofast-web-client-secret",
    secret: true /* with a generated default */ );

var adminClientSecret = builder.AddParameter(
    "keycloak-admin-client-secret",
    secret: true /* with a generated default */ );
```

If the pinned SDK doesn't expose a one-call "generated secret parameter," fall back to a value
factory that generates once and persists, e.g. read `Parameters:<name>` from config and, when
absent, generate a random string and write it back to user secrets before first use. Keep this
helper in the AppHost; verify the supported API first.

Feed the realm import (in the Keycloak setup, see `AddKeycloak`/`WithRealmImport` wiring) and
auth from the same parameter:

```csharp
var keycloak = builder.AddKeycloak("keycloak", 8080)
    .WithSmtpDevServer(builder, smtpFromEmail)
    .WithEnvironment("PROTOFAST_WEB_CLIENT_SECRET", protofastWebClientSecret)
    .WithEnvironment("ADMIN_CLIENT_SECRET", adminClientSecret)
    .WithRealmImport("../infra/keycloak/realms");

var auth = builder.AddProject<Projects.ProtoFast_Auth_Api>("auth")
    // ...
    .WithEnvironment("Auth_Keycloak__ClientSecretProtofastWeb", protofastWebClientSecret)
    .WithEnvironment("Auth_Keycloak__ClientSecretAdmin", adminClientSecret)
    // ...
```

### 3.3 JWT keypair generated in code

```csharp
using System.Security.Cryptography;

static (string PrivatePem, string PublicPem) GenerateInternalJwtKeyPair()
{
    using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    return (ec.ExportPkcs8PrivateKeyPem(), ec.ExportSubjectPublicKeyInfoPem());
}

var (jwtPrivatePem, jwtPublicPem) = GenerateInternalJwtKeyPair();
```

Inject (string values; or wrap in parameters captured from these locals so the pair stays
matched):

```csharp
var auth = builder.AddProject<Projects.ProtoFast_Auth_Api>("auth")
    // ...
    .WithEnvironment("Auth_InternalJwt__PrivateKeyPem", jwtPrivatePem)
    .WithEnvironment("Auth_InternalJwt__KeyId", internalJwtKeyId);   // non-secret param

builder.AddProject<Projects.ProtoFast_Payments_Api>("payments")
    .WithOtlpCollectorReference(otel)
    .WithEnvironment("Shared_InternalJwt__PublicKeyPem", jwtPublicPem);

builder.AddProject<Projects.ProtoFast_Api>("api")
    .WithOtlpCollectorReference(otel)
    .WithEnvironment("Shared_InternalJwt__PublicKeyPem", jwtPublicPem);
```

### 3.4 Non-secret config

`internal-jwt-key-id` and `smtp-from-email` become ordinary parameters with committed defaults
(or plain `WithEnvironment` literals). `WithSmtpDevServer` takes the SMTP-from value as a
parameter instead of referencing `AuthDevEnvVars.SmtpFromEmail`.

### 3.5 Remove the constants file

Delete [`apphost/Auth/AuthDevEnvVars.cs`](../apphost/Auth/AuthDevEnvVars.cs) once all references
are migrated.

### 3.6 Realm JSON committed fallback

The realm JSON still carries `${env.PROTOFAST_WEB_CLIENT_SECRET:dev-protofast-web-secret}`. Now
that the AppHost always supplies the env var, the `:dev-protofast-web-secret` fallback is dead
code for the AppHost path (only used by a standalone realm import). To honour "no secrets in
git," replace the fallback with an obviously-fake sentinel or drop the default so a standalone
import fails fast without the env var:

```json
"secret": "${env.PROTOFAST_WEB_CLIENT_SECRET}",
```

(Strictly this edits `infra/keycloak`, just outside the AppHost; flagged here because it's the
last committed secret-looking string in the chain. Decide whether to include it.)

---

## 4. Why this satisfies "works for all users" with nothing in git

| | In git? | Auto-materialises for a fresh clone? |
| --- | --- | --- |
| Client secrets | No (generated → user-secrets, per developer) | Yes — generated on first `aspire run` |
| JWT keypair | No (generated in code each run) | Yes — generated at startup |
| Key id / SMTP from | Yes (not secrets) | Yes |

No developer has to receive a secret out-of-band or run a setup script. Each gets a working,
internally-consistent set of dev secrets the first time they launch the AppHost, and none of it
is committed.

---

## 5. Migration / rollout notes

- **Existing developers with a persisted Keycloak volume** imported the realm under the old
  hardcoded `dev-protofast-web-secret`. After switching to generated secrets, their freshly
  generated value won't match the already-imported realm. One-time fix when adopting: wipe the
  Keycloak Postgres data volume (or force a realm re-import) so the realm re-imports with the
  new generated secret. New clones are unaffected.
- The internal JWT keypair change needs no volume reset (nothing persists it).

---

## 6. Verification

1. On a **clean checkout with no user secrets**, `aspire run`. Confirm client-secret parameters
   are generated (visible, masked, in the dashboard) and `auth`/`payments`/`api`/Keycloak all
   start and authenticate end-to-end — proving zero-setup works for a brand-new user.
2. `aspire describe` / dashboard env inspection: each consuming resource receives the same env
   var names it does today (`Auth_Keycloak__ClientSecret*`, `Auth_InternalJwt__*`,
   `Shared_InternalJwt__PublicKeyPem`, `SMTP_FROM`).
3. Confirm the Keycloak realm secret and the auth client secret **match** (login via the
   protofast-web client succeeds) — validates the single-source-of-truth wiring.
4. Restart the AppHost: client secrets stay stable (reused from user-secrets); JWT pair may
   rotate but auth↔payments/api token validation still succeeds.
5. `git status` / `git grep` for any secret-shaped string — confirm nothing credential-like is
   tracked.

---

## 7. Out of scope

- Service-level secret loading (services keep reading the same env vars / config keys).
- Publish/production secret sourcing (Secrets Manager, deploy pipeline) — unchanged.

---

## 8. Checklist

- [ ] Add `<UserSecretsId>` to `ProtoFast.AppHost.csproj`.
- [ ] Verify generated-parameter API for Aspire 13.4.3 (`aspire docs search`).
- [ ] Add generated client-secret parameters; inject into **both** Keycloak and auth.
- [ ] Generate the internal JWT keypair in code; inject private→auth, public→payments/api.
- [ ] Keep key id / SMTP-from as committed non-secret config.
- [ ] Delete `AuthDevEnvVars.cs`.
- [ ] Decide on realm JSON fallback (remove the committed `dev-*-secret` default).
- [ ] Clean-checkout run with no user secrets succeeds end-to-end.
- [ ] One-time Keycloak volume reset documented for existing developers.
- [ ] `git grep` confirms no secret values are tracked.
