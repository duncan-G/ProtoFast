import {
  inject,
  provideAppInitializer,
  REQUEST_CONTEXT,
  type EnvironmentProviders,
  type InjectionToken,
} from '@angular/core';
import { Meta } from '@angular/platform-browser';
import {
  context,
  propagation,
  ROOT_CONTEXT,
  SpanKind,
  SpanStatusCode,
  trace,
} from '@opentelemetry/api';
import type { NextFunction, Request, Response } from 'express';

const tracer = trace.getTracer('ssr');

/** Handed to `AngularNodeAppEngine.handle()` so the app can reach the render's trace. */
export interface SsrRequestContext {
  traceparent?: string;
}

/**
 * Express middleware that wraps each server-rendered page in a SERVER span.
 *
 * The render, and every gRPC call it makes, runs inside that span's context, and the span's
 * traceparent is left on `res.locals` for {@link ssrRequestContext}. The browser then parents
 * its `documentLoad` span on the same traceparent (via `<meta name="traceparent">`), so one
 * trace covers the SSR render, its backend calls and the page load. Mount it after the static
 * file handler so asset requests stay out of it.
 */
export function ssrTraceMiddleware(req: Request, res: Response, next: NextFunction): void {
  const parent = propagation.extract(ROOT_CONTEXT, req.headers);
  const span = tracer.startSpan(
    `${req.method} ${req.path}`,
    {
      kind: SpanKind.SERVER,
      attributes: { 'http.request.method': req.method, 'url.path': req.path },
    },
    parent,
  );
  const ctx = trace.setSpan(parent, span);

  const carrier: Record<string, string> = {};
  propagation.inject(ctx, carrier);
  res.locals['traceparent'] = carrier['traceparent'];

  let ended = false;
  const end = () => {
    if (ended) return;
    ended = true;
    span.setAttribute('http.response.status_code', res.statusCode);
    if (res.statusCode >= 500) {
      span.setStatus({ code: SpanStatusCode.ERROR });
    }
    span.end();
  };
  res.once('finish', end);
  res.once('close', end);

  context.with(ctx, next);
}

/** The request context to pass to `AngularNodeAppEngine.handle()`. */
export function ssrRequestContext(res: Response): SsrRequestContext {
  return { traceparent: res.locals['traceparent'] };
}

/**
 * Server-side app initializer that writes the render's traceparent into
 * `<meta name="traceparent">`, which DocumentLoadInstrumentation reads in the browser.
 */
export function provideSsrTraceparentMeta(): EnvironmentProviders {
  return provideAppInitializer(() => {
    const requestContext = inject(REQUEST_CONTEXT as InjectionToken<SsrRequestContext | null>, {
      optional: true,
    });
    if (requestContext?.traceparent) {
      inject(Meta).addTag({ name: 'traceparent', content: requestContext.traceparent });
    }
  });
}
