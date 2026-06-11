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

The client needs to know its Envoy listener URL (for gRPC transport
and browser telemetry). In dev mode, the SSR server reads `SERVER_URL`
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

- **SSR (dev)**: Reads `SERVER_URL` from the env var injected by
  `AddClientApp` (pointing at the client's per-client Envoy listener),
  stores it in `TransferState` so it's serialized into the HTML.
- **Browser**: Reads from `TransferState` (which was hydrated from the
  serialized HTML). Falls back to `window.location.origin` when empty —
  which is the publish-mode path: the unified SSR host does not set
  `SERVER_URL`, and the page, API, and telemetry are all same-origin
  behind Envoy.
- **No proxy config needed**: The browser enters through the Envoy
  listener; the dev server never proxies API calls.

## 2e. Register the client as an Aspire resource

In run mode each client is its own Angular dev server; in publish mode
(and in the `dev-host` smoke-test mode) all clients are served by the
single unified SSR host container (`clients/host/`):

| Mode | What runs | Registered by |
|------|-----------|---------------|
| Dev  | `npm start` → Angular dev server (HTTPS) per client | `AddClientApp` |
| Dev-host / Publish | one `clients` container from `clients/host/Dockerfile` | `AddClientHost` (once, not per client) |

In every mode the client also gets `proxy.WithClient(...)`, which in
dev creates the client's Envoy listener (fixed internal target ports
20000, 20001, … in registration order) and in publish wires the
`«clientname»-domain` parameter into the client's virtual host.

### Dev-mode package

`Aspire.Hosting.JavaScript` provides `AddJavaScriptApp`. If not already
added, install from inside `apphost/`:

```bash
cd apphost
aspire add javascript --non-interactive
```

### Extension methods

All Angular clients are registered through reusable extension methods
in `apphost/ClientApp/ClientAppResourceBuilderExtensions.cs`. These
already exist in a bootstrapped project; the relevant signatures are:

```csharp
namespace «ProjectName».AppHost.ClientApp;

public static class ClientAppResourceBuilderExtensions
{
    // Run mode only: one Angular dev server per client. serverEndpoint is the
    // client's Envoy listener endpoint (returned by proxy.WithClient), injected
    // as SERVER_URL. Returns the dev server's HTTPS endpoint (the Envoy upstream).
    public static EndpointReference AddClientApp(
        this IDistributedApplicationBuilder builder,
        string clientName,
        string clientPath,
        EndpointReference serverEndpoint,
        EndpointReference? clientOtelEndpoint = null,
        EndpointReference? clientServerOtelEndpoint = null);

    // Publish mode (and dev-host smoke tests): the unified SSR host container,
    // built from clients/host/Dockerfile with the repo root as build context.
    // Registered once for all clients. Returns the host's HTTP endpoint.
    public static EndpointReference AddClientHost(
        this IDistributedApplicationBuilder builder,
        string name,
        string defaultClient,
        EndpointReference? clientOtelEndpoint = null,
        EndpointReference? clientServerOtelEndpoint = null);
}
```

Dev mode details (`AddClientApp`):

- **`WithHttpsEndpoint(env: "PORT")`** registers the dev server as
  HTTPS with an Aspire-assigned port.
- **`WithHttpsDeveloperCertificate()`** enrolls the app in Aspire's
  developer certificate infrastructure.
- **`WithHttpsCertificateConfiguration`** injects `SSL_CERT` and
  `SSL_KEY` environment variables with paths to the certificate and
  key files. The `start` script passes these to `ng serve --ssl`.
- Aspire auto-creates a `«clientname»-installer` resource that runs
  `npm install` first.

### Registration in `apphost/Program.cs`

Add the `using` directive at the top of `Program.cs` (if missing):

```csharp
using «ProjectName».AppHost.ClientApp;
```

Then register the client after the existing clients, following the
established pattern:

```csharp
var «clientname»Web = proxy.WithClient(builder, "«clientname»");

if (useSsrHost)
{
    // AddClientHost is already registered once; nothing per-client here.
}
else
{
    var «clientname»Dev = builder.AddClientApp(
        "«clientname»", "../clients/«clientname»", «clientname»Web, otelHttp, otelHttp);
    proxy.WithUpstreamEndpoint("CLIENT_«CLIENTNAME»", «clientname»Dev);
}
```

- **`proxy.WithClient(builder, "«clientname»")`** — dev: creates the
  per-client Envoy listener endpoint (`«clientname»-web` on the envoy
  resource) and the `CLIENT_«CLIENTNAME»_LISTENER_PORT` env var;
  publish: creates the `«clientname»-domain` parameter and the
  `CLIENT_«CLIENTNAME»_DOMAIN` env var. Returns the endpoint to use as
  `SERVER_URL`.
- **`AddClientApp`** (dev only) — runs the Angular dev server with the
  Envoy listener endpoint as `SERVER_URL`.
- **`WithUpstreamEndpoint("CLIENT_«CLIENTNAME»", …)`** — injects
  `CLIENT_«CLIENTNAME»_HOST/PORT` so Envoy's entrypoint can build the
  client's upstream cluster in dev.
- The `useSsrHost` flag and the single `AddClientHost("clients", …)` /
  `WithUpstreamEndpoint("CLIENTS_HOST", …)` registration already exist
  in `Program.cs` — do not duplicate them. If the new client should
  become the default, update the `defaultClient:` argument and the
  envoy `DEFAULT_CLIENT` env var.
- OTel endpoints (`otelHttp`) are optional and can be wired later when
  adding observability.

## 2f. Guard OTel instrumentation for the unified host

If the client has Node OTel instrumentation (`src/instrumentation.ts`,
added by the add-opentelemetry skill), it must guard SDK startup so
that only the first bundle loaded into the unified SSR host process
starts the SDK:

```typescript
const otelGlobal = globalThis as { __nodeOtelSdkStarted?: boolean };

if (otelEndpoint && !otelGlobal.__nodeOtelSdkStarted) {
  otelGlobal.__nodeOtelSdkStarted = true;
  // ... new NodeSDK(...).start();
}
```

Copy the pattern from an existing client (e.g.
`clients/admin/src/instrumentation.ts`).
