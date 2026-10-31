# Per-app admin consoles

Every client app (protofast, theplot, and any app added later) gets its own admin console. Two
constraints shape the design:

- **Operators are disjoint.** The people who administer app 1 are not the people who administer
  app 2, and over time they are not us either. Nothing an app-1 operator can see or run may touch
  app 2.
- **Each app's admin code is written by that app's agent.** A bounded agent adds features to its
  app's console. From every other app's point of view that code is untrusted.

There is one admin host, `admin.protofast.dev`. What an operator's roles allow decides which
console's code is served to them at all. Each console is its own Angular project in its own SSR
process, built on a platform layer we own and the agent cannot change.

```
                                   platform (ours)                          per app (agent)
 browser ─▶ admin.protofast.dev ─▶ ProtoStrap's edge, host `admin`
              │ asks auth at /ext-authz/<path>: session only if roles ∋ some console role (operators realm)
              ├─ /theplot/   auth: 302 /signin or /forbidden unless roles ∋ admin-theplot
              │              ─▶ clients-admin-theplot (CSP: connect-src 'self') ─▶ clients/admin-theplot
              ├─ /admin.     ─▶ api: admin RPCs, AdminAccess.Require(context, "theplot")
              └─ /           ─▶ clients (CSP) ─▶ clients/admin: the console list, platform pages, account
```

## 1. What is built

| Piece | Where |
|---|---|
| `operators` realm: registration off, no social sign-in, one `admin` client, `platform` / `admin-protofast` / `admin-theplot` roles | `services/keycloak-config/realms/operators-realm.json` |
| `admin` client removed from the protofast realm; `protofast-web`'s redirect URIs come from `ORIGIN_PROTOFAST` alone | `protofast-realm.json` |
| `TenantConfig.RequiredRoles`: no session without one of them; a session that loses its last one is dropped at the next refresh | [AuthFlow](../../services/auth/src/ProtoFast.Auth.Api/Endpoints/AuthFlow.cs), [SessionResolver](../../services/auth/src/ProtoFast.Auth.Api/Sessions/SessionResolver.cs) |
| `CallerIdentity.Tenant`, `AdminAccess.Require` / `RequireRow` / `RequirePlatform` | [services/shared/Grpc](../../services/shared/Grpc/AdminAccess.cs) |
| Admin RPCs: `Admin/Shared` (`AdminOverview`) and `Admin/Theplot` (`TheplotAdmin`, `TheplotRuns`, `TheplotFamilies`) | `services/api/.../Protos/Admin`, `Services/Admin` |
| The engine as the console reads it: `IRunInspector`, `IDocumentFamilyDirectory` (+ `engine.document_families`), registered in the api by `AddDurableEngineAdministration()` | `ProtoFast.DocumentImport.Engine/Storage`, `Engine/Families`, `Data/Postgres`; see [admin-run-ledger.md](admin-run-ledger.md) |
| `clients/admin-kit`: identity, guards, gRPC transport, telemetry, shell, account menu, 403/404 pages, the SSR server and its gate | consumed through a `src/admin-kit` symlink |
| `clients/admin`: the platform console — the console list, `/app/platform`, `/app/account`, `/forbidden` | |
| `clients/admin-theplot`: theplot's console at `/theplot/` — overview, stories, runs (stage attempts, artifacts, the agent's conversation), document families (describe, register, reset), executors, skills | |
| The console gate: auth's answer to the edge redirects anyone without the console's role | `Tenants:ByHost:admin:Consoles` in auth's `appsettings.json`, [ExtAuthzEndpoint](../../services/auth/src/ProtoFast.Auth.Api/Endpoints/ExtAuthzEndpoint.cs) |
| The route and the CSP | `admin/theplot/` in `apphost/Program.cs`; `ADMIN_CSP` in `clients/host/host.mjs` and the dev servers' `angular.json` |
| Its own component in an account (`clients-admin-theplot`), its own dev server locally | `apphost/Program.cs` |
| Isolation checks in CI; CODEOWNERS | `scripts/check-admin-console.py` (the `clients` job of `ci.yml`), `.github/CODEOWNERS` |
| Grant script and console scaffold | `scripts/keycloak-grant-admin.py`, `scripts/scaffold-admin-console.py` |

