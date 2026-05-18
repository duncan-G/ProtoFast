---
name: add-angular-client
description: >-
  Adds a new Angular SSR client to an existing Aspire-orchestrated project:
  scaffolds under clients/, installs Tailwind CSS v4, patches npm scripts
  for Aspire's PORT and 0.0.0.0 binding, creates a publish-mode Dockerfile,
  registers the client in the AppHost (dev + publish modes), wires buf +
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

Ask the user for:

1. **Client name** — short, lowercase (e.g. `app`, `portal`).
2. **Which services' protos to generate TS for** — defaults to all
   directories under `services/` that contain a `Protos/` folder.

Detect `«ProjectName»` from the AppHost `.csproj`:

```bash
ls apphost/*.csproj
```

The name before `.AppHost.csproj` is the project name.

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

- Never hardcode ports — Aspire assigns them via the `PORT` env var.
- Each client under `clients/` is its own standalone Angular project,
  not a multi-project workspace.
- Each client has its own `buf.gen.yaml` selecting which service protos
  to codegen. This is where per-client selectivity lives.
- The `--host 0.0.0.0` and `--allowed-hosts` flags are required for
  containers (Envoy) to reach the dev server.
