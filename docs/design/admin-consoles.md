# Per-app admin consoles

Every client app (protofast, theplot, and any app added later) gets its own admin console. Two
constraints shape the design:

- **Operators are disjoint.** The people who administer app 1 are not the people who administer
  app 2, and over time they are not us either. Nothing an app-1 operator can see or run may touch
  app 2.
- **Each app's admin code is written by that app's agent.** A bounded agent adds features to its
  app's console. From every other app's point of view that code is untrusted.

So isolation has to come from the browser origin, the process and the token. Route guards inside a
shared bundle are not enough. Each app's console is its own client on its own host, built on a
platform layer we own and the agent cannot change.

The first feature on that platform is the run ledger: an operator's view of the document import
engine's runs, stage attempts and artifacts for their app (§6). The engine and its ledger are on
`feature/agent-workflow-engine`, not yet on `main`.

```
                          platform (ours)                               per app (agent)
 browser ─▶ admin.theplot.protofast.dev ─▶ Envoy vhost admin-theplot
   │            │ ext_authz ──▶ auth: session only if roles ∋ admin-theplot
   │            │               (operators realm, admin-theplot client)
   │            ├─ /        ──▶ clients-host container, CLIENTS=admin-theplot ──▶ clients/admin-theplot
   │            └─ /api/    ──▶ api: platform admin RPCs, AdminAccess.Require("theplot")
   │                              └─ engine.runs (app = theplot), artifacts, ...
   ▼
 admin.protofast.dev  ──▶ clients/admin: platform console for us, role `platform`
```

## 1. What exists on `main`

| Piece | Where | State |
|---|---|---|
| Roles end to end | Keycloak `realm_access.roles` → [KeycloakClaims](../../services/auth/src/ProtoFast.Auth.Api/Keycloak/KeycloakClaims.cs) → internal JWT `roles` → `x-roles` → [CallerIdentity.RequireRole](../../services/shared/Grpc/CallerIdentity.cs) | Plumbed but unused: no realm defines a role and nothing calls `RequireRole`. The internal JWT carries `tenant` (the realm); `CallerIdentity` does not read it. |
| Host → realm/client | [TenantOptions](../../services/auth/src/ProtoFast.Auth.Api/Configuration/TenantOptions.cs), `Tenants:ByHost` | `admin.protofast.dev` → `protofast` realm, `admin` client, `MaxAge` 900 s, optional `AcrValues`. Registration is open on that realm, so today "admin" means anyone. |
| Session isolation | auth's session cookie | Host-only (no `Domain`), and `SessionResolver` rejects a session whose realm does not match the host's tenant. Separate hosts are already separate sessions in prod. |
| Admin client | [clients/admin](../../clients/admin) | SSR gate on `x-user-id`, `authGuard`, placeholder dashboard, gRPC-web to `/api`. [AuthIdentityService](../../clients/admin/src/app/auth/auth-identity.ts) already parses `x-roles` / `x-tenant`. |
| SSR hosting | [clients/host](../../clients/host/server.mjs) | One Node process imports every client's server bundle and dispatches on Envoy's `x-client`. Fine while we write every client ourselves. |
| Generated clients | `buf.gen.yaml` in each client | Generates every proto in the api's `Protos` directory. |

## 2. Why a console per app, not one console with an area per app

A single console at `admin.protofast.dev` that shows each operator only their apps works while
the code is ours. It stops working once each app's agent writes that app's admin code:

- **Same origin.** All apps' code runs on one origin. App 1's code can call anything the signed-in
  operator's session allows, and app 2's lazy-loaded code is a public download from the same host.
  Guards hide routes; they do not contain code.
- **Same SSR process.** The unified clients host imports every server bundle into one Node process.
  App 1's server code would share memory, environment and request stream with every other client.
- **Same build.** One agent's broken change blocks the other apps' deploys.

Per-app hosts cost more per app (a vhost, a container, a Keycloak client, a tenant entry, a
pipeline), but that cost is what buys the isolation. §8 turns it into a template.

## 3. Platform and app split

