---
name: add-angular-client
description: >-
  Adds a new Angular SSR client to an existing Aspire-orchestrated project:
  scaffolds under clients/, installs Tailwind CSS v4, patches npm scripts
  for Aspire's PORT, 0.0.0.0 binding, and HTTPS dev-server flags, registers
  the client in the AppHost (dev server + per-client Envoy listener),
  registers the client with the unified SSR host (clients/host) that serves
  every client from one Node process in publish mode, creates a SERVER_URL
  injection token with TransferState for resolving the backend URL in SSR
  and browser contexts, and wires buf + Connect proto codegen for gRPC-Web.
  Use when the user asks to add a new Angular client, frontend app, web UI,
  or SPA — either during initial bootstrap or after the project is already
  running.
disable-model-invocation: true
---

# Add Angular Client

Adds a single Angular SSR client to an Aspire-orchestrated project. Works
in two contexts:

- **During bootstrap** — called by `bootstrap-project`. Envoy does not
  exist yet; the orchestrator creates it later with the full route set.
- **Standalone** — called directly on an already-bootstrapped project.
  Envoy exists and this skill registers the client with it.

## Architecture

- **Dev:** each client runs its own `ng serve` (HMR), but the browser
  enters through a **per-client Envoy listener** — pages and API share
  one origin per client. `proxy.WithClient(builder, "«clientname»")`
  creates the listener and returns its endpoint, which becomes the
  client's `SERVER_URL`.
- **Publish:** a single **unified SSR host** container
  (`clients/host/`) serves every client's SSR bundle from one Node
  process. Envoy matches the client's subdomain and tags requests with
  an `x-client` header; the host dispatches to the matching bundle.
- **Dev smoke test:** running the AppHost with `SsrHost__Dev=true`
  (launch profile `https-ssr-host`) runs the unified host locally
  instead of the per-client dev servers, behind the same Envoy
  listener URLs.

## Placeholders

- `«ProjectName»` — PascalCase root namespace, detected from the AppHost
  `.csproj` filename (e.g. `Nimbus.AppHost.csproj` → `Nimbus`).
- `«clientname»` — lowercase folder, Aspire resource name, and Angular
  project name (e.g. `app`, `portal`).
- `«CLIENTNAME»` — uppercase env-var form (`-` becomes `_`).

## Prerequisites

- `apphost/` exists with a working `.csproj` and `Program.cs`.
- `clients/` directory exists.
- `node`, `npm`, and `dotnet` are available.
- At least one .NET gRPC service exists under `services/` (for proto
  codegen to have inputs).

## Step 1 — Gather inputs

Detect `«ProjectName»` from the AppHost `.csproj`:

```bash
ls apphost/*.csproj
```

The name before `.AppHost.csproj` is the project name.

### Ask the user

1. **Client name** — short, lowercase (e.g. `app`, `portal`).
2. **Which services' protos to generate TS for** — defaults to all
   services whose API project contains a `Protos/` folder (i.e.
   `services/*/src/*/Protos/`).
3. **Production domain** — the subdomain this client will answer on in
   publish mode (e.g. `portal.example.com`). Defaults to
   `«clientname».example.com`; it is wired as the Aspire parameter
   `«clientname»-domain` and can be changed at deploy time.
4. **Should this client become the default?** — the default client
   answers for unmatched hosts and missing `x-client` headers. Usually
   "no" (keep the existing default).

## Step 2 — Scaffold and configure the Angular app

**Load `references/angular-setup.md`** and follow Steps 2a–2f.
Scaffolds the Angular SSR project into `clients/«clientname»/`, installs
Tailwind CSS v4, verifies SSR, patches npm scripts for Aspire's `PORT`
env var and `0.0.0.0` binding, guards OTel instrumentation against
double-init in the unified host, and registers the client in
`apphost/Program.cs` (per-client Envoy listener + dev server).

