# Register a new Angular client with the unified SSR host

Envoy routing is handled entirely by `proxy.WithClient(...)` in
`apphost/Program.cs` (see `angular-setup.md` 2e) — the proxy's
entrypoint generates the client's listener (dev) or virtual host
(publish) from the `CLIENTS` env var. No Envoy template edits are
needed for a new client.

What still needs manual edits is the **unified SSR host**
(`clients/host/`), which serves every client's SSR bundle in publish
mode and in the `dev-host` smoke-test mode.

---

## 1. Add the client to `clients/host/server.mjs`

Add a loader entry to the dispatch map:

```javascript
const clientLoaders = {
  admin: () => import('./admin/dist/admin/server/server.mjs'),
  «clientname»: () => import('./«clientname»/dist/«clientname»/server/server.mjs'),
};
```

The key must match the Aspire client name (it is compared against the
`x-client` header that Envoy sets).

## 2. Add the client to `clients/host/Dockerfile`

Add a build-stage pair before the final stage (copy the existing
`admin-build` / `admin-deps` pair):

```dockerfile
# --- «clientname»: build ---
FROM node:22-alpine AS «clientname»-build
WORKDIR /repo/clients/«clientname»
COPY clients/«clientname»/package*.json ./
RUN npm ci
COPY services /repo/services
COPY clients/«clientname»/ ./
RUN npm run build

# --- «clientname»: prod deps ---
FROM node:22-alpine AS «clientname»-deps
WORKDIR /deps
COPY clients/«clientname»/package*.json ./
RUN npm ci --omit=dev
```

And the COPY lines in the final stage:

```dockerfile
COPY --from=«clientname»-build /repo/clients/«clientname»/dist ./«clientname»/dist
COPY --from=«clientname»-deps /deps/node_modules ./«clientname»/node_modules
```

Notes:

- The build context is the **repo root** (`AddClientHost` passes
  `".."`), so the build stage can copy `services/` for buf proto
  codegen during `npm run build`.
- Each client gets its own `node_modules` next to its `dist/` so the
  SSR bundle's ESM imports resolve against its own dependency tree.

## 3. Rebuild and verify

```bash
dotnet build apphost
```

If the project was already running, restart with `aspire stop` then
`aspire start` (or `aspire run`). The new client's URL appears on the
envoy resource as the `«clientname»-web` endpoint.

To smoke-test the unified host locally, run the AppHost with the
`https-ssr-host` launch profile (sets `SsrHost__Dev=true`) and browse
the same per-client Envoy URLs.