| Layer | Per app: the agent may write | Platform: ours, agent cannot change |
|---|---|---|
| Frontend | `clients/admin-{app}/`, its own Angular project | `clients/admin-kit/`: shell, auth gate, `authGuard`, gRPC transport, account menu, design tokens. Consumed as a workspace library. |
| Host | — | `admin.{app}.protofast.dev`, its DNS record, Envoy vhost and CSP |
| Rendering | — | Its own `clients-host` container with `CLIENTS=admin-{app}`: the existing image, one process per admin app |
| Data | Calls platform RPCs only | Admin RPCs in the `api` project, each starting with `AdminAccess.Require(context, app)` |
| Protos | — | `Protos/Admin/Shared/` and `Protos/Admin/{App}/`; each admin client's `buf.gen.yaml` lists only those two directories |
| Identity | — | `operators` realm, one Keycloak client per admin host, `admin-{app}` roles |

The existing `clients/admin` at `admin.protofast.dev` stays as **our** console, gated on the
`platform` role: engine-wide executors and playbooks, and anything cross-app.

### 3.1 Isolation, layer by layer

- **Origin.** Each console is its own host, so a session on `admin.app1…` is never sent to
  `admin.app2…`. Dev is weaker: every listener is `localhost` and the browser ignores ports, so
  all consoles share one cookie jar. Tenant matching in `SessionResolver` still keeps sessions
  apart, but treat dev as single-operator.
- **Process.** Admin bundles never load into the shared clients host. Each runs in its own
  `clients-host` container (prod) or its own `AddClientApp` dev server (dev), with no secrets in
  its environment beyond what SSR needs today.
- **Exfiltration.** Envoy sets the CSP on each admin vhost: `connect-src 'self'`, scripts and
  styles only from the host itself. Agent code cannot post to a third party, because the header is
  added by a layer the agent does not write.
- **Data.** The console runs with its operator's token and nothing more. Every admin RPC checks
  tenant `operators` and role `admin-{app}`. A request for another app's data gets `NotFound` (by
  id) or `PermissionDenied` (by app), whatever the console's code does.
- **Build.** Each admin app has its own CI and deploy workflow, builds only its own project and
  uploads to its own S3 prefix. A failure stays in that app.

## 4. Identity

### 4.1 The `operators` realm

Operators get a dedicated realm with **registration off**, beside `protofast-realm.json` and
`theplot-realm.json` in both `infra/keycloak/realms/` and `deploy/keycloak/realms/`. Why not the
existing realms:

- **The protofast realm** is protofast's end-user realm, with open sign-up. App operators are not
  protofast users and should not sit among them.
- **Each app's own realm** would put admin roles next to that app's public sign-up and end-user
  accounts. Keeping app realms free of roles means no end-user token can ever carry an admin role,
  whatever a realm JSON mistake does.

The realm holds one confidential client per console: `admin` (ours), `admin-theplot`,
`admin-protofast`, and so on, plus the usual `account-admin` client so operators can manage their
own passkeys.

### 4.2 Roles

| Role | Kind | Meaning |
|---|---|---|
| `admin-{app}` | plain | may sign in to that app's console and read that app's admin data |
| `platform` | plain | may sign in to `admin.protofast.dev`; engine-wide data (executors, playbooks, registry) |

The app id is the name of the app's own realm, which is also the `tenant` claim its users' tokens
carry. So `admin-theplot` covers what `tenant=theplot` users produce.

There is no shared base `admin` role. A role that every operator holds becomes a cross-app
permission the moment an RPC is gated on it. Engine-wide data goes behind `platform` instead:
playbooks carry learned rules, and those may be derived from any app's documents.

### 4.3 Gates

| Layer | Change | Purpose |
|---|---|---|
| **api** (enforcement) | `AdminAccess.Require(context, app)`: `CallerIdentity` must have `Tenant == "operators"` **and** role `admin-{app}`. `app` comes from the request for list RPCs and from the stored row for anything keyed by id; a row in another app answers `NotFound`, like a missing one. `AdminAccess.RequirePlatform` for engine-wide RPCs. | The gate that matters. The edge annotates and fails open; the JWT is the trust boundary. The tenant check means a role of the same name in another realm never counts. |
| **auth** (sign-in) | `TenantConfig.RequiredRole` beside `AcrValues`. In the [AuthFlow](../../services/auth/src/ProtoFast.Auth.Api/Endpoints/AuthFlow.cs) callback, next to the acr check: if the host names a required role and `identity.Roles` lacks it, log, no session, redirect to `/forbidden`. | An operator of app 1 never gets a session on app 2's host at all. |
| **admin-kit** (UX) | SSR gate and `authGuard` require the console's role in `x-roles`; otherwise the 403 page. The role comes from the console's config, not from the app's code. | Stops a spinner that ends in a gRPC error. |

