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
"start": "npm run generate:grpc && ng serve --host 0.0.0.0 --port ${PORT:-4200} --ssl --ssl-cert \"${SSL_CERT}\" --ssl-key \"${SSL_KEY}\" --allowed-hosts",
"build": "npm run generate:grpc && ng build",
```

- `generate:grpc` runs `buf generate` using the client's `buf.gen.yaml`.
- `--host 0.0.0.0` binds the dev server so containers can reach it.
- `--port ${PORT:-4200}` reads the listen port from Aspire's `PORT`
  env var; falls back to 4200 for manual runs outside the orchestrator.
- `--ssl --ssl-cert "${SSL_CERT}" --ssl-key "${SSL_KEY}"` enables HTTPS
  using the certificate paths injected by Aspire's
  `WithHttpsCertificateConfiguration`.
- `--allowed-hosts` accepts connections from any hostname (needed when
  Aspire's assigned URL differs from `localhost`).
- Both `start` and `build` run codegen first so generated types are
  always current.

Also remove `proxyConfig` from `angular.json` if the generator added
it — there is no dev-server proxy. Under
`projects.«clientname».architect.serve`, delete the `options` block
containing `proxyConfig` (or just the `proxyConfig` key if other
options exist). Delete any `proxy.conf.json` or `proxy.conf.mjs`
the generator may have created.

## 2d. Create the `SERVER_URL` injection token

The client needs to know the Envoy proxy URL (for gRPC transport and
browser telemetry). In dev mode, the SSR server reads `SERVER_URL`
from its environment and transfers it to the browser via Angular's
`TransferState`. The browser reads it from the transfer state and
falls back to `window.location.origin`.

Create `clients/«clientname»/src/app/server-url.ts`:

```typescript
import { InjectionToken, makeStateKey } from '@angular/core';

export const SERVER_URL_KEY = makeStateKey<string>('serverUrl');
export const SERVER_URL = new InjectionToken<string>('SERVER_URL');
```

Update `clients/«clientname»/src/app/app.config.server.ts` to read
the env var and store it in transfer state:

```typescript
import { mergeApplicationConfig, ApplicationConfig, inject, TransferState } from '@angular/core';
import { provideServerRendering, withRoutes } from '@angular/ssr';
import { appConfig } from './app.config';
import { serverRoutes } from './app.routes.server';
import { SERVER_URL, SERVER_URL_KEY } from './server-url';

const serverConfig: ApplicationConfig = {
  providers: [
    provideServerRendering(withRoutes(serverRoutes)),
    {
      provide: SERVER_URL,
      useFactory: () => {
        const transferState = inject(TransferState);
        const url = process.env['SERVER_URL'] ?? '';
        transferState.set(SERVER_URL_KEY, url);
        return url;
      },
    },
  ]
};

export const config = mergeApplicationConfig(appConfig, serverConfig);
```

Update `clients/«clientname»/src/app/app.config.ts` to read from
transfer state on the browser:

```typescript
import { ApplicationConfig, inject, provideBrowserGlobalErrorListeners, TransferState } from '@angular/core';
import { provideRouter } from '@angular/router';

