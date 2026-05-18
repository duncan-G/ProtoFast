---
name: add-dotnet-service
description: >-
  Adds a new .NET gRPC service to an existing Aspire-orchestrated project:
  scaffolds under services/, binds on 0.0.0.0, registers in the AppHost,
  and updates the Envoy proxy if it already exists. Use when the user asks
  to add a new backend service, gRPC service, API service, or .NET service
  to the project — either during initial bootstrap or after the project is
  already running.
disable-model-invocation: true
---

# Add .NET gRPC Service

Adds a single .NET gRPC service to an Aspire-orchestrated project. Works
in two contexts:

- **During bootstrap** — called by `bootstrap-project`. Envoy does not
  exist yet; the orchestrator creates it later with the full route set.
- **Standalone** — called directly on an already-bootstrapped project.
  Envoy exists and this skill updates it.

## Placeholders

- `«ProjectName»` — PascalCase root namespace, detected from the AppHost
  `.csproj` filename (e.g. `Nimbus.AppHost.csproj` → `Nimbus`).
- `«ServiceName»` — PascalCase name for this service (e.g. `Billing`).
- `«servicename»` — lowercase folder and Aspire resource name (e.g.
  `billing`).
- `«SERVICENAME»` — UPPERCASE form for Envoy env var prefixes (e.g.
  `BILLING`).

## Prerequisites

- `apphost/` exists with a working `.csproj` and `Program.cs`.
- `services/` directory exists.
- `dotnet` CLI is available.

## Step 1 — Gather inputs

Ask the user for:

1. **Service name** — short, lowercase (e.g. `billing`, `notifications`).
   Derive PascalCase and UPPERCASE forms automatically.
2. **Envoy route prefix** — defaults to `/«servicename»/`.

Detect `«ProjectName»` from the AppHost `.csproj`:

```bash
ls apphost/*.csproj
```

The name before `.AppHost.csproj` is the project name.

## Step 2 — Scaffold the service

```bash
dotnet new grpc -n «ProjectName».«ServiceName» -o services/«servicename»
dotnet build services/«servicename»
```

Per bootstrap-project principle 2: if a flag is rejected, drop it and
re-run.

## Step 3 — Set RootNamespace

Ensure the `.csproj` has an explicit `<RootNamespace>` of
`«ProjectName».«ServiceName»` inside the first `<PropertyGroup>`:

```xml
<RootNamespace>«ProjectName».«ServiceName»</RootNamespace>
```

This guarantees the generated namespace is `«ProjectName».«ServiceName»`
regardless of folder structure or project name conventions. If the
element already exists with the correct value, leave it as-is.

## Step 4 — Bind on `0.0.0.0`

Update `services/«servicename»/Properties/launchSettings.json`: set the
`http` profile's `applicationUrl` to `http://0.0.0.0:0`. Port `0` lets
Aspire assign dynamically. Do **not** hardcode a port.

## Step 5 — Add ProjectReference to AppHost

Add to `apphost/«ProjectName».AppHost.csproj` inside the `<ItemGroup>`
containing other `<ProjectReference>` entries:

```xml
<ProjectReference Include="../services/«servicename»/«ProjectName».«ServiceName».csproj" />
```

## Step 6 — Register in `apphost/Program.cs`

Add alongside existing service registrations:

```csharp
var «servicename» = builder.AddProject<Projects.«ProjectName»_«ServiceName»>("«servicename»");
```

Dots in the project name become underscores in the source-generated type.

Build to verify:

```bash
dotnet build apphost
```

## Step 7 — Update Envoy (post-bootstrap only)

Check whether `proxy/envoy.yaml.tmpl` exists.

- **File absent** — skip this step. The caller (`bootstrap-project`)
  creates Envoy later with the full route set.
- **File present** — **load `references/update-envoy.md`** and follow
  it to add the service's route, cluster, entrypoint validation, and
  AppHost Envoy wiring.

## Guardrails

- Never hardcode ports — Aspire assigns them dynamically.
- Service folder is lowercase (`services/billing/`); `.csproj` and
  assembly use PascalCase with the root namespace
  (`«ProjectName».Billing`).
- Resource names in `Program.cs` are short lowercase strings matching
  the folder name.