`CallerIdentity` gains `Tenant`, read from the `tenant` claim. The user-facing services do not
change.

### 4.4 Tenant entries

| Host | Realm | Client | Extra |
|---|---|---|---|
| `admin.protofast.dev` | `operators` | `admin` | `RequiredRole=platform`, `MaxAge`, `AcrValues` |
| `admin.theplot.protofast.dev` | `operators` | `admin-theplot` | `RequiredRole=admin-theplot`, `MaxAge`, `AcrValues` |
| dev `localhost+20000` | `operators` | `admin` | `RequiredRole=platform` |
| dev `localhost+20003` | `operators` | `admin-theplot` | `RequiredRole=admin-theplot` |

Dev ports follow `WithClient` registration order in [Program.cs](../../apphost/Program.cs), so
`admin-theplot` registered after `theplot` lands on 20003.

### 4.5 Granting and revoking

There is no operator sign-up. `scripts/keycloak-grant-admin.py --app APP [--app APP …] [--platform] [--revoke] EMAIL`,
shaped like [keycloak-apply-account-admin-client.py](../../scripts/keycloak-apply-account-admin-client.py),
idempotently:

1. ensures each named role exists in the `operators` realm;
2. creates the user by email if missing (registration is off), with a required action to register
   a passkey on first sign-in;
3. adds or removes the role mappings;
4. prints the subject and the user's effective `admin-*` / `platform` roles.

`--app` is checked against a list of known apps, so a typo cannot mint `admin-teplot`. In prod,
mint the throwaway Keycloak service account the account-admin script documents, run the grant,
and delete that client again, so no standing Keycloak admin credential stays behind.

Roles are re-read on every access-token refresh and the internal JWT is re-minted from them, and
admin hosts force re-authentication every `MaxAge` seconds. A revoke takes effect within one
access-token lifetime and at most 15 minutes. That is enough while consoles are read-mostly.

## 5. The agent's boundary

The agent is bounded by mechanism, not by its prompt:

- **Write scope**: `clients/admin-{app}/` only. CODEOWNERS on everything else, including
  `clients/admin-kit/`, `services/`, `proxy/`, `infra/`, `deploy/` and `apphost/`.
- **Imports**: lint rules allow only `@angular/*`, the approved UI dependencies, `admin-kit` and
  the app's own generated client. No new npm dependencies without review.
- **Data**: frontend only, to start. The agent composes features from platform RPCs. A feature that
  needs new data is a request for a new RPC under `Protos/Admin/{App}/`, which a human reviews and
  we implement.
- **Delivery**: changes arrive as PRs on the app's own pipeline. The pipeline builds and tests
  only that project and deploys only that app's bundle.

If frontend-only stops being enough, the next step is a per-app admin service, not code in `api`:
its own process, a Postgres role limited to the app's rows (row-level security on `app`), an S3
policy limited to its prefix, and engine tables reachable only through platform RPCs. Do not let
agent-written backend code into a process that holds every app's credentials.

## 6. First platform feature: the run ledger

The ledger itself (`engine.runs`, `stage_records`, `run_decisions`, `run_progress`, S3 artifacts
under `runs/{runId}/{stageId}/{hash}`, and the executor/playbook registry) lands with
`feature/agent-workflow-engine`. This section is the work to expose it, per app.

### 6.1 Tagging runs with their app

A run knows its signature and mode, not whose upload it read. Stamp the app where it is known:

- `DocumentImportRequested` gains `App`; `DocumentUploadService` sets it from
  `CallerIdentity.Tenant`, never from a request field.
- The worker passes it to `RunDispatcher.RunAsync(input, app, ct)`, and every scheduled, shadow
  and discovery run for that upload inherits it.
- `IRunLedger.OpenAsync` gains `RunOrigin(string App, string SourceId)`, `SourceId` being the
  upload id.
- `engine.runs` gets `app` and `source_id`, backfilled `app = 'theplot'` (the only importer today),
  and an index on `(app, opened_at DESC, run_id)`.

### 6.2 Read model

