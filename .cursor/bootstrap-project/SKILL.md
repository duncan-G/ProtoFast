---
name: bootstrap-project
description: >-
  Bootstraps a new project repo from an empty state: asks the user for a
  project name, pre-flights the agent's environment, agrees on a folder
  layout, scaffolds the Aspire C# AppHost (project-based, with .csproj), scaffolds
  the .NET gRPC services under `services/`, scaffolds the Angular SSR
  `admin` client under `clients/` with Tailwind CSS v4 for styling,
  wires buf + Connect proto-to-TypeScript codegen, adds an Envoy front
  proxy with static config, runs a pass/fail smoke check that proves
  end-to-end gRPC-Web connectivity from the Angular client through Envoy
  to the backend services, and documents a clean stop. Use when the user
  asks to initialize, bootstrap, scaffold, or set up a project, or when
  the repo only contains README/scripts/.cursor and no app code yet.
disable-model-invocation: true
---

# Bootstrap Project

End-to-end project initialization workflow. Run from the **repo root**
(the skill makes no assumption about absolute paths). Starting state:
only `README.md`, `.gitignore`, `.editorconfig`, `.gitattributes`,
`scripts/`, and `.cursor/`.

## Project name

Before doing anything else, **ask the user for the project name**.
This name is used as the root namespace for .NET projects (e.g.
`«ProjectName».Auth`), in Envoy virtual-host labels, and in the
README. It should be PascalCase and short (e.g. `ProtoJet`,
`SkyBridge`, `Nimbus`).

Throughout the rest of this skill, `«ProjectName»` is a placeholder.
Substitute the user's chosen name everywhere it appears — in shell
commands, file contents, folder-layout descriptions, and reference
files.

When followed end to end, this workflow produces a working stack: the
Aspire dashboard shows every resource Healthy, the Envoy proxy routes
all traffic, and the Angular admin client can call the gRPC Greeter
service end-to-end via gRPC-Web. There is no "MVP path with
known-broken routes" — the prescription is the working configuration.
If a step produces an unexpected symptom, consult
`references/cleanup-and-troubleshooting.md` which maps symptoms to
causes.

## Operating principles

Follow every rule below. Each is a load-bearing assumption for later
steps; deviations should be deliberate and discussed with the user.

1. **Generator output is the contract.** After every generator
   (`aspire new`, `dotnet new`, `ng new`, …), list the produced files
   and adapt. The skill prescribes one expected shape per generator;
   if the output differs, stop and ask before continuing.

2. **Specify intent, drop unknown flags.** If a generator rejects a
   flag with `Unknown argument: ...`, drop the flag and re-run. Do
   not pin to a deprecated generator version just to keep the flag.

3. **The agent's shell is not the user's shell.** Version managers
   (nvm, fnm, n, volta, asdf, pyenv, sdkman, rbenv, …) load via
   interactive-shell init scripts. The agent runs non-interactively
   and **must source them explicitly** in every command that needs
   them — including the command that launches Aspire, since
   orchestrator child processes inherit the env at launch time.

4. **Aspire assigns all ports (except its own dashboard).** No
   service, client, or proxy port is hardcoded. Aspire selects every
   port at startup and passes it through endpoint references. Envoy
   routes `/ → admin`; all cluster endpoints (hosts and ports) are
   injected as environment variables via `WithClusterEndpoint` and
   substituted by `entrypoint.sh` at container startup (see
   `references/envoy-proxy.md` 6d, 6f–6g). Do not add fixed port
   numbers to `launchSettings.json` or Envoy config; rely on
   Aspire's dynamic assignment throughout.

5. **`«ProjectName»` is a placeholder, not a literal.** Every
   occurrence of `«ProjectName»` in commands, file contents, and
   comments must be replaced with the project name the user chose
   at the start. The corresponding lowercase form (e.g.
   `«projectname»`) is used for Envoy virtual-host names and similar
   identifiers that conventionally use lowercase.

6. **Container ↔ host networking requires explicit handling.** Every
   container resource that reaches a host process follows Appendix A
   (host-gateway hostname) and Appendix B (bind backends on
   `0.0.0.0`). The Steps below already apply these — don't strip the
   calls.

7. **Every smoke-check expectation has a registration step.** When
   Step 8 lists N resources, Steps 2–6 contain N corresponding
   `.Add...` calls. Cross-reference by name.

8. **Pre-flight and cleanup are workflow steps, not optional.** Step 0
   and Step 9 are first-class. The orchestrator's stop command does
   not always clean up the containers it spawned; pre-flight surfaces
   any holdovers before they collide.

