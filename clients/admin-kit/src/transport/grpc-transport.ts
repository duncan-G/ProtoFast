import { inject, InjectionToken } from '@angular/core';
import { type Transport } from '@connectrpc/connect';
import { createGrpcWebTransport } from '@connectrpc/connect-web';
import { traceInterceptor } from './grpc-trace.interceptor';
import { SERVER_URL } from './server-url';

/** gRPC-web to the api on this host's `/api/`; the operator's session is the only credential. */
export const GRPC_TRANSPORT = new InjectionToken<Transport>('grpc-transport', {
  providedIn: 'root',
  factory: () =>
    createGrpcWebTransport({
      baseUrl: `${inject(SERVER_URL)}/api`,
      interceptors: [traceInterceptor],
    }),
});
