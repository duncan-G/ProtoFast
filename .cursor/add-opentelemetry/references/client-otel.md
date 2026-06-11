# Step 5 — Configure client app to send telemetry

Two telemetry paths: **browser** traces/logs flow through Envoy's
`/otlp/v1/` passthrough route (using the `SERVER_URL` from transfer
state to build the base URL); **SSR server** traces/logs go directly
to the collector's HTTP endpoint via `SERVER_OTEL_ENDPOINT`.

## 5a. Install npm dependencies

Run inside the client app directory:

```bash
npm install \
  @opentelemetry/api @opentelemetry/api-logs @opentelemetry/core \
  @opentelemetry/resources @opentelemetry/sdk-trace-web \
  @opentelemetry/sdk-trace-base @opentelemetry/sdk-logs \
  @opentelemetry/sdk-node @opentelemetry/exporter-trace-otlp-http \
  @opentelemetry/exporter-logs-otlp-http @opentelemetry/instrumentation \
  @opentelemetry/instrumentation-document-load \
  @opentelemetry/instrumentation-fetch \
  @opentelemetry/auto-instrumentations-node
```

## 5b. Create `src/lib/telemetry.browser.ts`

Browser-side OTel initialisation. Reads the `SERVER_URL` from Angular's
transfer state (serialized in the `#ng-state` script tag during SSR)
to determine the Envoy proxy URL. Falls back to
`window.location.origin` if not available (e.g. in production where
the browser accesses Envoy directly).

```typescript
import {
  BatchSpanProcessor,
  StackContextManager,
  WebTracerProvider,
} from '@opentelemetry/sdk-trace-web';
import {
  LoggerProvider,
  BatchLogRecordProcessor,
} from '@opentelemetry/sdk-logs';
import { OTLPTraceExporter } from '@opentelemetry/exporter-trace-otlp-http';
import { OTLPLogExporter } from '@opentelemetry/exporter-logs-otlp-http';
import { resourceFromAttributes } from '@opentelemetry/resources';
import { logs } from '@opentelemetry/api-logs';
import { DocumentLoadInstrumentation } from '@opentelemetry/instrumentation-document-load';
import { FetchInstrumentation } from '@opentelemetry/instrumentation-fetch';
import { registerInstrumentations } from '@opentelemetry/instrumentation';
import { W3CTraceContextPropagator } from '@opentelemetry/core';

export function initBrowserTelemetry(): void {
  if (typeof window === 'undefined') {
    return;
  }

  const serverUrl = getServerUrlFromTransferState() || window.location.origin;
  const otelBase = `${serverUrl}/otlp/v1`;

  const resource = resourceFromAttributes({
    'service.name': '«projectname»-«clientname»',
    'service.version': 'browser',
    'browser.language': navigator.language,
  });

  const isDev =
    typeof import.meta !== 'undefined' &&
    (import.meta as { env?: { DEV?: boolean } }).env?.DEV === true;
  const batchConfig = isDev ? { scheduledDelayMillis: 1000 } : undefined;

  const traceExporter = new OTLPTraceExporter({
    url: `${otelBase}/traces`,
  });

  const traceProvider = new WebTracerProvider({
    resource,
    spanProcessors: [new BatchSpanProcessor(traceExporter, batchConfig)],
  });

  traceProvider.register({
    contextManager: new StackContextManager(),
    propagator: new W3CTraceContextPropagator(),
  });

  const logExporter = new OTLPLogExporter({
    url: `${otelBase}/logs`,
  });

  const loggerProvider = new LoggerProvider({
    resource,
    processors: [new BatchLogRecordProcessor(logExporter, batchConfig)],
  });

  logs.setGlobalLoggerProvider(loggerProvider);

  const ignoreUrls = [/\/otlp\/v1\//];
  const propagateToAll = [/^https?:\/\/.*/];

  registerInstrumentations({
    instrumentations: [
      new DocumentLoadInstrumentation(),
      new FetchInstrumentation({
        propagateTraceHeaderCorsUrls: propagateToAll,
        ignoreUrls,
        clearTimingResources: true,
      }),
    ],
  });
}

/**
 * Reads SERVER_URL from Angular's SSR transfer state script tag before
 * Angular bootstraps, so OTLP exports target the Envoy proxy (which has
 * the /otlp/v1/ route) instead of the Node SSR server.
 */
function getServerUrlFromTransferState(): string | null {
  try {
    const el = document.getElementById('ng-state');
    if (!el?.textContent) return null;
    const state = JSON.parse(el.textContent);
    return state['serverUrl'] || null;
  } catch {
    return null;
  }
}
```

