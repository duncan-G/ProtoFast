import { inject, InjectionToken } from '@angular/core';
import { type Transport } from '@connectrpc/connect';
import { createGrpcWebTransport } from '@connectrpc/connect-web';
import { traceInterceptor } from '../lib/grpc-trace.interceptor';
import { SERVER_URL } from './server-url';

/**
 * gRPC-Web to the page's own origin: the edge routes `/<package>.<Service>/` to the service that
 * serves the package, as it is, so the base URL carries no path of its own.
 */
export const GRPC_TRANSPORT = new InjectionToken<Transport>('grpc-transport', {
  providedIn: 'root',
  factory: () =>
    createGrpcWebTransport({
      baseUrl: inject(SERVER_URL),
      interceptors: [traceInterceptor],
    }),
});
