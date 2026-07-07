import './instrumentation';

import {
  AngularNodeAppEngine,
  createNodeRequestHandler,
  isMainModule,
  writeResponseToNodeResponse,
} from '@angular/ssr/node';
import express from 'express';
import { join } from 'node:path';

const browserDistFolder = join(import.meta.dirname, '../browser');

const app = express();
// Requests reach this process through Envoy, the trusted edge that sets
// `x-forwarded-*` (and `x-client`). Trust those proxy headers so SSR builds
// the request URL from the original host rather than the internal one.
const angularApp = new AngularNodeAppEngine({
  trustProxyHeaders: [
    'x-forwarded-host',
    'x-forwarded-proto',
    'x-forwarded-port',
    'x-forwarded-prefix',
  ],
});

/**
 * Example Express Rest API endpoints can be defined here.
 * Uncomment and define endpoints as necessary.
 *
 * Example:
 * ```ts
 * app.get('/api/{*splat}', (req, res) => {
 *   // Handle API request
 * });
 * ```
 */

/**
 * Serve static files from /browser. The client bundle is identical for everyone and carries no
 * identity, so it is served publicly (and cacheably) ahead of the auth gate below.
 */
app.use(
  express.static(browserDistFolder, {
    maxAge: '1y',
    index: false,
    redirect: false,
  }),
);

/**
 * Protected-app gate (guide §7). The admin app is entirely internal — every rendered page requires
 * an authenticated identity. The edge only annotates identity, so the SSR host itself enforces it:
 * anonymous requests (no `x-user-id` from ext_authz) are bounced to the BFF sign-in server-side —
 * no flash of protected chrome — and personalized responses are never cached.
 */
app.use((req, res, next) => {
  res.setHeader('Cache-Control', 'private, no-store');
  if (!req.headers['x-user-id']) {
    res.redirect(302, `/signin?returnUrl=${encodeURIComponent(req.originalUrl)}`);
    return;
  }
  next();
});

/**
 * Handle all other requests by rendering the Angular application.
 */
app.use((req, res, next) => {
  angularApp
    .handle(req)
    .then((response) =>
      response ? writeResponseToNodeResponse(response, res) : next(),
    )
    .catch(next);
});

/**
 * Start the server if this module is the main entry point, or it is ran via PM2.
 * The server listens on the port defined by the `PORT` environment variable, or defaults to 4000.
 */
if (isMainModule(import.meta.url) || process.env['pm_id']) {
  const port = process.env['PORT'] || 4000;
  app.listen(port, (error) => {
    if (error) {
      throw error;
    }

    console.log(`Node Express server listening on http://localhost:${port}`);
  });
}

/**
 * Request handler used by the Angular CLI (for dev-server and during build) or Firebase Cloud Functions.
 */
export const reqHandler = createNodeRequestHandler(app);