Design choices:

- **`getServerUrlFromTransferState()`** reads the Envoy URL from
  Angular's transfer state before Angular bootstraps. This avoids
  needing an injection token (telemetry init runs before DI is
  available). The SSR server stores `SERVER_URL` in transfer state
  under the `serverUrl` key (see `add-angular-client` Step 2d).
- **No `XMLHttpRequestInstrumentation`** — Angular 21 uses `fetch()`
  natively; XHR instrumentation is unnecessary weight.
- **`ignoreUrls`** excludes `/otlp/v1/` to prevent recursive tracing
  of telemetry export requests.
- **`propagateTraceHeaderCorsUrls`** enables `traceparent` propagation
  on all fetch calls (Envoy CORS is already configured to allow these
  headers in Step 3c).
- **`StackContextManager`** — Angular 21 is zoneless, so the Zone.js
  context manager is not needed.
- **Dev-mode batch delay** — `scheduledDelayMillis: 1000` flushes
  faster during development for quicker feedback.

## 5c. Create `src/lib/grpc-trace.interceptor.ts`

ConnectRPC interceptor that creates OpenTelemetry spans for each RPC
call with standard RPC semantic convention attributes. Works for both
unary and streaming calls.

Trace context propagation (`traceparent` header injection) is handled
automatically by `FetchInstrumentation` — ConnectRPC uses `fetch()`
under the hood. This interceptor creates the parent span so the fetch
span nests under it correctly in the trace waterfall.

```typescript
import type { Interceptor } from '@connectrpc/connect';
import { context, trace, SpanKind, SpanStatusCode } from '@opentelemetry/api';
import { ConnectError } from '@connectrpc/connect';

const tracer = trace.getTracer('connectrpc');

export const traceInterceptor: Interceptor = (next) => async (req) => {
  const service = req.service.typeName;
  const method = req.method.name;

  const span = tracer.startSpan(`gRPC ${service}/${method}`, {
    kind: SpanKind.CLIENT,
    attributes: {
      'rpc.system': 'grpc',
      'rpc.service': service,
      'rpc.method': method,
    },
  });

  return context.with(trace.setSpan(context.active(), span), async () => {
    try {
      const res = await next(req);
      span.setStatus({ code: SpanStatusCode.OK });
      return res;
    } catch (err) {
      if (err instanceof ConnectError) {
        span.setAttribute('rpc.grpc.status_code', err.code);
        span.setStatus({ code: SpanStatusCode.ERROR, message: err.message });
      } else {
        span.setStatus({
          code: SpanStatusCode.ERROR,
          message: String(err),
        });
      }
      throw err;
    } finally {
      span.end();
    }
  });
};
```

Wire into the transport by adding `interceptors: [traceInterceptor]`
to `createGrpcWebTransport()` in `src/app/grpc-transport.ts`:

```typescript
import { inject, InjectionToken } from '@angular/core';
import { type Transport } from '@connectrpc/connect';
import { createGrpcWebTransport } from '@connectrpc/connect-web';
import { traceInterceptor } from '../lib/grpc-trace.interceptor';
import { SERVER_URL } from './server-url';

export const GRPC_TRANSPORT = new InjectionToken<Transport>('grpc-transport', {
  providedIn: 'root',
  factory: () =>
    createGrpcWebTransport({
      baseUrl: `${inject(SERVER_URL)}/api`,
      interceptors: [traceInterceptor],
    }),
});
```

