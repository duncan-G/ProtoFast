import { inject, InjectionToken } from '@angular/core';
import { type Transport } from '@connectrpc/connect';
import { createGrpcWebTransport } from '@connectrpc/connect-web';
import { traceInterceptor } from './grpc-trace.interceptor';
import { SERVER_URL } from './server-url';

/**
 * gRPC-Web to the api on this host: the edge routes `/admin.` (each RPC is
 * `/<package>.<Service>/<Method>`) to the api as it is, so the base URL carries no path of its
 * own. The operator's session is the only credential.
 */
export const GRPC_TRANSPORT = new InjectionToken<Transport>('grpc-transport', {
  providedIn: 'root',
  factory: () =>
    createGrpcWebTransport({
      baseUrl: inject(SERVER_URL),
      interceptors: [traceInterceptor],
    }),
});
