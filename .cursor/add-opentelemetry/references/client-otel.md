# Step 5 — Configure client app to send telemetry

Two telemetry paths: **browser** traces/logs flow through Envoy's
`/otlp/v1/` passthrough route (same-origin, no CORS issues); **SSR
server** traces/logs go directly to the collector's HTTP endpoint via
`SERVER_OTEL_ENDPOINT`.

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

Browser-side OTel initialisation. Uses the same-origin `/otlp/v1/`
path (routed through Envoy to the collector) so no injection tokens or
`TransferState` are needed.

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

  const otelBase = `${window.location.origin}/otlp/v1`;

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
```

Design choices:

- **Same-origin `/otlp/v1/`** — the browser always sends telemetry
  relative to its own origin. In dev mode this is proxied to Envoy via
  `proxy.conf.mjs`; in production Envoy is the origin.
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
import { InjectionToken } from '@angular/core';
import { type Transport } from '@connectrpc/connect';
import { createGrpcWebTransport } from '@connectrpc/connect-web';
import { traceInterceptor } from '../lib/grpc-trace.interceptor';

export const GRPC_TRANSPORT = new InjectionToken<Transport>('grpc-transport', {
  providedIn: 'root',
  factory: () =>
    createGrpcWebTransport({
      baseUrl:
        typeof window !== 'undefined'
          ? `${window.location.origin}/api`
          : '/api',
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

if (otelEndpoint) {
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
- **`instrumentation-http` with `ignoreIncomingRequestHook`** — in dev
  mode, browser telemetry requests to `/otlp/v1/...` are proxied through
  the Node dev server (via `proxy.conf.mjs`) before reaching Envoy.
  Without this hook, the Node HTTP instrumentation would capture those
  proxy requests as its own spans/logs, producing misleading telemetry
  that appears to originate from the Node server. The hook suppresses
  incoming `/otlp` requests so only genuine SSR traffic is instrumented.

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

## 5f. Update `proxy.conf.mjs` for dev mode

Add the `/otlp` path so the Angular dev server proxies browser
telemetry requests to Envoy (which forwards to the collector via its
`/otlp/v1/` passthrough route):

```javascript
const serverUrl = process.env.SERVER_URL || 'http://localhost:8080';

export default {
  '/otlp': { target: serverUrl, secure: false, changeOrigin: true },
  '/auth': { target: serverUrl, secure: false, changeOrigin: true },
  '/payments': { target: serverUrl, secure: false, changeOrigin: true },
  '/api': { target: serverUrl, secure: false, changeOrigin: true },
};
```

**Two distinct telemetry paths in dev mode:**

1. **Browser telemetry data** (traces/logs the browser exports) flows
   Browser → Node dev server (proxy) → Envoy → OTel Collector. The
   Node server is only a pass-through here.
2. **Node server's own telemetry** (spans/logs generated by its HTTP
   instrumentation) exports directly to the OTel Collector via
   `SERVER_OTEL_ENDPOINT` — it never goes through Envoy.

Because the `/otlp` proxy requests transit through Node's HTTP layer,
the Node HTTP auto-instrumentation will capture them and generate
spans/logs as if the Node server itself originated that traffic. The
`ignoreIncomingRequestHook` in Step 5d suppresses these `/otlp`
requests so only genuine SSR traffic appears in the Node server's
telemetry.

## 5g. Update `apphost/Program.cs` — use HTTP endpoint for SSR

Change `clientServerOtelEndpoint` from gRPC to HTTP since the Node
SSR server uses HTTP OTLP exporters:

```csharp
var adminEndpoint = builder.AddClientApp("admin-api", "../clients/admin", 4000, proxy.GetEndpoint("http"),
    clientOtelEndpoint: otel.GetEndpoint(OpenTelemetryCollectorResource.OtlpHttpEndpointName),
    clientServerOtelEndpoint: otel.GetEndpoint(OpenTelemetryCollectorResource.OtlpHttpEndpointName));
```

Both endpoints now use `OtlpHttpEndpointName` (port 4318).
`BROWSER_OTEL_ENDPOINT` is injected but unused by the browser (it uses
same-origin `/otlp/v1/`); it remains available for future use.

## Files created / modified summary

| File | Action | Purpose |
|------|--------|---------|
| `src/lib/telemetry.browser.ts` | Create | Browser OTel init (traces + logs via OTLP HTTP) |
| `src/lib/grpc-trace.interceptor.ts` | Create | ConnectRPC interceptor for RPC span creation |
| `src/instrumentation.ts` | Create | Node SSR OTel init (traces + logs via OTLP HTTP) |
| `src/main.ts` | Modify | Call `initBrowserTelemetry()` before bootstrap |
| `src/server.ts` | Modify | Import `./instrumentation` as first line |
| `src/app/grpc-transport.ts` | Modify | Add `traceInterceptor` to transport interceptors |
| `proxy.conf.mjs` | Modify | Add `/otlp` proxy route for dev mode |
| `apphost/Program.cs` | Modify | Use HTTP endpoint for `clientServerOtelEndpoint` |

## Environment variable summary

| Env var | Source | Used by |
|---|---|---|
| `SERVER_OTEL_ENDPOINT` | Collector HTTP endpoint (port 4318) | `src/instrumentation.ts` — SSR OTLP HTTP export |
| `BROWSER_OTEL_ENDPOINT` | Collector HTTP endpoint (port 4318) | Injected but unused — browser uses same-origin `/otlp/v1/` |
| `SERVER_URL` | Envoy proxy endpoint | `proxy.conf.mjs` — dev-mode proxy target (including `/otlp`) |
