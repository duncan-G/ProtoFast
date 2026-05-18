# Angular client setup

Scaffolds and configures the Angular SSR project. All paths are relative
to the repo root. `«clientname»` is the lowercase client name,
`«ProjectName»` is the PascalCase root namespace.

---

## 2a. Generate the project

```bash
npx -y @angular/cli@latest new «clientname» \
  --directory ./clients/«clientname» \
  --style scss \
  --routing \
  --ssr \
  --skip-git \
  --package-manager npm \
  --ai-config cursor \
  --defaults
```

Per bootstrap-project principle 2: if any flag is rejected with
`Unknown argument: ...`, drop **that** flag and re-run.

## 2b. Install Tailwind CSS v4

From `clients/«clientname»/`:

```bash
npm install tailwindcss @tailwindcss/postcss
```

Create `clients/«clientname»/.postcssrc.json`:

```json
{
  "plugins": {
    "@tailwindcss/postcss": {}
  }
}
```

Replace the contents of `clients/«clientname»/src/styles.scss` with:

```scss
@import "tailwindcss";
```

Verify Tailwind is wired:

```bash
cd clients/«clientname» && npx ng build 2>&1 | tail -5
```

The build should succeed with no Tailwind-related errors. If the builder
doesn't pick up PostCSS, ensure `.postcssrc.json` is at
`clients/«clientname»/` (the project root), not inside `src/`.

## 2c. Verify SSR scaffolded

- `ls clients/«clientname»/angular.json`
- `ls clients/«clientname»/src/server.ts` (path may vary by version)
- `clients/«clientname»/package.json` `scripts` includes a
  `serve:ssr:*` entry.
- `angular.json` `architect.build.options` includes both `server` and
  `ssr` keys.

If any is missing, re-run the generator with `--ssr`.

## 2d. Patch the `start` and `build` scripts

Edit `clients/«clientname»/package.json` scripts:

```json
"generate:grpc": "npx buf generate",
"start": "npm run generate:grpc && ng serve --host 0.0.0.0 --port ${PORT:-4200} --allowed-hosts",
"build": "npm run generate:grpc && ng build",
```

- `generate:grpc` runs `buf generate` using the client's `buf.gen.yaml`.
- `--host 0.0.0.0` binds the dev server so containers can reach it.
- `--port ${PORT:-4200}` reads the listen port from Aspire's `PORT`
  env var; falls back to 4200 for manual runs outside the orchestrator.
- `--allowed-hosts` accepts connections from any hostname (needed when
  Aspire's assigned URL differs from `localhost`).
- Both `start` and `build` run codegen first so generated types are
  always current.

## 2e. Dev-server proxy config

Since all traffic flows through Envoy (the browser hits Envoy, Envoy
routes to backends by prefix), the Angular dev server does **not** need
a proxy config. No `proxy.conf.json` is needed — remove it if the
generator created one, and do not add `proxyConfig` to `angular.json`.

## 2f. Register the client as an Aspire resource

The client has two hosting modes gated on
`builder.ExecutionContext.IsPublishMode`:

| Mode | API | What runs |
|------|-----|-----------|
| Dev  | `AddJavaScriptApp` | `npm start` → Angular dev server |
| Publish | `AddDockerfile` | Container from `clients/«clientname»/Dockerfile` |

### Dev-mode package

`Aspire.Hosting.JavaScript` provides `AddJavaScriptApp`. If not already
added, install from inside `apphost/`:

```bash
cd apphost
aspire add javascript --non-interactive
```

### Publish-mode Dockerfile

Create `clients/«clientname»/Dockerfile`:

```dockerfile
FROM node:22-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM node:22-alpine
WORKDIR /app
COPY --from=build /app/dist/«clientname» ./dist/«clientname»
COPY --from=build /app/package*.json ./
RUN npm ci --omit=dev
CMD ["node", "dist/«clientname»/server/server.mjs"]
```

Adapt the `COPY --from=build` paths and `CMD` entry point to match the
actual `ng build` SSR output layout, which varies by Angular version.

### Registration in `apphost/Program.cs`

Add between the services and Envoy (or after the last client if one
already exists):

```csharp
EndpointReference «clientname»Endpoint;
if (builder.ExecutionContext.IsPublishMode)
{
    «clientname»Endpoint = builder.AddDockerfile("«clientname»", "../clients/«clientname»")
        .WithHttpEndpoint(env: "PORT")
        .WithExternalHttpEndpoints()
        .GetEndpoint("http");
}
else
{
    «clientname»Endpoint = builder.AddJavaScriptApp("«clientname»", "../clients/«clientname»", "start")
        .WithHttpEndpoint(env: "PORT")
        .GetEndpoint("http");
}
```

- **Dev:** `AddJavaScriptApp` runs `npm start`. Aspire auto-creates a
  `«clientname»-installer` resource that runs `npm install` first.
- **Prod:** `AddDockerfile` builds the Dockerfile. Aspire allocates the
  port and sets `PORT`.
- `«clientname»Endpoint` is passed to Envoy registration so the proxy
  can route traffic to this client.
- No port is specified — Aspire allocates one and passes it via `PORT`.