Each RPC call produces a span named `gRPC ServiceName/MethodName` with
`rpc.system`, `rpc.service`, `rpc.method` attributes. On failure,
`rpc.grpc.status_code` is set from the `ConnectError` code.

## 5d. Create `src/instrumentation.ts`

Node SSR instrumentation. Must be imported before any other module so
monkey-patching captures all HTTP activity.

```typescript
import { NodeSDK } from '@opentelemetry/sdk-node';
import { BatchSpanProcessor } from '@opentelemetry/sdk-trace-base';
import { OTLPTraceExporter } from '@opentelemetry/exporter-trace-otlp-http';
import { OTLPLogExporter } from '@opentelemetry/exporter-logs-otlp-http';
import { BatchLogRecordProcessor } from '@opentelemetry/sdk-logs';
import { resourceFromAttributes } from '@opentelemetry/resources';
import { getNodeAutoInstrumentations } from '@opentelemetry/auto-instrumentations-node';

const otelEndpoint = (process.env['SERVER_OTEL_ENDPOINT'] || '').replace(/\/$/, '');
delete process.env['OTEL_SERVICE_NAME'];

const isDev = process.env['NODE_ENV'] === 'development';
const batchConfig = isDev ? { scheduledDelayMillis: 1000 } : undefined;

// In the unified SSR host every client bundle runs in one process; only the
// first bundle to load may start the Node SDK (a second start would clobber
// the global providers and double-patch auto-instrumentations).
const otelGlobal = globalThis as { __nodeOtelSdkStarted?: boolean };

if (otelEndpoint && !otelGlobal.__nodeOtelSdkStarted) {
  otelGlobal.__nodeOtelSdkStarted = true;
  const traceExporter = new OTLPTraceExporter({
    url: `${otelEndpoint}/v1/traces`,
  });
  const logExporter = new OTLPLogExporter({
    url: `${otelEndpoint}/v1/logs`,
  });

  const sdk = new NodeSDK({
    resource: resourceFromAttributes({
      'service.name': '«projectname»-«clientname»-ssr',
      'service.version': 'node',
    }),
    spanProcessors: [new BatchSpanProcessor(traceExporter, batchConfig)],
    logRecordProcessors: [
      new BatchLogRecordProcessor(logExporter, batchConfig),
    ],
    instrumentations: [
      getNodeAutoInstrumentations({
        '@opentelemetry/instrumentation-fs': { enabled: false },
        '@opentelemetry/instrumentation-dns': { enabled: false },
        '@opentelemetry/instrumentation-net': { enabled: false },
        '@opentelemetry/instrumentation-http': {
          enabled: true,
          ignoreIncomingRequestHook: (req) =>
            req.url?.startsWith('/otlp') ?? false,
        },
      }),
    ],
  });

  sdk.start();
}
```

Design choices:

- **HTTP OTLP exporters** (not gRPC) to avoid the heavy `@grpc/grpc-js`
  dependency. `SERVER_OTEL_ENDPOINT` points at the collector's HTTP
  endpoint (port 4318).
- **`delete process.env['OTEL_SERVICE_NAME']`** — Aspire injects
  `OTEL_SERVICE_NAME` into every resource, which would override the
  manually set `service.name` attribute. Deleting it before SDK init
  ensures the SSR service name is distinct from the Aspire resource name.
- **`instrumentation-fs` disabled** — avoids extremely noisy
  file-system spans from SSR rendering.
- **`instrumentation-dns` / `instrumentation-net` disabled** — low
  signal-to-noise ratio for an SSR server; they add volume without
  actionable insight.
