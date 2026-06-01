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

/**
 * Initialize OpenTelemetry for the browser.
 * Exports traces and logs to the OTel collector via Envoy's /otlp/v1/ passthrough route.
 * Instruments page-load timing and all fetch() calls with distributed trace propagation.
 */
export function initBrowserTelemetry(): void {
  if (typeof window === 'undefined') {
    return;
  }

  const otelBase = `${window.location.origin}/otlp/v1`;

  const resource = resourceFromAttributes({
    'service.name': 'admin-client',
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
