import {
  BatchSpanProcessor,
  StackContextManager,
  WebTracerProvider,
  type Span,
  type SpanProcessor,
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
import { ROOT_CONTEXT, trace, type Context } from '@opentelemetry/api';

/**
 * Context manager that adopts the document-load trace as the ambient context
 * once the page has finished loading.
 *
 * DocumentLoadInstrumentation emits a `documentLoad` root span for the initial
 * page load. After that span's synchronous work unwinds, the context stack is
 * empty again — so every fetch()/gRPC call made afterwards would otherwise
 * start its own root span (a brand-new trace). By treating the captured
 * page-load context as the active context whenever the stack has no span, those
 * follow-up asset requests nest under the document-load trace instead.
 */
class PageLoadContextManager extends StackContextManager {
  private pageLoadContext: Context | undefined;

  setPageLoadContext(ctx: Context): void {
    this.pageLoadContext = ctx;
  }

  override active(): Context {
    const current = super.active();
    if (this.pageLoadContext && trace.getSpan(current) === undefined) {
      return this.pageLoadContext;
    }
    return current;
  }
}

/**
 * Captures the `documentLoad` root span the moment it starts so the context
 * manager can reuse its trace as the parent for post-load requests.
 */
class DocumentLoadContextCapture implements SpanProcessor {
  constructor(private readonly onCaptured: (ctx: Context) => void) {}

  onStart(span: Span): void {
    if (span.name === 'documentLoad') {
      this.onCaptured(trace.setSpan(ROOT_CONTEXT, span));
    }
  }

  onEnd(): void {}

  forceFlush(): Promise<void> {
    return Promise.resolve();
  }

  shutdown(): Promise<void> {
    return Promise.resolve();
  }
}

/**
 * Initialize OpenTelemetry for the browser.
 * Exports traces and logs to the OTel collector via Envoy's /otlp/v1/ passthrough route.
 * Instruments page-load timing and all fetch() calls with distributed trace propagation.
 */
export function initBrowserTelemetry(): void {
  if (typeof window === 'undefined') {
    return;
  }

  const serverUrl = getServerUrlFromTransferState() || window.location.origin;
  const otelBase = `${serverUrl}/otlp/v1`;

  const resource = resourceFromAttributes({
    'service.name': 'protofast-client',
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

  const contextManager = new PageLoadContextManager();

  const traceProvider = new WebTracerProvider({
    resource,
    spanProcessors: [
      new DocumentLoadContextCapture((ctx) =>
        contextManager.setPageLoadContext(ctx),
      ),
      new BatchSpanProcessor(traceExporter, batchConfig),
    ],
  });

  traceProvider.register({
    contextManager,
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