## 2. One host, a process per console

A host per console (`admin.theplot.protofast.dev`) gives each console its own origin. It also
costs a DNS record, a Keycloak client, a tenant entry and a second-level certificate (Cloudflare's
free certificate does not cover `*.theplot.protofast.dev`) for every app. We chose one host instead,
and kept everything else separate:

- **Code.** A console's bundle is served only to holders of its role. The edge asks auth about
  every request to `/theplot/`, assets included, and auth answers a redirect to anyone without
  `admin-theplot`, so an operator without it cannot even download theplot's console.
- **Process.** Each app console runs in its own clients-host component (`clients-admin-theplot`)
  in an account, or its own dev server locally. The shared clients host refuses to load one
  beside another client, so agent-written server code shares no memory, environment or request
  stream with another app's.
- **Data.** Every admin RPC checks tenant `operators` and role `admin-{app}`, whatever a console's
  code does.
- **Exfiltration.** The clients host sets the CSP on every console's responses: scripts and
  styles only from the host, `connect-src 'self'`. The admin dev servers set the same header.
- **Build.** Each console is its own Angular project with its own build stage in
  `clients/Dockerfile`; CI builds, tests and checks each one separately. The image holds every
  console, and `CLIENTS` decides which one a process loads.

**The trade-off.** All consoles share one origin. An operator who holds two console roles runs
both consoles' code on that origin, so app 1's console could call app 2's admin RPCs with that
operator's session. Operators who hold one role, which is the expected case, are unaffected.
Grant more than one console role only to people we would trust with both apps anyway.

ProtoStrap's edge never lets a request through unasked: while auth is down or slower than two
seconds, the admin host answers 503. The edge always removes a client-supplied `x-roles`
first, so only auth's can reach a console's SSR process.

## 3. Platform and app split

