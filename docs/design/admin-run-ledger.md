# Admin run ledger console

An admin-only view of the document import engine: every run the ledger recorded, every stage
attempt inside it, and the executor input and output artifacts each attempt read and wrote.
Ordinary users never see the ledger; the admin console at `admin.protofast.dev` is the only way in.

> **Status (2026-10-07).** Built as part of theplot's console, on the access model of
> [admin-consoles.md](admin-consoles.md) rather than §2–§3 below (operators realm, `admin-theplot`,
> no base `admin` role). What exists:
>
> - read model: `IRunInspector` and `IDocumentFamilyDirectory` in the Engine project, Postgres
>   implementations in Data, one new table `engine.document_families` for what an operator writes
>   about a family, registered in the api by `AddDurableEngineAdministration()`;
> - RPCs: `TheplotRuns` (list, run, transcript, artifact, executor, skill) and `TheplotFamilies`
>   (list, get, create, update, reset) in `Protos/Admin/Theplot/theplot_engine.proto`, every one
>   opening with `AdminAccess` for `theplot`;
> - pages in `clients/admin-theplot`: `/runs`, `/runs/:id` (stages, artifact side sheet deep-linked
>   as `?artifact=stage/hash`, the agent's conversation), `/families`, `/families/:family`,
>   `/executors/:id/:version`, `/skills/:id/:version`.
>
> Not done from this document: §4.1 (stamping runs with their app). Every import today is
> theplot's, so the RPCs serve the whole ledger to `admin-theplot`; before a second app imports
> documents, runs need `app`/`source_id` columns and the RPCs a filter on them. `GetArtifact` is
> unary with a 1 MiB default cap rather than streaming.

The console serves every client app (protofast, theplot, and any app added later) from that one
host. Which apps a signed-in operator sees is decided by which per-app admin roles their account
holds (§2, §5).

```
 browser ──▶ Envoy (admin vhost) ──▶ /api/ ──▶ api: AdminRuns gRPC ──▶ engine.runs / stage_records
   │              │ ext_authz                         │  tenant = protofast   / run_decisions / run_progress
   │              ▼                                   │  role admin-{app}
   │       auth: session only if                      ▼
   │       roles ∋ admin on this host             IArtifactStore (S3)  runs/{runId}/{stageId}/{hash}
   │                                              IRegistry            engine/executors/{hash}.json
   ▼
 clients/admin  /app, /app/:app/runs, /app/:app/runs/:runId, artifact panel
                (apps listed from the admin-{app} roles in x-roles)
```

## 1. What exists already

| Piece | Where | State |
|---|---|---|
| Run ledger tables | `engine.runs`, `engine.stage_records`, `engine.run_decisions`, `engine.run_progress` ([WorkflowEngineDbContext](../../services/document_import/src/ProtoFast.DocumentImport.Data/Postgres/WorkflowEngineDbContext.cs)) | Written by the worker. Stage records are the serialised `StageRecord` JSON plus `recorded_at`. No owner column: the ledger is deliberately not user-scoped. No app or source column either: nothing on a run says which app's upload it read (§4.1). |
| Artifacts | S3 bucket, keys from [ArtifactKeys](../../services/document_import/src/ProtoFast.DocumentImport.Storage/ArtifactKeys.cs) | Frozen. Each stage's inputs and output are `ArtifactRef(runId, stageId, hash)`; the run input sits under the upload id as `$input`. The contract lives beside each artifact. |
| Executor specs and playbooks | Postgres registry rows + `engine/executors/{hash}.json` in S3 ([IRegistry](../../services/document_import/src/ProtoFast.DocumentImport.Engine/Storage/IRegistry.cs)) | Resolvable by `ExecutorRef` (`id@version`), which every `StageRecord` carries. Engine-wide, not per app. |
| Ledger in the api | `AddDurableRunLedger()` in [Program.cs](../../services/api/src/ProtoFast.Api/Program.cs) | The api already reads `run_progress`, but only for ids the caller's own rows vouch for. |
| Roles end to end | Keycloak `realm_access.roles` → `SessionData.Roles` → internal JWT `roles` claims → `x-roles` header → [CallerIdentity.RequireRole](../../services/shared/Grpc/CallerIdentity.cs) | Plumbed but unused: neither realm defines a role and nothing calls `RequireRole`. The internal JWT also carries `tenant` (the realm), but `CallerIdentity` does not read it. |
| Admin host | `admin.protofast.dev` → protofast realm, `admin` client, `MaxAge` 900 s, optional `AcrValues` ([05-identity.md](../05-identity.md)) | Any protofast-realm account can sign in. Registration is open on that realm, so today "admin" means "anyone". |
| Admin client | [clients/admin](../../clients/admin) | SSR gate on `x-user-id`, `authGuard`, a placeholder dashboard, gRPC-web transport to `/api`. [AuthIdentityService](../../clients/admin/src/app/auth/auth-identity.ts) already parses `x-roles` and `x-tenant` during SSR and transfers them to the browser. |

So the work is: define the roles, gate three layers on them, tag runs with their app, add
read-only admin RPCs over the ledger and artifact store, and build the pages.

## 2. Access model

### 2.1 One operator account, one role per app

`TenantResolver` maps a host to exactly one realm and client, and the admin host maps to the
`protofast` realm. So an operator is **one account in the protofast realm**, however many apps
they administer. "An admin account for theplot" is not an account in the theplot realm; it is the
`admin-theplot` role on that protofast account.

Why not an admin account inside each app's own realm:

- the admin host would need one realm per app: a host per app (`admin.theplot…`) or a realm
  picker before sign-in, and then one session and one passkey per app for the same person;
- each app realm has open, user-facing registration, its own theme and a credential model tuned
  for end users; operator privilege would sit among those accounts;
- keeping app realms free of roles means no end-user token can ever carry an admin role, whatever
  a realm JSON edit does by mistake.

An operator who also uses theplot as an ordinary user signs up there separately; the theplot
account and the protofast operator account are unrelated.

### 2.2 The roles

Realm roles in the `protofast` realm (realm roles are per realm and have nothing to do with
Keycloak's own `master` admin):

| Role | Kind | Meaning |
|---|---|---|
| `admin` | plain; never granted directly | may sign in to the console |
| `admin-protofast`, `admin-theplot`, … | composite, each containing `admin` | may see that app's data |

The app id is the name of the app's own realm, which is also the `tenant` claim its users' tokens
carry, so `admin-theplot` covers what `tenant=theplot` users produce. A new client app with its
own realm (the "adding a tenant" path in [05-identity.md](../05-identity.md)) adds one role,
`admin-<realm>`; nothing else in the model changes.

The composite is what makes a grant a single mapping. Keycloak puts effective roles, composites
expanded, into `realm_access.roles`, so granting `admin-theplot` makes the token carry both
`admin-theplot` and `admin`, and revoking an operator's last app role takes `admin` away with it.
Nothing ever has to keep `admin` in step by hand.

Both realm JSON copies (`infra/keycloak/realms/protofast-realm.json`,
`deploy/keycloak/realms/protofast-realm.json`) declare all of them under `roles.realm`, the app
roles with `composite: true` and `composites.realm: ["admin"]`, so a fresh realm has them.
Existing realms get them from the grant script in §3, because the deploy reconcile never touches
roles. The theplot realm never gets any of them.

`admin` and `protofast-web` have `fullScopeAllowed: true` and the realm's default `roles` scope, so
the roles land in `realm_access.roles` on the access token and
[KeycloakClaims.ReadRealmRoles](../../services/auth/src/ProtoFast.Auth.Api/Keycloak/KeycloakClaims.cs)
already reads them. Confirm once on the first sign-in after a grant by checking that `x-roles` in
the SSR request holds both `admin` and `admin-theplot`; if it is empty, the `roles` client scope
was not defaulted on the realm and must be added to the client.

### 2.3 Three gates, one of them real

| Layer | Change | Purpose |
|---|---|---|
| **api (enforcement)** | Every `AdminRuns` RPC starts with `AdminAccess.Require(context, app)`: `CallerIdentity.From(context)` must have tenant `protofast` **and** role `admin-{app}`. `app` comes from the request for `ListRuns` and from the run row for everything keyed by run id. A run in an app the caller does not administer answers `NotFound`, exactly like a missing run, so run ids cannot be probed across apps. `GetExecutor` needs only `admin`: executors and playbooks are engine-wide and hold no user data. Anything else is `PermissionDenied` with the existing non-revealing message. | The only gate that matters. The edge annotates and never denies; the JWT is the trust boundary. The tenant check means a role named `admin-theplot` in some other realm can never count. |
| **auth (sign-in)** | `TenantConfig.RequiredRole` beside `AcrValues`. In the callback ([AuthFlow.cs](../../services/auth/src/ProtoFast.Auth.Api/Endpoints/AuthFlow.cs), next to the acr check), if the host's tenant names a required role and `identity.Roles` lacks it: log, no session, redirect to `/forbidden`. Set `Tenants__ByHost__admin.protofast.dev__RequiredRole=admin` in `deploy/docker-compose.host-services.yml` and the dev `localhost+20000` entry in `appsettings.Development.json`. | A non-admin never gets a session on the admin host at all, so the console is not an empty shell for the whole user base. It checks the base `admin` role, so auth config never lists apps. |
| **admin client (UX)** | The SSR gate in `server.ts` and `authGuard` require `admin` in `x-roles` / `identity.roles`; otherwise render the 403 page. Routes under `/app/:app` add `adminAppGuard`, which requires `admin-{app}` (§5.2). | Belt and braces; stops in-app navigation into an app's pages showing a spinner that ends in a gRPC error. |

`CallerIdentity` gains a `Tenant` property read from the `tenant` claim. Nothing else about it
changes, and the user-facing services stay exactly as they are: no RPC ever lists runs for a user,
`DocumentService` keeps asking the ledger only about ids the caller's rows vouch for.

### 2.4 Revocation

Roles are re-read on every access-token refresh and the internal JWT is re-minted from them
(`SessionResolver` comment: "roles may have changed — always re-mint"), and the admin host forces
re-authentication every `MaxAge` seconds. Removing an app role therefore removes that app from
the console, and removing the last one removes the console, within one Keycloak access-token
lifetime and at most 15 minutes. That is acceptable for a read-only console; no extra session-kill
path is needed.

## 3. Registering an admin account

There is no admin sign-up form, on purpose. An admin is an ordinary protofast-realm account that
has been granted one app role per client app it should see, by someone who already has Keycloak's
Admin API.

**Script**: `scripts/keycloak-grant-admin.py --app APP [--app APP …] [--revoke] EMAIL`, shaped like
[keycloak-apply-account-admin-client.py](../../scripts/keycloak-apply-account-admin-client.py).
Idempotent:

1. ensure realm role `admin` exists, and that `admin-<app>` exists for each `--app` as a composite
   containing `admin` (create either with a description if missing; add the composite child if a
   hand-made role lacks it);
2. look up the user by exact email;
3. add the realm-role mapping `admin-<app>` for each `--app` (or remove it with `--revoke`); never
   map `admin` directly, it arrives through the composite;
4. print the user's subject and their effective `admin*` roles, nothing else.

`--app` is required and checked against the script's list of known apps (`protofast`, `theplot`),
so a typo cannot mint `admin-teplot`. Adding a client app adds it to that list.

| To administer | Grant |
|---|---|
| theplot only | `--app theplot` |
| protofast only | `--app protofast` |
| both | `--app protofast --app theplot` |

Env: `KC_URL`, `KC_ADMIN_USER`/`KC_ADMIN_PASSWORD` (dev) or a client-credentials pair (prod),
`KC_REALM=protofast`, and the email as the positional argument.

**Dev**

1. Register at `https://localhost:20001` (protofast-web) with an address smtp4dev will show at
   `http://localhost:8025`. Finish the email code. This is the operator account for every app,
   theplot included; do not register on `:20002` for it.
2. Grant: `KC_URL=http://localhost:8080 KC_ADMIN_USER=admin KC_ADMIN_PASSWORD=admin scripts/keycloak-grant-admin.py --app theplot --app protofast you@example.test`.
   Or click it in the Keycloak console at `localhost:8080` → protofast → Users → Role mapping,
   assigning the `admin-*` roles.
3. Sign in at `https://localhost:20000`. The `MaxAge` re-auth means the session opened on :20001
   is not silently reused.

Do not put a dev admin user in the realm JSON: the same file creates the prod realm.

**Prod**

1. Register at `https://protofast.dev` as normal.
2. Mint the throwaway Keycloak service account exactly as the account-admin script documents
   (`kc.sh bootstrap-admin service …`), run the grant script against `https://auth.protofast.dev`
   with the apps this person should see, then delete that client from the master realm again. No
   standing Keycloak admin credential is left behind, which keeps the current security posture.
3. Sign in at `https://admin.protofast.dev`; with `ADMIN_ACR_VALUES=passkey` a passkey is required.

## 4. Backend

All of it lives in the existing `api` project: it already has the ledger, the S3 object store and
the admin vhost's `/api/` route pointing at it. A separate admin service would duplicate that
wiring for no isolation gain, because the gate is the JWT, not the process.

### 4.1 Tagging runs with their app

A run knows its signature and mode, not whose upload it read, so the api cannot scope by app yet.
The app has to be stamped where it is known, when the import is requested, and carried to the
ledger:

- `DocumentImportRequested` gains `App`. `DocumentUploadService` sets it from
  `CallerIdentity.Tenant`, never from a request field.
- The worker passes it to `RunDispatcher.RunAsync(input, app, ct)`, which hands it to the
  scheduled, shadow and discovery runs it opens, so every run for an upload inherits it.
- `IRunLedger.OpenAsync` gains a `RunOrigin(string App, string SourceId)`, with `SourceId` the
  input's `ArtifactRef.RunId` (the upload id). This is the only change to the write-side contract.
- `engine.runs` gets `app` and `source_id` columns. The migration backfills `app = 'theplot'`,
  the only app that imports documents today, and `source_id` from `run_progress` where it names
  the run, null otherwise.

`source_id` on the run replaces the `run_progress` join the detail page would otherwise need,
which only ever finds the latest run for a source and never a shadow run.

### 4.2 Read model over the ledger

Add an admin read interface in `ProtoFast.DocumentImport.Engine/Storage/` with its Postgres
implementation in `ProtoFast.DocumentImport.Data/Postgres/` (one type per file):

```csharp
public interface IRunInspector
{
    Task<RunPage> ListAsync(RunListQuery query, CancellationToken ct);   // newest first, cursor by (opened_at, run_id)
    Task<RunDetail?> FindAsync(string runId, CancellationToken ct);
}

public sealed record RunListQuery(string App, string? Family, RunMode? Mode, RunStatus? Status, string? Cursor, int Take);
public enum RunStatus { Open, Closed }
public sealed record RunHeader(string RunId, string App, Signature Signature, RunMode Mode, DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt, TraceRef? Trace, int StageAttempts, bool Passed, decimal Cost);
public sealed record RunPage(IReadOnlyList<RunHeader> Runs, string? NextCursor);
public sealed record RecordedStage(StageRecord Record, DateTimeOffset RecordedAt);
public sealed record RunDetail(RunHeader Header, IReadOnlyList<RecordedStage> Stages,
    IReadOnlyList<Decision> Decisions, string? SourceId, RunProgress? Progress);
```

- `App` is required on the list query: there is no cross-app listing, even for an operator who
  holds every app role. The dashboard asks once per app.
- `RunHeader.Passed` is "the last non-shadow attempt at every stage passed"; `Cost` sums
  `Result.Cost.Amount` over the run's records. Both are computed from the deserialised records in
  the page, not stored, because `stage_records.record` is opaque JSON.
- Migration in `ProtoFast.DocumentImport.Data`: index `runs (app, opened_at DESC, run_id)` for the
  list.
- `Take` capped at 100.

Register `IRunInspector`, `IArtifactStore` (`S3ArtifactStore`) and `IRegistry` (`PostgresRegistry`)
in the api. `AddDurableRunLedger()` grows an `AddDurableRunInspector()` sibling rather than pulling
the whole `AddDurableWorkflowEngineStores()` in, which would also start the outcome consumer.

### 4.3 gRPC surface

`services/api/src/ProtoFast.Api/Protos/admin_runs.proto`, service `AdminRuns`:

| RPC | Request | Reply | Role |
|---|---|---|---|
| `ListRuns` | app, family, mode, status, cursor, take | `RunHeader[]`, next_cursor | `admin-{app}` |
| `GetRun` | run_id | header (with app), stages (stage id, executor `id@version`, tier, shadow, passed, degraded, verdicts with findings, cost, duration, decisions, inputs `ArtifactRef[]`, output `ArtifactRef`, recorded_at), run decisions, source_id, progress | `admin-{run.app}` |
| `GetArtifact` | run_id, stage_id, hash | server-streaming: first message carries contract (schema id, version), size and content type; then 64 KiB `bytes` chunks | `admin-{run.app}` |
| `GetExecutor` | id, version | `ExecutorSpec` fields plus the resolved playbook's instructions and rules | `admin` |

Notes:

- There is no "which apps can I see" RPC. The client reads that from `x-roles` (§5.1); the api
  answers every data RPC on its own authority, so a client that shows the wrong app gets
  `PermissionDenied` and nothing else.
- `GetArtifact` streams because the `$input` manuscript can be megabytes. The client renders the
  first 256 KiB and offers "download all". Keys are built only through `ArtifactKeys`, which escapes
  each segment, so a request cannot reach outside `runs/`. The run row is loaded first for its
  app, so an artifact is never served for a run the caller cannot see.
- Executor **input** in the UI means "the artifacts this attempt read" (`StageRecord.Inputs`),
  which is exactly what `StagePrompt.BuildAsync` concatenates. The prompt text itself is not
  stored today; §8 covers capturing it.
- Every RPC runs `AdminAccess.Require` before touching any store other than the run row it needs
  for the app.
- `AdminRunsService` maps engine records to messages; the mapping is the only place that knows
  `StageRecord` shape, so a later ledger change touches one file.

## 5. Admin client

### 5.1 How the console decides which apps to show

The roles are already in the page. Envoy's `Check` injects `x-roles` on every request,
[AuthIdentityService](../../clients/admin/src/app/auth/auth-identity.ts) parses it during SSR and
hands it to the browser through `TransferState`. No RPC is involved.

- **Catalogue**: `src/app/apps/admin-apps.ts` lists the apps the console knows how to present,
  as `{ id, label, sections }`: `theplot` with `['runs']`, `protofast` with `[]` until it has
  something to administer. It is presentation only; the api never reads it.
- **Visible apps**: `AdminAppsService.visible` is the catalogue entries whose `admin-{id}` is in
  `identity.roles`, in catalogue order. A role with no catalogue entry is ignored (the realm may be
  ahead of the client); an entry without its role is absent, not greyed out.
- **Dashboard** (`/app`): one card per visible app, each linking to its sections, with the runs
  counts and latest failures on apps that have `runs`. Exactly one visible app redirects straight
  to `/app/{id}`. None renders the 403 page (only reachable if someone mapped `admin` directly).
- **Header**: an app switcher when more than one app is visible; the current app comes from the
  `:app` route parameter.
- **Changes**: identity is resolved per request from the session, so a grant or revoke shows up on
  the first full page load after the next token refresh (§2.4). An SPA left open keeps its old
  list until then; the api has already stopped answering for a revoked app.

### 5.2 Routes

Under the existing `/app` gate in [app.routes.ts](../../clients/admin/src/app/app.routes.ts).
Everything app-scoped sits under `/app/:app`, guarded by `adminAppGuard`: an unknown app id is
404, a known one without its role, or a section not in its `sections`, is 403.

| Route | Page |
|---|---|
| `/app` | Dashboard: the visible apps (§5.1). |
| `/app/:app` | The app's landing page: its sections; for `runs`, counts per family and mode and the latest failures. |
| `/app/:app/runs` | Table: run id, family + facets, mode, opened, duration, attempts, passed, cost, trace id. Filters for family, mode, open/closed. Cursor paging. |
| `/app/:app/runs/:runId` | Header, run decisions, then the stage attempts in ledger order. Each attempt shows executor (link), tier, shadow tag, verdicts and findings, cost and duration, its decisions, and chips for each input artifact and the output artifact. A run whose app differs from `:app` redirects to its own app's URL. |
| artifact panel (side sheet on the run page) | Contract, size, pretty-printed JSON when it parses, raw text otherwise, copy and download. Deep-linkable as `/app/:app/runs/:runId/artifacts/:stageId/:hash`. |
| `/app/executors/:id/:version` | The spec: tier, model class, tools, origin, promoted, playbook instructions and learned rules. Not under `:app`: executors are engine-wide. |
| `/forbidden` | Static 403 for a signed-in non-admin (auth redirects here; the SSR gate and `adminAppGuard` render it). |

Code layout mirrors theplot: `src/app/runs/admin-runs-api.ts` wraps the generated client and maps
messages to plain interfaces (as `document-api.ts` does), pages under `src/app/pages/runs/`.
Add the proto to `buf.gen.yaml` (it already points at the api's `Protos` directory, so only
`npm run gen` is needed). Styling stays with the Tailwind utility classes the dashboard uses.

## 6. Adding a client app

Everything app-specific is one name, the app's realm:

1. the app's realm and tenant entry, as [05-identity.md](../05-identity.md) describes;
2. `admin-<realm>` (composite of `admin`) in both protofast realm JSON copies, and `<realm>` in the
   grant script's known apps; run the script once to create the role on existing realms;
3. a catalogue entry in `admin-apps.ts` with the sections it supports;
4. its uploads stamp `App` from the caller's tenant, which `DocumentUploadService` already does
   for any tenant.

No auth config, api gate or route changes.

## 7. Tests

- **api unit**: `AdminRunsService` with a `TestServerCallContext` carrying: no admin role;
  `admin-theplot` but tenant `theplot`; `admin-protofast` asking `ListRuns(app: theplot)`
  (`PermissionDenied`) and `GetRun` for a theplot run (`NotFound`); `admin-theplot` with tenant
  `protofast` (reaches the inspector). `GetExecutor` with `admin` alone succeeds.
- **api integration**: `DocumentUploadService` stamps `App` from the caller's tenant.
- **DocumentImport integration**: `PostgresRunInspectorTests` beside `PostgresRunLedgerTests`:
  app filter, paging order and cursor, open/closed filter, `Passed` and `Cost` derivation,
  `SourceId`. `PostgresRunLedgerTests` for `RunOrigin` on open; the migration backfill.
- **auth**: `SignInLoopTests` case for `RequiredRole` missing → no cookie, redirect to `/forbidden`;
  present → session as today. `TenantResolverTests` for the new property binding.
- **admin client**: `AdminAppsService` spec (roles → visible apps, unknown roles ignored);
  `adminAppGuard` spec (404 unknown app, 403 missing role or section); dashboard single-app
  redirect; `admin-runs-api` mapping spec.
- **manual**: the dev flow in §3 end to end, including the `x-roles` check in §2.2, once with
  `--app theplot` only (protofast absent, redirect to `/app/theplot`) and once with both.

## 8. Order of work

1. Roles and gates: realm JSON roles with composites, `RequiredRole` in auth, `Tenant` on
   `CallerIdentity`, grant script, client 403. Ships alone and already closes the "anyone can enter
   the console" gap.
2. App tagging: `App` on `DocumentImportRequested`, `RunOrigin` through the dispatcher and ledger,
   `runs.app` / `runs.source_id` migration with backfill.
3. `IRunInspector` + Postgres implementation + migration + integration tests.
4. `admin_runs.proto`, `AdminRunsService`, `AdminAccess`, api registration, unit tests.
5. Client: app catalogue, `AdminAppsService`, dashboard and switcher, then list, detail, artifact
   panel, executor page.
6. Later, if wanted:
   - persist the built prompt per attempt (worker writes it as a `{stageId}.prompt` artifact) so
     the panel can show the literal executor input;
   - link `source_id` to the upload's owner (needs an unscoped upload lookup in the api, which the
     ThePlot repositories deliberately do not offer today);
   - a trace id link into the tracing backend;
   - registry browsing (playbooks, workflows, promotions) under `admin`;
   - per-app sections beyond runs (users, uploads) on the same `admin-{app}` roles.
