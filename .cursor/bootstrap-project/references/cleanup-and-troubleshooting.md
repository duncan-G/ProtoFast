# Step 9 — Cleanup / reset

Use this when you're done with the smoke check, or any time the stack
misbehaves and you want a clean slate.

## 9a. Stop the running AppHost

```bash
aspire stop
aspire ps    # should report no running AppHost
```

## 9b. Clean orphan DCP containers

`aspire stop` does not always clean up the containers it spawned
(in particular DCP-managed `persistent=false` ones). Leftovers can
hold host ports and the next `aspire start` then collides.

```bash
docker ps --filter "label=com.microsoft.developer.usvc-dev.persistent=false" \
  --format '{{.Names}}'

docker stop $(docker ps \
  --filter "label=com.microsoft.developer.usvc-dev.persistent=false" \
  --format '{{.Names}}') 2>/dev/null || true
```

The `docker stop` is safe on an empty arg list. **Before mass-stopping,
scan the names** — the label selects DCP containers from *any* Aspire
AppHost on this machine, not just this project's. If anything looks
unfamiliar, confirm with the user.

## 9c. Confirm no orphan processes remain

Since all ports are Aspire-assigned (no fixed ports), there is no
static list to check. Instead, verify that the DCP container cleanup
in 9b left nothing running and that `aspire ps` reports no AppHost.

## 9d. Recover from a misconfigured AppHost

The project-based AppHost supports `dotnet build` for validation.
Always build before starting:

```bash
dotnet build apphost
```

If the build fails with a C# compilation error:

1. Read the error output from `dotnet build`.
2. Fix `apphost/Program.cs` (or the `.csproj` if it's a reference
   issue).
3. Re-run `dotnet build apphost` to confirm the fix.
4. Re-run Step 9a (stop), then Step 8 (restart).

If startup fails despite a clean build (e.g. runtime errors), read
the error verbatim from the Aspire log file printed in the
`aspire start --format Json` output.

---

# Appendix: Container ↔ host networking

The single largest source of unexpected symptoms. The fixes are
already wired into Steps 3–6. This appendix exists so unexpected
runtime symptoms (e.g. the user has modified the repo, or is running
on an unusual host) can be reverse-engineered back to causes.

## A. `host.docker.internal` resolution inside containers

- Rule: every container resource that reaches a host process by
  hostname needs the host-gateway alias.
- Aspire call: `.WithContainerRuntimeArgs("--add-host=host.docker.internal:host-gateway")`.
- The argument is safe everywhere — on Docker Desktop it duplicates
  an existing entry harmlessly; on Docker Engine it's load-bearing.

Likely symptom if missing: cluster targeting `host.docker.internal:<port>`
returns `503 no healthy upstream` because DNS resolution fails inside
the container.

## B. Loopback-only dev/backend servers

- Rule: every server a container needs to reach must bind on `0.0.0.0`,
  not the default `127.0.0.1`.
- .NET services: set `applicationUrl` to `http://0.0.0.0:0` in
  `launchSettings.json` (Step 3b). Port `0` lets Aspire assign
  dynamically.
- Angular dev server: `--host 0.0.0.0` (Step 4c).

Likely symptom if missing: connections from the container return
`Connection refused` (distinct from A's DNS failure).

## C. Dev-server `allowedHosts` enforcement

- Rule: a reverse-proxied dev server needs either a widened host-allow-list
  *or* a proxy that rewrites the `Host` header.
- Angular override: `ng serve --allowed-hosts` (Step 4c).
- Envoy alternative: `route: { cluster: <c>, host_rewrite_literal: "<allowed>" }`.

This skill uses the dev-server override (one-line `package.json`
change). Use the proxy alternative if upstream needs the original
`Host` header preserved.

Likely symptom if missing: HTTP 400 with body like
`Header "host" with value "<x>" is not allowed.`.

---

# Adding proto codegen to a new client

When `clients/app/` (or another client) is scaffolded, wire proto
codegen by repeating Step 5 with that client's directory:

1. Install the same npm dependencies (5a).
2. Create `buf.gen.yaml` selecting only the protos the new client
   needs (5b). This is where per-client selectivity lives.
3. Run `npx buf generate` (5c).
4. Create a `grpc-transport.ts` (5d) with the appropriate `baseUrl`
   for the Envoy route prefix the client uses.
5. Add `generate:grpc` to the client's npm scripts (4c).