| Layer | Per app: the agent may write | Platform: ours, agent cannot change |
|---|---|---|
| Frontend | `clients/admin-{app}/` | `clients/admin-kit/`, linked in as `src/admin-kit` (a symlink, built with `preserveSymlinks`, so Angular compiles it and resolves `@angular/*` from the console's own `node_modules`) |
| Routing | — | `admin/{app}/` in the AppHost, the `Consoles` entry in auth's admin tenant, the CSP in the clients host |
| Rendering | — | Its `clients-admin-{app}` component with `CLIENTS=admin-{app}` and `ADMIN_CONSOLE_ROLES=admin-{app}` |
| Data | Calls admin RPCs only | Admin RPCs in `api`, each opening with `AdminAccess` |
| Protos | — | `Protos/Admin/Shared/` and `Protos/Admin/{App}/`; the console's `buf.gen.yaml` lists only those two, and the product clients exclude `Admin/` |
| Identity | — | `operators` realm, `admin-{app}` role, the admin host's `RequiredRoles` |

The role a console's SSR gate admits comes from `ADMIN_CONSOLE_ROLES`, set by the AppHost rather
than by the console. The gate is for the operator's benefit; the api is the gate
that matters.

## 4. Identity

### 4.1 The `operators` realm

Operators get a dedicated realm with **registration off**. It uses the same passwordless browser
flow as the app realms (passkey or mailed code, with the step-up branch), and has no Google or
Apple sign-in. Keeping app realms free of admin roles means no end-user token can ever carry one,
whatever a realm JSON mistake does. Its passkey RP ID is the auth host's hostname
(`auth.protofast.dev` in prod), the same as theplot's; keycloak-config works it out from
`HOST_AUTH`.

### 4.2 Roles

| Role | Meaning |
|---|---|
| `admin-{app}` | opens that app's console and its admin data |
| `platform` | opens `/app/platform`; engine-wide and cross-app data. It does **not** open any app's data |

The app id is the name of the app's own realm. There is no shared base `admin` role: one that every
operator holds becomes a cross-app permission the moment an RPC is gated on it.

### 4.3 Gates

| Layer | Gate |
|---|---|
| **api** | `AdminAccess.Require(context, app)` (app named by the request) answers `PermissionDenied`; `RequireRow(context, rowApp, …)` (app read from the row) answers `NotFound`, like a missing row; `RequirePlatform`. All require `tenant == operators`. |
| **auth** | The admin host's `RequiredRoles`: an account holding none gets no session and is sent to `/forbidden`; a session whose refresh drops the last one is erased. |
| **auth, at the edge** | `/{app}/` on the admin host reaches the console's process only when auth's answer finds `admin-{app}` in the session's roles; otherwise a redirect to `/signin` or `/forbidden`. |
| **admin-kit** | The SSR gate signs anonymous visitors in and sends role-less ones to `/forbidden`; `consoleGuard` / `roleGuard` do the same on client-side navigation. |

### 4.4 Tenant entries

| Host name | Realm | Client | Extra |
|---|---|---|---|
| `admin` (`HOST_ADMIN`: `admin.protofast.dev` in prod, `admin.dev.localhost` locally) | `operators` | `admin` | `RequiredRoles`, `MaxAge` (900 s), `Consoles`; `AcrValues` left empty for now |

### 4.5 Granting and revoking

`scripts/keycloak-grant-admin.py [--app APP …] [--platform] [--revoke] EMAIL` ensures the roles
exist, creates the operator if missing (registration is off; a passkey is a required action on
first sign-in unless `--no-passkey`), adds or removes the mappings and prints the result. `--app` is
checked against the known apps. It needs Keycloak's admin API, which the edge doesn't route; in an
account only the overlay reaches it, and how to make an operator there is still to be decided,
as the script's own docstring says. Locally, `DEV_OPERATORS` in the AppHost creates the
operators keycloak-config seeds.

A revoke takes effect at the operator's next access-token refresh, within one token lifetime.

## 5. The agent's boundary

- **Write scope**: `clients/admin-{app}/` only; CODEOWNERS covers everything else, including the
  kit symlink inside that folder.
- **Imports and dependencies**: `scripts/check-admin-console.py` (run for each console in CI)
  allows only relative imports, `@angular/*`, `@connectrpc/*`, `@bufbuild/*`, `@opentelemetry/*`,
  `rxjs`, `tslib`, `express`, `node:*` and the reviewed exceptions `highlight.js` and `marked`,
  and fails on any other dependency the platform console does not have. It also fails on generated code from another app's protos, a kit link that points
  elsewhere, an app console listed in the shared host's `CLIENTS`, and a CSP that no longer pins
  `connect-src`.
- **Data**: frontend only. A feature that needs new data is a request for a new RPC under
  `Protos/Admin/{App}/`, which a person reviews and we implement.

If frontend-only stops being enough, the next step is a per-app admin service, not code in `api`:
its own process, a Postgres role limited to the app's rows, and shared tables reachable only
through platform RPCs.

## 6. Adding a client app's console

`scripts/scaffold-admin-console.py APP "Title"` writes the console project (from theplot's), its
proto folder, CODEOWNERS entries and its card on the console list, then prints the platform wiring
still done by hand: `KnownApps` (and the grant script's list), the `admin-{app}` role in the
operators realm file, `RequiredRoles` and `Consoles` in auth's admin tenant, the RPCs, the
AppHost's dev server and `clients-admin-{app}` component, a build stage in `clients/Dockerfile`,
`CONSOLE_PATHS` in the clients host, and the spec (`aspire do protostrap-spec`). App ids that collide with the host's own paths
(`app`, `api`, `forbidden`, …) are refused.

## 7. Tests

- **api** (`AdminAccessTests`): no role; `admin-theplot` minted in the theplot realm; another app's
  operator listing (`PermissionDenied`) and fetching by id (`NotFound`); `platform` alone; theplot's
  operator reading every writer's stories and the overview.
- **auth**: `RequiredRoles` binding and matching; a session without a console role dropped; a
  revoke ending the session at the next refresh; a callback without a console role issuing no
  cookie and redirecting to `/forbidden`; `/signin` on the admin host going to the operators realm.
- **admin-kit**: role matching, `consoleGuard`, `roleGuard` (runs in every console's suite).
- **CI**: `scripts/check-admin-console.py` per console.

## 8. Rolling it out

A release carries all of it at once: keycloak-config creates the `operators` realm with its roles,
auth gets its tenant entries, the api its admin RPCs, the edge the console route, and the clients
image every console. Operators then need granting in each account (see 4.5 for what is missing).
The protofast console waits for protofast to have admin data; scaffold it then.