`IRunInspector` in the engine's `Storage/`, Postgres implementation in the data project:
`ListAsync(RunListQuery)` (newest first, cursor by `(opened_at, run_id)`, `App` required, `Take`
capped at 100) and `FindAsync(runId)` returning header, recorded stages, decisions, source id and
progress. `Passed` and `Cost` are computed from the deserialised stage records, not stored. The
api registers it through an `AddDurableRunInspector()` sibling of `AddDurableRunLedger()`.

### 6.3 RPCs

`Protos/Admin/Shared/admin_runs.proto`, service `AdminRuns`: shared because every app with
imports gets the same view.

| RPC | Gate |
|---|---|
| `ListRuns(app, family, mode, status, cursor, take)` | `admin-{app}` |
| `GetRun(run_id)` | `admin-{run.app}` |
| `GetArtifact(run_id, stage_id, hash)`: server-streaming, contract first, then 64 KiB chunks | `admin-{run.app}` |

`GetExecutor` and registry browsing go in `Protos/Admin/Platform/`, gated on `platform`, and only
`clients/admin` generates them.

`GetArtifact` loads the run row first for its app, builds keys only through `ArtifactKeys`, and so
never serves an artifact for a run the caller cannot see.

### 6.4 Pages

In `admin-kit`, so every app console gets them by adding a route: runs list
(`/runs`), run detail (`/runs/:runId`), artifact side panel
(`/runs/:runId/artifacts/:stageId/:hash`). There is no `:app` segment: the host is the app.

## 7. Adding a client app's console

1. The app's realm and tenant entry ([05-identity.md](../05-identity.md)).
2. `admin-{app}` role and client in both `operators` realm copies; the app in the grant script's
   known apps.
3. `proxy.WithClient(builder, "admin-{app}")` and `AddClientApp` in the AppHost; the vhost, CSP
   and its own upstream cluster in the Envoy templates; the DNS record; a `clients-host` service
   with `CLIENTS=admin-{app}` in compose.
4. Tenant entries for the prod host and the dev port.
5. `clients/admin-{app}` scaffolded from the template: `admin-kit` shell, its role, `buf.gen.yaml`
   limited to `Admin/Shared` and `Admin/{App}`.
6. Its pipeline and CODEOWNERS entry; then hand the folder to the app's agent.

Steps 2–6 become one scaffolding script once the first console is built by hand.

## 8. Tests

- **api**: `AdminAccess` with a test call context: no role; `admin-theplot` with tenant `theplot`
  (denied); `admin-protofast` listing theplot runs (`PermissionDenied`) and fetching a theplot run
  (`NotFound`); `admin-theplot` with tenant `operators` (allowed); `platform` alone cannot list
  runs.
- **auth**: sign-in case for `RequiredRole` missing (no cookie, redirect to `/forbidden`) and
  present; `TenantResolverTests` for the new property.
- **DocumentImport**: `PostgresRunInspectorTests` (app filter, paging, open/closed, `Passed` and
  `Cost`, `SourceId`); `RunOrigin` on open; the backfill.
- **admin-kit**: role gate, 403 page, runs pages and API mapping.
- **Isolation checks in CI**: each admin client's generated code contains no other app's protos;
  each admin vhost serves the CSP header; the clients host's `CLIENTS` never lists an admin app.
- **Manual**: grant `--app theplot` only. The theplot console signs in and its SSR request carries
  `x-roles: admin-theplot`; `admin.protofast.dev` redirects to `/forbidden` with no session.

## 9. Order of work

1. **Identity**: `operators` realm, roles, `RequiredRole`, `CallerIdentity.Tenant`, grant script,
   `admin.protofast.dev` moved to `operators` + `platform`. Ships alone and closes "anyone can enter
   the admin console".
2. **admin-kit**: pull the shell, auth gate, transport and account menu out of `clients/admin`;
   `clients/admin` becomes its first consumer.
3. **First app console by hand**: `clients/admin-theplot`, its host, vhost, CSP, container, tenant
   entries and pipeline.
4. **Run ledger** (after the engine merges): app tagging, `IRunInspector`, `AdminRuns`, pages in
   `admin-kit`.
5. **Agent boundary**: CODEOWNERS, import lint, per-app pipeline gates; then give the theplot agent
   its folder.
6. **Scaffold script** for further apps; then the protofast console.
7. **Later**: per-app admin services if frontend-only falls short (§5); store the built prompt per
   stage attempt so the artifact panel can show the literal executor input; trace links.