import { routes } from './app.routes';
import { provideClientHydration, withEventReplay } from '@angular/platform-browser';
import { SERVER_URL, SERVER_URL_KEY } from './server-url';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideClientHydration(withEventReplay()),
    {
      provide: SERVER_URL,
      useFactory: () => {
        const transferState = inject(TransferState);
        return transferState.get(SERVER_URL_KEY, window.location.origin);
      },
    },
  ]
};
```

This pattern:

- **SSR**: Reads `SERVER_URL` from the env var injected by
  `AddClientApp` (pointing at Envoy's HTTPS endpoint), stores it in
  `TransferState` so it's serialized into the HTML.
- **Browser**: Reads from `TransferState` (which was hydrated from the
  serialized HTML). Falls back to `window.location.origin` if not
  present (e.g. in production where the browser accesses Envoy
  directly).
- **No proxy config needed**: The browser talks directly to the Envoy
  URL provided by `SERVER_URL`, not through the dev server.

## 2e. Register the client as an Aspire resource

The client has two hosting modes gated on
`builder.ExecutionContext.IsPublishMode`:

| Mode | API | What runs |
|------|-----|-----------|
| Dev  | `AddJavaScriptApp` | `npm start` → Angular dev server (HTTPS) |
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
    /// Adds a client app to the distributed application. In publish mode, the client is built as a
    /// Docker container with external HTTPS endpoints.
    /// </summary>
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
                .WithHttpsEndpoint(targetPort: productionPort, env: "PORT")
                .WithExternalHttpEndpoints();

            var clientEndpoint = clientApp.GetEndpoint("https", KnownNetworkIdentifiers.PublicInternet);
            clientApp
                .WithEnvironment("NG_ALLOWED_HOSTS", clientEndpoint.Property(EndpointProperty.Host))
                .WithEnvironment("SERVER_URL", serverEndpoint)
                .WithOtelEndpoints(clientOtelEndpoint, clientServerOtelEndpoint);

            return clientEndpoint;
        }

        var clientAppDev = builder.AddJavaScriptApp(clientName, clientPath, runScriptName: "start")
            .WithHttpsEndpoint(env: "PORT")
            .WithHttpsDeveloperCertificate()
            .WithHttpsCertificateConfiguration(ctx =>
            {
                ctx.EnvironmentVariables["SSL_CERT"] = ctx.CertificatePath;
                ctx.EnvironmentVariables["SSL_KEY"] = ctx.KeyPath;
                return Task.CompletedTask;
            })
            .WithEnvironment("SERVER_URL", serverEndpoint);
            
        clientAppDev.WithOtelEndpoints(clientOtelEndpoint, clientServerOtelEndpoint);

        return clientAppDev.GetEndpoint("https", KnownNetworkIdentifiers.LocalhostNetwork);
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
  on in publish mode, passed as `targetPort` to `WithHttpsEndpoint`.
  Ports start at 4000 and increment per client (4000, 4001, …). The
  skill auto-detects the next available port by scanning existing
  `AddClientApp` calls in `Program.cs`.
- `serverEndpoint` — the endpoint the client's SSR server should call
  (typically Envoy's HTTPS endpoint).
- `clientOtelEndpoint` / `clientServerOtelEndpoint` — optional OpenTelemetry
  collector endpoints for browser and server-side telemetry.

Dev mode:

- **`WithHttpsEndpoint`** registers the dev server as HTTPS.
- **`WithHttpsDeveloperCertificate()`** enrolls the app in Aspire's
  developer certificate infrastructure.
- **`WithHttpsCertificateConfiguration`** injects `SSL_CERT` and
  `SSL_KEY` environment variables with paths to the certificate and
  key files. The `start` script passes these to `ng serve --ssl`.
- Returns `GetEndpoint("https", KnownNetworkIdentifiers.LocalhostNetwork)`.

The method returns an `EndpointReference` for the client, used later by
Envoy registration (`WithUpstreamEndpoint`, `WithCorsOriginExact`).

### Registration in `apphost/Program.cs`

Add the `using` directive at the top of `Program.cs`:

```csharp
using «ProjectName».AppHost.ClientApp;
```

Then register the client between the services and Envoy (or after the
last client if one already exists):

```csharp
var «clientname»Endpoint = builder.AddClientApp("«clientname»", "../clients/«clientname»", «productionPort», proxy.GetEndpoint("https"));
```

- `«productionPort»` is the port chosen in Step 1 (e.g. `4000`).
- **`proxy.GetEndpoint("https")`** passes Envoy's HTTPS endpoint as
  `SERVER_URL`, so the Angular SSR server knows where to direct
  backend requests.
- **Dev:** `AddJavaScriptApp` runs `npm start` with HTTPS. Aspire
  auto-creates a `«clientname»-installer` resource that runs
  `npm install` first. The port is dynamically assigned via `PORT`.
- **Prod:** `AddDockerfile` builds the Dockerfile. The container
  listens on `productionPort` (`targetPort`) and Aspire maps it
  externally via `PORT`.
- `«clientname»Endpoint` is passed to Envoy registration so the proxy
  can route traffic to this client.
- OTel endpoints are optional and can be wired later when adding
  observability.