9. **Smoke check is pass/fail.** Step 8 checks return pass or fail.
   On the first failure, stop and report — don't silently work around.
   There is no "documented caveat" middle category in this skill.

See **Skill maintenance** at the bottom for the rules the skill
author follows when updating this file.

## Available MCP servers

The repo's `.cursor/mcp.json` exposes:

- `aspire` — `aspire agent mcp`.
- `angular-cli` — `npx -y @angular/cli mcp`.
- `aws-mcp` — not needed for bootstrap.

Prefer MCP tools when the server's descriptors are populated for this
workspace; otherwise use the underlying CLI. Both achieve the same
effect. Either way, list a server's tools before calling them — don't
guess argument shapes.

## Workflow checklist

```
Bootstrap Progress:
- [ ] Step 0: Pre-flight
- [ ] Step 1: Confirm folder layout with the user
- [ ] Step 2: Scaffold Aspire AppHost
- [ ] Step 3: Add .NET gRPC services (via add-dotnet-service)
- [ ] Step 4–5: Add Angular `admin` client + proto codegen (via add-angular-client)
- [ ] Step 6: Add Envoy proxy
- [ ] Step 7: Update README
- [ ] Step 8: Smoke check
- [ ] Step 9: Cleanup or graceful stop
```

---

### Step 0 — Pre-flight

**Load `references/preflight.md`** and run every check (0a–0g) before
any generator. Confirms: repo structure, Node + npm via version manager, Aspire CLI,
container runtime, no orphan AppHosts/containers, ports free, env
prepared for Aspire child processes.

---

### Step 1 — Folder layout

Propose this layout and **get explicit user confirmation** before any
generator. If the user pushes back, adjust *first*, then rerun the
propose-confirm loop.

```
<repo root>/
├── apphost/              # Aspire AppHost (project-based)
│   ├── «ProjectName».AppHost.csproj
│   ├── Program.cs
│   └── aspire.config.json
├── services/             # Backend .NET gRPC services
│   ├── auth/             # «ProjectName».Auth.csproj
│   ├── payments/         # «ProjectName».Payments.csproj
│   └── api/              # «ProjectName».Api.csproj
├── clients/              # Angular SSR apps, one per client
│   ├── admin/            # Admin client (scaffolded during bootstrap)
│   │   ├── .postcssrc.json # PostCSS config (loads Tailwind v4 plugin)
│   │   ├── buf.gen.yaml  # Proto codegen config (selects which protos)
│   │   └── src/lib/gen/  # Generated TS from protos (gitignored)
│   └── app/              # End-user client (placeholder; not scaffolded)
├── proxy/                # Envoy config (Dockerfile + templates)
│   ├── Dockerfile
│   ├── envoy.yaml.tmpl   # Config template with __PLACEHOLDER__ tokens
│   ├── entrypoint.sh     # Validates env vars, substitutes, launches Envoy
│   ├── cors-allow-origins-exact.tmpl
│   └── cors-allow-origins-with-subdomain.tmpl
├── scripts/              # Existing dev-setup scripts
├── .cursor/              # MCP + skills (existing)
├── .gitignore
└── README.md
```

Rules:

- All generators write **into the dirs above**, never into the repo
  root. Pass each generator's output flag.
- `clients/app/` stays a placeholder (`clients/app/.gitkeep` only if
  git needs the directory tracked).
- .NET service folders are short and lowercase
  (`services/auth/`) but `.csproj` / assembly names are
  root-namespaced as `«ProjectName».<Name>`.
- `apphost/` is lowercase as the folder name; the `.csproj` inside is
  root-namespaced as `«ProjectName».AppHost`.
- Each client has its own `buf.gen.yaml` that selects which service
  protos to generate TypeScript clients for.

---

### Step 2 — Scaffold the Aspire AppHost

Use the **C#** AppHost (`aspire-empty`) with the `--project-based`
flag to get a `.csproj` + `Program.cs` layout.

```bash
aspire new aspire-empty \
  --name «ProjectName».AppHost \
  --output ./apphost \
  --language csharp \
  --project-based \
  --non-interactive
```

Per principle 2, if `--project-based` is rejected, drop it and check
the output: some CLI versions default to project-based, others to
file-based. If the output is file-based (`apphost.cs` with `#:sdk`
directive, no `.csproj`), delete it and scaffold manually (see below).

Verify the produced shape:

- `apphost/«ProjectName».AppHost.csproj` exists and contains
  `<Sdk Name="Aspire.AppHost.Sdk" .../>`.
- `apphost/Program.cs` exists with `DistributedApplication.CreateBuilder`.
- `apphost/aspire.config.json` exists and `appHost.path` points at
  the `.csproj`.
