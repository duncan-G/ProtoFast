import type { Interceptor } from '@connectrpc/connect';
import { context, trace, SpanKind, SpanStatusCode } from '@opentelemetry/api';
import { ConnectError } from '@connectrpc/connect';

const tracer = trace.getTracer('connectrpc');

/**
 * ConnectRPC interceptor that creates an OpenTelemetry span for each RPC call
 * with standard RPC semantic convention attributes.
 *
 * Trace context propagation (traceparent header injection) is handled
 * automatically by FetchInstrumentation — this interceptor only creates
 * the parent span so the fetch span nests under it correctly.
 *
 * Works for both unary and streaming calls.
 */
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
