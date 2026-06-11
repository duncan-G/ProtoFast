---
name: add-angular-client
description: >-
  Adds a new Angular SSR client to an existing Aspire-orchestrated project:
  scaffolds under clients/, installs Tailwind CSS v4, patches npm scripts
  for Aspire's PORT, 0.0.0.0 binding, and HTTPS dev-server flags, creates
  a publish-mode Dockerfile, registers the client in the AppHost with HTTPS
  endpoints and Aspire developer certificate injection (dev + publish
  modes), creates a SERVER_URL injection token with TransferState for
  resolving the backend URL in SSR and browser contexts, wires buf +
  Connect proto codegen for gRPC-Web, and updates the Envoy proxy if it
  already exists. Use when the user asks to add a new Angular client,
  frontend app, web UI, or SPA — either during initial bootstrap or after
  the project is already running.
disable-model-invocation: true
---

# Add Angular Client

Adds a single Angular SSR client to an Aspire-orchestrated project. Works
in two contexts:

- **During bootstrap** — called by `bootstrap-project`. Envoy does not
  exist yet; the orchestrator creates it later with the full route set.
- **Standalone** — called directly on an already-bootstrapped project.
  Envoy exists and this skill updates it.

## Placeholders

- `«ProjectName»` — PascalCase root namespace, detected from the AppHost
  `.csproj` filename (e.g. `Nimbus.AppHost.csproj` → `Nimbus`).
- `«clientname»` — lowercase folder, Aspire resource name, and Angular
  project name (e.g. `app`, `portal`).

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

### Detect the next available production port

Scan `apphost/Program.cs` for existing `AddClientApp` calls and extract
the `productionPort` arguments already in use. The port sequence starts
at **4000** and increments by 1. Pick the lowest port in the sequence
that is not already used (e.g. if 4000 is taken → 4001, if both →
4002, etc.).

### Ask the user

1. **Client name** — short, lowercase (e.g. `app`, `portal`).
2. **Production port** — the container port the client SSR server
   listens on in publish mode. Present the detected default (e.g.
   "4000" or "4001") and let the user override.
3. **Which services' protos to generate TS for** — defaults to all
   services whose API project contains a `Protos/` folder (i.e.
   `services/*/src/*/Protos/`).

## Step 2 — Scaffold and configure the Angular app

**Load `references/angular-setup.md`** and follow Steps 2a–2f.
Scaffolds the Angular SSR project into `clients/«clientname»/`, installs
Tailwind CSS v4, verifies SSR, patches npm scripts for Aspire's `PORT`
env var and `0.0.0.0` binding, creates the publish-mode Dockerfile, and
registers the client in `apphost/Program.cs` with dual dev/publish modes.

## Step 3 — Wire proto codegen (buf + Connect)

**Load `references/proto-codegen.md`** and follow Steps 3a–3e. Installs
buf + Connect dependencies, creates `buf.gen.yaml` selecting service
protos, runs initial generation, creates the gRPC transport provider,
and adds generated code to `.gitignore`.

## Step 4 — Update Envoy CORS (post-bootstrap only)

Check whether `proxy/envoy.yaml.tmpl` exists.

- **File absent** — skip this step. The caller (`bootstrap-project`)
  creates Envoy later with the full CORS config.
- **File present** — **load `references/update-envoy.md`** and follow
  it to add the new client's origin to the Envoy CORS allowed origins
  in `apphost/Program.cs`. The client is not an Envoy cluster — it is
  the browser app making requests *to* Envoy.

## Guardrails

- In **dev mode** Aspire assigns ports dynamically via the `PORT` env
  var — never hardcode. In **publish mode** each client has a fixed
  `productionPort` starting at 4000 (used as `targetPort`).
- The dev server runs HTTPS using Aspire's developer certificate.
  `AddClientApp` injects `SSL_CERT` and `SSL_KEY` env vars via
  `WithHttpsCertificateConfiguration`; the `start` script passes them
  to `ng serve --ssl --ssl-cert --ssl-key`.
- There is **no dev-server proxy config** (`proxy.conf.mjs`). The
  browser talks directly to Envoy's HTTPS endpoint. The `SERVER_URL`
  injection token (resolved via Angular `TransferState`) provides the
  Envoy URL to both the gRPC transport and browser telemetry.
- Each client under `clients/` is its own standalone Angular project,
  not a multi-project workspace.
- Each client has its own `buf.gen.yaml` selecting which service protos
  to codegen. This is where per-client selectivity lives.
- The `--host 0.0.0.0` and `--allowed-hosts` flags are required for
  containers (Envoy) to reach the dev server.
