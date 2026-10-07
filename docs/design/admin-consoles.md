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
 browser ─▶ admin.protofast.dev ─▶ Envoy vhost `admin`  (CSP: connect-src 'self')
              │ ext_authz ──▶ auth: session only if roles ∋ some console role (operators realm)
              │ (clear_route_cache: routes see the trusted x-roles)
              ├─ /theplot/*  + x-roles ∋ admin-theplot ─▶ clients-admin-theplot ─▶ clients/admin-theplot
              ├─ /theplot/*  otherwise ─▶ platform console, x-console-denied ─▶ sign-in or /forbidden
              ├─ /api/       ─▶ api: admin RPCs, AdminAccess.Require(context, "theplot")
              └─ /           ─▶ clients host ─▶ clients/admin: the console list, platform pages, account
```

## 1. What is built

| Piece | Where |
|---|---|
| `operators` realm: registration off, no social sign-in, one `admin` client, `platform` / `admin-protofast` / `admin-theplot` roles | `infra/keycloak/realms/operators-realm.json` (and the `deploy/` copy) |
| `admin` client removed from the protofast realm; `protofast-web` no longer answers on the admin dev port | `protofast-realm.json` |
| `TenantConfig.RequiredRoles`: no session without one of them; a session that loses its last one is dropped at the next refresh | [AuthFlow](../../services/auth/src/ProtoFast.Auth.Api/Endpoints/AuthFlow.cs), [SessionResolver](../../services/auth/src/ProtoFast.Auth.Api/Sessions/SessionResolver.cs) |
| `CallerIdentity.Tenant`, `AdminAccess.Require` / `RequireRow` / `RequirePlatform` | [services/shared/Grpc](../../services/shared/Grpc/AdminAccess.cs) |
| Admin RPCs: `Admin/Shared` (`AdminOverview`) and `Admin/Theplot` (`TheplotAdmin`) | `services/api/.../Protos/Admin`, `Services/Admin` |
| `clients/admin-kit`: identity, guards, gRPC transport, telemetry, shell, account menu, 403/404 pages, the SSR server and its gate | consumed through a `src/admin-kit` symlink |
| `clients/admin`: the platform console — the console list, `/app/platform`, `/app/account`, `/forbidden` | |
| `clients/admin-theplot`: theplot's console at `/theplot/` — overview, stories | |
| Envoy console routes and CSP | `proxy/envoy.console-routes.yaml.tmpl`, `proxy/envoy.admin-csp.yaml.tmpl` |
| Its own container in prod (`clients-admin-theplot`), its own dev server in dev | `deploy/docker-compose.host-edge.yml`, `apphost/Program.cs` |
| Its own pipeline; isolation checks in CI; CODEOWNERS | `.github/workflows/deploy-client-admin-theplot.yml`, `scripts/check-admin-console.py`, `.github/CODEOWNERS` |
| Grant script and console scaffold | `scripts/keycloak-grant-admin.py`, `scripts/scaffold-admin-console.py` |

## 2. One host, a process per console

A host per console (`admin.theplot.protofast.dev`) gives each console its own origin. It also
costs a DNS record, a Keycloak client, a tenant entry and a second-level certificate (Cloudflare's
free certificate does not cover `*.theplot.protofast.dev`) for every app. We chose one host instead,
and kept everything else separate:

- **Code.** A console's bundle is served only to holders of its role. Envoy matches its path on the
  `x-roles` header that ext_authz has just rewritten, assets included, so an operator without
  `admin-theplot` cannot even download theplot's console.
- **Process.** Each app console runs in its own `clients-host` container (prod) or dev server
  (dev). The shared clients host never loads one, so agent-written server code shares no memory,
  environment or request stream with another app's.
- **Data.** Every admin RPC checks tenant `operators` and role `admin-{app}`, whatever a console's
  code does.
- **Exfiltration.** Envoy sets the CSP on the admin vhost: scripts and styles only from the host,
  `connect-src 'self'` (dev also allows the dev server's `wss://localhost:*` HMR socket).
- **Build.** Each console has its own pipeline, builds only its own project and ships only its
  own bundle.

**The trade-off.** All consoles share one origin. An operator who holds two console roles runs
both consoles' code on that origin, so app 1's console could call app 2's admin RPCs with that
operator's session. Operators who hold one role, which is the expected case, are unaffected.
Grant more than one console role only to people we would trust with both apps anyway.

Envoy's ext_authz fails open (`failure_mode_allow`). While auth is down, a client-supplied
`x-roles` is passed through unchanged and can reach a console's SSR process. The console then
renders chrome without data, because the api accepts only the internal JWT.

## 3. Platform and app split

| Layer | Per app: the agent may write | Platform: ours, agent cannot change |
|---|---|---|
| Frontend | `clients/admin-{app}/` | `clients/admin-kit/`, linked in as `src/admin-kit` (a symlink, built with `preserveSymlinks`, so Angular compiles it and resolves `@angular/*` from the console's own `node_modules`) |
| Routing | — | `/{app}/` on the admin vhost, its role match, the CSP |
| Rendering | — | Its `clients-admin-{app}` container with `CLIENTS=admin-{app}` and `ADMIN_CONSOLE_ROLES=admin-{app}` |
| Data | Calls admin RPCs only | Admin RPCs in `api`, each opening with `AdminAccess` |
| Protos | — | `Protos/Admin/Shared/` and `Protos/Admin/{App}/`; the console's `buf.gen.yaml` lists only those two, and the product clients exclude `Admin/` |
| Identity | — | `operators` realm, `admin-{app}` role, the admin host's `RequiredRoles` |

The role a console's SSR gate admits comes from `ADMIN_CONSOLE_ROLES`, set by the AppHost and
compose rather than by the console. The gate is for the operator's benefit; the api is the gate
that matters.

## 4. Identity

### 4.1 The `operators` realm

Operators get a dedicated realm with **registration off**. It uses the same passwordless browser
flow as the app realms (passkey or mailed code, with the step-up branch), and has no Google or
Apple sign-in. Keeping app realms free of admin roles means no end-user token can ever carry one,
whatever a realm JSON mistake does. Its passkey RP ID is `auth.protofast.dev`, the same as
theplot's: only two values are valid for a page on `auth.protofast.dev`.

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
| **Envoy** | `/{app}/` reaches the console's process only with `admin-{app}` in the trusted `x-roles`. |
| **admin-kit** | The SSR gate signs anonymous visitors in and sends role-less ones to `/forbidden`; `consoleGuard` / `roleGuard` do the same on client-side navigation. |

### 4.4 Tenant entries

| Host | Realm | Client | Extra |
|---|---|---|---|
| `admin.protofast.dev` | `operators` | `admin` | `RequiredRoles`, `MaxAge`, `AcrValues` |
| dev `admin.dev.localhost` | `operators` | `admin` | `RequiredRoles` |

### 4.5 Granting and revoking

`scripts/keycloak-grant-admin.py [--app APP …] [--platform] [--revoke] EMAIL` ensures the roles
exist, creates the operator if missing (registration is off; a passkey is a required action on
first sign-in unless `--no-passkey`), adds or removes the mappings and prints the result. `--app` is
checked against the known apps. In prod, run it with the throwaway service account that
`keycloak-apply-account-admin-client.py` documents, then delete that account.

A revoke takes effect at the operator's next access-token refresh, within one token lifetime.

## 5. The agent's boundary

- **Write scope**: `clients/admin-{app}/` only; CODEOWNERS covers everything else, including the
  kit symlink inside that folder.
- **Imports and dependencies**: `scripts/check-admin-console.py` (run in the console's pipeline)
  allows only relative imports, `@angular/*`, `@connectrpc/*`, `@bufbuild/*`, `@opentelemetry/*`,
  `rxjs`, `tslib`, `express` and `node:*`, and fails on any dependency the platform console does
  not have. It also fails on generated code from another app's protos, a kit link that points
  elsewhere, an app console listed in the shared host's `CLIENTS`, and a CSP that no longer pins
  `connect-src`.
- **Data**: frontend only. A feature that needs new data is a request for a new RPC under
  `Protos/Admin/{App}/`, which a person reviews and we implement.

If frontend-only stops being enough, the next step is a per-app admin service, not code in `api`:
its own process, a Postgres role limited to the app's rows, and shared tables reachable only
through platform RPCs.

## 6. Adding a client app's console

`scripts/scaffold-admin-console.py APP "Title"` writes the console project (from theplot's), its
proto folder, pipeline, CODEOWNERS entries and its card on the console list, then prints the
platform wiring still done by hand: `KnownApps`, the realm role and `RequiredRoles`, the RPCs, the
AppHost `WithConsole`, the compose service and Envoy env, and the bootstrap entry in `deploy.sh`.
App ids that collide with the host's own paths (`app`, `api`, `forbidden`, …) are refused.

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

1. Deploy Keycloak: `ensure_realm_imported` now checks every realm file, so the `operators` realm
   is imported on the next Keycloak apply. The live protofast realm keeps its old `admin` client
   until someone deletes it in the admin console. Nothing maps a host to it any more.
2. Deploy auth (tenant entries), api (admin RPCs), Envoy (console routes, CSP), the clients host
   and `client-admin`, then `client-admin-theplot`.
3. Grant the first operators with `keycloak-grant-admin.py`.
4. The protofast console waits for protofast to have admin data; scaffold it then.
