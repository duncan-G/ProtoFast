// OpenTelemetry for the clients host, configured from the OTEL_ variables alone: ProtoStrap sets
// the endpoint, the protocol (http/protobuf), the service name and the resource; `aspire run`
// sets its dashboard's. server.mjs imports this only when OTEL_EXPORTER_OTLP_ENDPOINT is set,
// before any app loads, and shuts the SDK down after draining, so what's buffered is sent.
//
// The apps' bundles carry their own copy of @opentelemetry/api, which finds this SDK's tracer
// provider, context manager and propagator through the API's process-wide registry: each render
// is a span from the app's own SSR middleware (lib/telemetry.ssr.ts, admin-kit/telemetry/ssr.ts),
// and its traceparent goes to the browser. Express is inside those bundles, where no
// instrumentation can patch it, so the one instrumentation here is fetch's (undici), which
// Node reports through diagnostics channels whoever calls it.
import { UndiciInstrumentation } from '@opentelemetry/instrumentation-undici';
import { NodeSDK } from '@opentelemetry/sdk-node';

export const sdk = new NodeSDK({
  // No exporter named: the SDK picks the OTLP one for OTEL_EXPORTER_OTLP_PROTOCOL and reads the
  // endpoint and headers itself.
  // No log exporter: the logs are JSON lines on stdout already, and both would send every
  // line twice.
  logRecordProcessors: [],
  instrumentations: [new UndiciInstrumentation()],
});
sdk.start();
