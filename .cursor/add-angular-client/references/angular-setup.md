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

## 2c. Patch the `start` and `build` scripts

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

## 2d. Dev-server proxy config

In dev mode the Angular dev server proxies backend routes to Envoy so
that the browser's gRPC-Web requests reach the right backend. The
`AddClientApp` extension injects `SERVER_URL` pointing at Envoy's HTTP
endpoint.

Create `clients/«clientname»/proxy.conf.mjs`:

```js
const serverUrl = process.env.SERVER_URL || 'http://localhost:8080';

export default {
  '/auth': { target: serverUrl, secure: false, changeOrigin: true },
  '/payments': { target: serverUrl, secure: false, changeOrigin: true },
  '/api': { target: serverUrl, secure: false, changeOrigin: true },
};
```

Add one entry per service route prefix. The fallback
`http://localhost:8080` is only for manual runs outside the orchestrator.

Wire it into `angular.json` under
`projects.«clientname».architect.serve.options`:

```json
"proxyConfig": "proxy.conf.mjs"
```

If the generator created a `proxy.conf.json`, replace it with the `.mjs`
file above.

## 2e. Register the client as an Aspire resource

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

### `AddClientApp` extension method

All Angular clients are registered through a single reusable extension
method that encapsulates the dev/publish branching. If it does not
already exist, create `apphost/ClientApp/ClientAppResourceBuilderExtensions.cs`:

```csharp
namespace «ProjectName».AppHost.ClientApp;

public static class ClientAppResourceBuilderExtensions
{
    /// <summary>
    /// Fillout summary and params
    public static EndpointReference AddClientApp(
        this IDistributedApplicationBuilder builder,
        string clientName,
        string clientPath,
        int productionPort,
        EndpointReference serverEndpoint,
        EndpointReference? clientOtelEndpoint = null,
        EndpointReference? clientServerOtelEndpoint = null)
    {
        if (builder.ExecutionContext.IsPublishMode)
        {
            var clientApp = builder.AddDockerfile(clientName, clientPath)
                .WithHttpEndpoint(targetPort: productionPort, env: "PORT")
                .WithExternalHttpEndpoints();

            var clientEndpoint = clientApp.GetEndpoint("http", KnownNetworkIdentifiers.PublicInternet);
            clientApp
                .WithEnvironment("NG_ALLOWED_HOSTS", clientEndpoint.Property(EndpointProperty.Host))
                .WithEnvironment("SERVER_URL", serverEndpoint)
                .WithOtelEndpoints(clientOtelEndpoint, clientServerOtelEndpoint);

            return clientEndpoint;
        }

        var clientAppDev = builder.AddJavaScriptApp(clientName, clientPath, runScriptName: "start")
            .WithHttpEndpoint(env: "PORT")
            .WithEnvironment("SERVER_URL", serverEndpoint);
            
        clientAppDev.WithOtelEndpoints(clientOtelEndpoint, clientServerOtelEndpoint);

        return clientAppDev.GetEndpoint("http");
    }

    private static IResourceBuilder<IResourceWithEnvironment> WithOtelEndpoints(
        this IResourceBuilder<IResourceWithEnvironment> clientApp,
        EndpointReference? clientOtelEndpoint,
        EndpointReference? clientServerOtelEndpoint)
    {
        if (clientOtelEndpoint is not null)
        {
            clientApp = clientApp.WithEnvironment("BROWSER_OTEL_ENDPOINT", clientOtelEndpoint);
        }

        if (clientServerOtelEndpoint is not null)
        {
            clientApp = clientApp.WithEnvironment("SERVER_OTEL_ENDPOINT", clientServerOtelEndpoint);
        }

        return clientApp;
    }
}
```

Parameters:

- `clientName` / `clientPath` — Aspire resource name and relative path
  to the Angular project (e.g. `"admin"`, `"../clients/admin"`).
- `productionPort` — the container port the client SSR server listens
  on in publish mode, passed as `targetPort` to `WithHttpEndpoint`.
  Ports start at 4000 and increment per client (4000, 4001, …). The
  skill auto-detects the next available port by scanning existing
  `AddClientApp` calls in `Program.cs`.
- `serverEndpoint` — the endpoint the client's SSR server should call
  (typically Envoy's HTTP endpoint).
- `clientOtelEndpoint` / `clientServerOtelEndpoint` — optional OpenTelemetry
  collector endpoints for browser and server-side telemetry.

The method returns an `EndpointReference` for the client, used later by
Envoy registration (`WithClusterEndpoint`, `WithCorsOriginExact`).

### Registration in `apphost/Program.cs`

Add the `using` directive at the top of `Program.cs`:

```csharp
using «ProjectName».AppHost.ClientApp;
```

Then register the client between the services and Envoy (or after the
last client if one already exists):

```csharp
var «clientname»Endpoint = builder.AddClientApp("«clientname»", "../clients/«clientname»", «productionPort», envoy.GetEndpoint("http"));
```

- `«productionPort»` is the port chosen in Step 1 (e.g. `4000`).
- **Dev:** `AddJavaScriptApp` runs `npm start`. Aspire auto-creates a
  `«clientname»-installer` resource that runs `npm install` first.
  The port is dynamically assigned via `PORT`.
- **Prod:** `AddDockerfile` builds the Dockerfile. The container
  listens on `productionPort` (`targetPort`) and Aspire maps it
  externally via `PORT`.
- `«clientname»Endpoint` is passed to Envoy registration so the proxy
  can route traffic to this client.
- OTel endpoints are optional and can be wired later when adding
  observability.
