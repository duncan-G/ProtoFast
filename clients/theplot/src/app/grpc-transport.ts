import { inject, InjectionToken, PLATFORM_ID } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { Code, ConnectError, type Interceptor, type Transport } from '@connectrpc/connect';
import { createGrpcWebTransport } from '@connectrpc/connect-web';
import { traceInterceptor } from '../lib/grpc-trace.interceptor';
import { redirectToSignIn } from './auth/sign-in-redirect';
import { SERVER_URL } from './server-url';

/** The edge found no live session behind the call; the SSR gate covers the server side. */
const signInWhenUnauthenticated: Interceptor = (next) => async (req) => {
  try {
    return await next(req);
  } catch (err) {
    if (err instanceof ConnectError && err.code === Code.Unauthenticated) {
      redirectToSignIn();
    }
    throw err;
  }
};

/**
 * gRPC-Web to the page's own origin: the edge routes `/<package>.<Service>/` to the service that
 * serves the package, as it is, so the base URL carries no path of its own.
 */
export const GRPC_TRANSPORT = new InjectionToken<Transport>('grpc-transport', {
  providedIn: 'root',
  factory: () =>
    createGrpcWebTransport({
      baseUrl: inject(SERVER_URL),
      interceptors: isPlatformBrowser(inject(PLATFORM_ID))
        ? [traceInterceptor, signInWhenUnauthenticated]
        : [traceInterceptor],
    }),
});