## Step 3 — Wire proto codegen (buf + Connect)

**Load `references/proto-codegen.md`** and follow Steps 3a–3e. Installs
buf + Connect dependencies, creates `buf.gen.yaml` selecting service
protos, runs initial generation, creates the gRPC transport provider,
and adds generated code to `.gitignore`.

## Step 4 — Register with the unified SSR host

**Load `references/update-envoy.md`** and follow it. The unified host is
generic — it bakes in no client assets and has no per-client loader map,
so you do **not** edit `clients/host/server.mjs` or
`clients/host/Dockerfile`. Instead you: (1) add a
`.github/workflows/deploy-client-«clientname».yml` workflow (each client
builds to S3 and deploys independently), and (2) register `«clientname»`
in the `CLIENTS` env var (the instance seed in `infra/`; the first deploy
also self-registers it on a running box). The host discovers clients from
`CLIENTS` and pulls each pinned build from S3 at start
(docs/independent-deployment-plan.md §7).

If `clients/host/` does not exist yet (mid-bootstrap), skip this step —
the orchestrator creates the unified host with the full client set.

## Step 5 — Wire OpenTelemetry into the client

If the project has OpenTelemetry set up (`apphost/OpenTelemetryCollector/`
exists and `AddOpenTelemetryCollector` appears in `apphost/Program.cs`),
**load `../add-opentelemetry/references/client-otel.md`** and follow it
for the new client. It installs the OTel npm packages, creates browser
telemetry (`src/lib/telemetry.browser.ts`), the ConnectRPC trace
interceptor (`src/lib/grpc-trace.interceptor.ts`), and Node SSR
instrumentation (`src/instrumentation.ts`), and wires them into
`src/main.ts`, `src/server.ts`, and `src/app/grpc-transport.ts`.

When registering the client in `apphost/Program.cs`, pass the
collector's HTTP endpoint for both OTel parameters:

```csharp
var «clientname»Dev = builder.AddClientApp(
    "«clientname»", "../clients/«clientname»", «clientname»Web, otelHttp, otelHttp);
```

If another client under `clients/` is already instrumented, mirror its
telemetry files (and OTel package versions) instead of the reference
verbatim — the existing client reflects the project's current
conventions and any fixes applied since the reference was written.

If the project does not have OTel yet, skip this step — the
`add-opentelemetry` skill wires every client when it runs.

## Guardrails

- In **dev mode** Aspire assigns the dev-server port dynamically via
  the `PORT` env var — never hardcode it. Per-client Envoy listener
  ports are fixed internal targets (20000, 20001, …) assigned in
  registration order by `WithClient`; do not pick them manually.
- The dev server runs HTTPS using Aspire's developer certificate.
  `AddClientApp` injects `SSL_CERT` and `SSL_KEY` env vars via
  `WithHttpsCertificateConfiguration`; the `start` script passes them
  to `ng serve --ssl --ssl-cert --ssl-key`.
- There is **no dev-server proxy config** (`proxy.conf.mjs`) and no
  per-client Dockerfile. The browser enters through the client's Envoy
  listener; the `SERVER_URL` injection token (resolved via Angular
  `TransferState`) carries that listener's URL to the gRPC transport
  and browser telemetry. In publish mode `SERVER_URL` is unset and the
  browser falls back to `window.location.origin` (same-origin).
- Each client under `clients/` is its own standalone Angular project,
  not a multi-project workspace. Only the built SSR bundles are
  combined — by the unified host at image build time.
- Each client has its own `buf.gen.yaml` selecting which service protos
  to codegen. This is where per-client selectivity lives.
- The `--host 0.0.0.0` and `--allowed-hosts` flags are required for
  containers (Envoy) to reach the dev server.
- The client's `src/instrumentation.ts` must keep the
  `globalThis.__nodeOtelSdkStarted` guard — in the unified host every
  bundle shares one process and only the first may start the Node SDK.