- **`instrumentation-http` with `ignoreIncomingRequestHook`** —
  suppresses `/otlp` requests so only genuine SSR traffic appears in
  the Node server's telemetry (browser telemetry transit would
  otherwise be misreported as Node-originated spans).

## 5e. Wire telemetry into Angular entry points

In `src/main.ts` (browser entry), call `initBrowserTelemetry()` before
`bootstrapApplication()`:

```typescript
import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';
import { initBrowserTelemetry } from './lib/telemetry.browser';

initBrowserTelemetry();

bootstrapApplication(App, appConfig).catch((err) => console.error(err));
```

In `src/server.ts` (SSR entry), import instrumentation as the very
first line:

```typescript
import './instrumentation';

import { AngularNodeAppEngine, ... } from '@angular/ssr/node';
// ... rest of server.ts unchanged
```

## 5f. Update `apphost/Program.cs` — use HTTP endpoint for SSR

Change `clientServerOtelEndpoint` from gRPC to HTTP since the Node
SSR server uses HTTP OTLP exporters:

```csharp
var otelHttp = otel.GetEndpoint(OpenTelemetryCollectorResource.OtlpHttpEndpointName);

var adminDev = builder.AddClientApp(
    "admin", "../clients/admin", adminWeb, otelHttp, otelHttp);
```

Both endpoints now use `OtlpHttpEndpointName` (port 4318).
`BROWSER_OTEL_ENDPOINT` is injected but unused by the browser (it uses
`SERVER_URL`-based `/otlp/v1/`); it remains available for future use.
Note `adminWeb` — the client's `SERVER_URL` is its per-client Envoy
listener endpoint returned by `proxy.WithClient(builder, "admin")`.
When the unified SSR host is registered (`AddClientHost`), pass the
same two OTel endpoints to it as well.

## Telemetry flow

In the current architecture (HTTPS everywhere, no dev-server proxy):

1. **Browser telemetry data** (traces/logs the browser exports) flows
   Browser → Envoy HTTPS → OTel Collector. The browser reads
   `SERVER_URL` from Angular transfer state to find Envoy's URL and
   sends to `{serverUrl}/otlp/v1/traces` (and `/logs`).
2. **Node server's own telemetry** (spans/logs generated by its HTTP
   instrumentation) exports directly to the OTel Collector via
   `SERVER_OTEL_ENDPOINT` — it never goes through Envoy.

The `ignoreIncomingRequestHook` in Step 5d is still important because
the SSR server may receive requests that transit through it (e.g.
health checks or other middleware) and `/otlp` paths should not
generate misleading telemetry.

## Files created / modified summary

| File | Action | Purpose |
|------|--------|---------|
| `src/lib/telemetry.browser.ts` | Create | Browser OTel init (traces + logs via OTLP HTTP to Envoy) |
| `src/lib/grpc-trace.interceptor.ts` | Create | ConnectRPC interceptor for RPC span creation |
| `src/instrumentation.ts` | Create | Node SSR OTel init (traces + logs via OTLP HTTP) |
| `src/main.ts` | Modify | Call `initBrowserTelemetry()` before bootstrap |
| `src/server.ts` | Modify | Import `./instrumentation` as first line |
| `src/app/grpc-transport.ts` | Modify | Add `traceInterceptor` to transport interceptors |
| `apphost/Program.cs` | Modify | Use HTTP endpoint for `clientServerOtelEndpoint` |

## Environment variable summary

| Env var | Source | Used by |
|---|---|---|
| `SERVER_OTEL_ENDPOINT` | Collector HTTP endpoint (port 4318) | `src/instrumentation.ts` — SSR OTLP HTTP export |
| `BROWSER_OTEL_ENDPOINT` | Collector HTTP endpoint (port 4318) | Injected but unused — browser uses `SERVER_URL` + `/otlp/v1/` |
| `SERVER_URL` | Envoy HTTPS proxy endpoint | `src/lib/telemetry.browser.ts` — browser OTel export base URL (via transfer state) |