- **No `#:sdk` directive** in any `.cs` file (that's the file-based
  shape — wrong).

If the CLI only produced a file-based AppHost, create the project
manually:

```bash
dotnet new console -n «ProjectName».AppHost -o apphost --use-program-main false
```

Then add the Aspire AppHost SDK to `apphost/«ProjectName».AppHost.csproj`.
Detect the correct values first:

```bash
# Target framework from the installed SDK
dotnet --version | cut -d. -f1,2   # e.g. "9.0" → net9.0

# Latest Aspire AppHost SDK version
dotnet package search Aspire.AppHost.Sdk --exact-match --take 1 --format json
```

Use the detected values in the `.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Sdk Name="Aspire.AppHost.Sdk" Version="«AspireSdkVersion»" />
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net«MajorMinor»</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

And write the initial `apphost/Program.cs`:

```csharp
var builder = DistributedApplication.CreateBuilder(args);
builder.Build().Run();
```

Run `dotnet build apphost` to confirm it compiles.

The CLI may also create `apphost/.agents/` and `apphost/.vscode/`.
Leave them as-is.

---

### Step 3 — Add .NET gRPC services

**Read and follow `.cursor/add-dotnet-service/SKILL.md`** for each of
the three bootstrap services: `auth`, `payments`, `api`.

For each service, supply these inputs:

| Service name | PascalCase | Envoy prefix |
|---|---|---|
| `auth` | `Auth` | `/auth/` |
| `payments` | `Payments` | `/payments/` |
| `api` | `Api` | `/api/` |

Since `proxy/` does not exist yet at this point, the sub-skill will
skip Envoy updates — Step 6 creates the full Envoy config from scratch
for all services at once.

After all three services are added, verify the AppHost builds:

```bash
dotnet build apphost
```

---

### Step 4–5 — Add Angular `admin` client + proto codegen

**Read and follow `.cursor/add-angular-client/SKILL.md`** for the
`admin` client.

Supply these inputs:

- **Client name:** `admin`
- **Protos:** all services (`auth`, `payments`, `api`)
- **Envoy route prefix:** `/` (catch-all — the primary client)

The sub-skill scaffolds the Angular SSR project, installs Tailwind
CSS v4, patches npm scripts for Aspire's `PORT` and `0.0.0.0`
binding, creates the publish-mode Dockerfile, registers the client in
`apphost/Program.cs` with dual dev/publish modes, and wires buf +
Connect proto codegen for all three services.

**During bootstrap only:** also follow the Greeter example in
`add-angular-client/references/proto-codegen.md` (the "Example:
wiring a Greeter component" section) to prove end-to-end gRPC-Web
connectivity in the smoke check.

Since `proxy/` does not exist yet, the sub-skill will skip Envoy
updates — Step 6 creates the full Envoy config.

---

### Step 6 — Envoy proxy

**Load `references/envoy-proxy.md`** and follow Steps 6a–6h. Creates
the `proxy/` directory with a Dockerfile, an `envoy.yaml.tmpl` config
template using `__PLACEHOLDER__` tokens, an `entrypoint.sh` that
validates environment variables and substitutes tokens at container
startup, and CORS fragment templates. Adds
`apphost/EnvoyProxy/EnvoyProxyResourceBuilderExtensions.cs` with
extension methods (`AddEnvoyProxy`, `WithCorsOriginExact`,
`WithClusterEndpoint`, `WithAllowedHosts`) that inject cluster
endpoints, CORS origins, and allowed hosts as environment variables.
Registers Envoy in `apphost/Program.cs` via `AddDockerfile`. The
reference includes the full `Program.cs` at this point.

---

### Step 7 — Update README

Append a "Running the app" section to `README.md`. Do not remove or
reorder existing content.

````markdown
## Running the app

The whole stack (Aspire AppHost + .NET gRPC services + Angular admin client +
Envoy proxy) is started via the Aspire CLI from the repo root:

```bash
aspire run
```

This launches:

- Envoy front proxy (proxies all traffic; ports assigned by Aspire)
- Angular `admin` client with SSR (proxied via Envoy at `/`)
- .NET gRPC services from `services/` (proxied via Envoy):
  - `auth`     at `/auth/*`
  - `payments` at `/payments/*`
  - `api`      at `/api/*`
- The Aspire dashboard (URL printed in the terminal on startup)

All resource URLs (including Envoy) are shown in the Aspire dashboard.

`clients/app/` is reserved for an end-user Angular client and is not yet
scaffolded.

Stop everything with `Ctrl+C`, or from another shell:

```bash
aspire stop
```
````

Optionally include a `## Layout` block (the diagram from Step 1).

---

### Step 8 — Smoke check

**Launch.** Use `aspire start` (background) not `aspire run`
(foreground-blocking). Re-source the version manager in the launching
shell (principle 3). From the repo root:

```bash
if [ -s "$HOME/.nvm/nvm.sh" ]; then
  export NVM_DIR="$HOME/.nvm" && \. "$NVM_DIR/nvm.sh"
fi
aspire start --non-interactive --format Json
```

Capture the dashboard URL from the JSON output. Wait ~20–30 s for
`npm install`, dotnet builds, and the Envoy image pull.

**Inventory.** Run `aspire describe --format Json` and verify:

| Resource | Type | Expected state | Notes |
|---|---|---|---|
| `admin-installer` | Executable | Finished, exit 0 | Auto-`npm install` for the JS app |
| `admin` | Executable | Running, Healthy | `npm start` = proto codegen + `ng serve` (port is Aspire-assigned) |
| `auth` | Project | Running, Healthy | .NET gRPC service (port is Aspire-assigned) |
| `payments` | Project | Running, Healthy | .NET gRPC service (port is Aspire-assigned) |
| `api` | Project | Running, Healthy | .NET gRPC service (port is Aspire-assigned) |
| `envoy` | Container | Running, Healthy | front proxy (ports are Aspire-assigned) |

Six rows corresponding to six `.Add...` calls in `Program.cs`
(principle 6).

**Curl checks.** Obtain the Envoy HTTP and admin URLs from
`aspire describe --format Json` (they are Aspire-assigned). All pass
on a clean run:

| # | Check | Command | Pass criterion |
|---|---|---|---|
| 1 | Envoy admin UI | `curl -sI <envoy-admin-url>/` | 200 |
| 2 | Envoy to admin | `curl -sI <envoy-http-url>/` | 200, `text/html` |
| 3 | SSR rendered | `curl -s <envoy-http-url>/ \| head -c 1200` | Rendered Angular markup |
| 4 | Envoy to `/auth/` | `curl -si -X POST -H 'content-type: application/grpc-web' <envoy-http-url>/auth/` | non-5xx with `grpc-status` header |
| 5 | Envoy to `/payments/` | same with `/payments/` | as #4 |
| 6 | Envoy to `/api/` | same with `/api/` | as #4 |

If any check fails, stop and consult
`references/cleanup-and-troubleshooting.md` — the Appendix maps
symptoms back to the rule (A–C) the failure violates. Don't silently
work around.

---

### Step 9 — Cleanup / reset

**Load `references/cleanup-and-troubleshooting.md`** and follow
Steps 9a–9d. Stops the AppHost, cleans orphan DCP containers,
confirms ports are free, and documents recovery from AppHost
compilation errors.

---

## Skill maintenance

Rules the skill *author* (not the agent running it) follows when
editing this file:

1. **Filesystem-path agnosticism.** No absolute paths
   (`/home/<user>/...`, `/Users/<user>/...`, `C:\Users\...\`) in the
   skill or its prescribed outputs. Use repo-relative paths or `$HOME`
   expansion.
2. **External-repo independence.** Patterns are described **inline**
   in enough detail to implement without consulting any
   other repository. Links to external code are aids, never
   prerequisites.
3. **Prescribe, don't narrate.** Action steps state what to do, with
   the minimum context to do it right. Symptoms and failure modes
   live in the Appendix, which the agent reads only when something
   unexpected happens.
4. **Ship a working configuration.** If a step has a "simpler MVP"
   that produces known-broken behavior plus a "real fix" later, the
   skill teaches only the real fix. There is no "documented caveat"
   middle category in Step 8.
5. **Update before the next step.** When a generator output, flag, or
   API drifts, update this file as part of the bootstrap, before
   moving on. The skill is the next agent's contract.
6. **Keep SKILL.md under 500 lines.** Detailed implementation content
   belongs in `references/` files, loaded on demand. SKILL.md carries
   the workflow structure, principles, and critical inline steps.

---

## Guardrails

- Never run destructive commands (`rm -rf`, `git reset --hard`, force
  pushes).
- Never commit on the user's behalf during bootstrap unless asked.
- Never invent MCP tool argument names — list the server's tools
  first; if not populated, use the CLI.
- Keep generator output inside the agreed folders (Step 1). If a tool
  insists on a different layout, stop and re-confirm with the user.
- Never touch another project's resources without explicit user
  confirmation. Step 0 surfaces them; Step 9 cleans them only after
  asking.
- The Aspire CLI typically lives at `$HOME/.aspire/bin/aspire`. If
  `aspire` is not on `PATH`, prepend that directory or invoke the
  absolute path; do not reinstall it.
