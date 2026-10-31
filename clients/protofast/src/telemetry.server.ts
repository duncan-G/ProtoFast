/**
 * OpenTelemetry for this app's server when it runs on its own (`ng serve`, under `aspire run`).
 * Must load before any other module.
 *
 * Configured from the OTEL_ variables alone: the SDK picks the OTLP exporter for
 * OTEL_EXPORTER_OTLP_PROTOCOL and reads the endpoint, headers, service name and resource itself.
 * In the clients host (clients/host/server.mjs) every app's bundle shares one process, and the
 * host starts the SDK and sets the flag below before loading them, so this starts nothing there.
 */
import { NodeSDK } from '@opentelemetry/sdk-node';
import { resourceFromAttributes } from '@opentelemetry/resources';
import { getNodeAutoInstrumentations } from '@opentelemetry/auto-instrumentations-node';

// A second start would clobber the global providers and double-patch the instrumentations.
const otelGlobal = globalThis as { __nodeOtelSdkStarted?: boolean };

if (process.env['OTEL_EXPORTER_OTLP_ENDPOINT'] && !otelGlobal.__nodeOtelSdkStarted) {
  otelGlobal.__nodeOtelSdkStarted = true;

  new NodeSDK({
    // OTEL_SERVICE_NAME, when it is set, wins over this name.
    resource: resourceFromAttributes({ 'service.name': 'protofast-client-server' }),
    // Logs stay on the console, which the dev server already shows: sending them over OTLP too
    // would show every line twice.
    logRecordProcessors: [],
    instrumentations: [
      getNodeAutoInstrumentations({
        '@opentelemetry/instrumentation-fs': { enabled: false },
        '@opentelemetry/instrumentation-dns': { enabled: false },
        '@opentelemetry/instrumentation-net': { enabled: false },
        // Each render is a span of its own (lib/telemetry.ssr.ts).
        '@opentelemetry/instrumentation-http': { enabled: false },
      }),
    ],
  }).start();
}
