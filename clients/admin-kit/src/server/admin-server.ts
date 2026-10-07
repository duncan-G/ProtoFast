import { AngularNodeAppEngine, writeResponseToNodeResponse } from '@angular/ssr/node';
import express from 'express';
import { admits, FORBIDDEN_PATH, SIGN_IN_PATH } from '../access/console-access';
import { parseRoles } from '../access/auth-identity';
import { ssrRequestContext, ssrTraceMiddleware } from '../telemetry/ssr';
import { consoleRolesFromEnv } from './console-roles';

/** Envoy sets it when a console path was requested without that console's role. */
const CONSOLE_DENIED_HEADER = 'x-console-denied';

export interface AdminServerOptions {
  browserDistFolder: string;
  /** The console's base href, e.g. `/theplot/`. */
  basePath?: string;
  /** Rendered without a session. */
  publicPaths?: readonly string[];
}

/**
 * The console's SSR server: static bundles, then the sign-in and role gate, then Angular. The
 * gate is for the operator's benefit — the api checks the role on every call.
 */
export function createAdminServer(options: AdminServerOptions): express.Express {
  const basePath = options.basePath ?? '/';
  const publicPaths = new Set(options.publicPaths ?? []);
  const required = consoleRolesFromEnv();

  const app = express();
  const angularApp = new AngularNodeAppEngine({
    // Browser-facing hostnames, injected by the deployment; Angular's SSRF guard rejects others.
    allowedHosts: (process.env['NG_ALLOWED_HOSTS'] ?? '')
      .split(',')
      .map((host) => host.trim())
      .filter((host) => host.length > 0),
    trustProxyHeaders: [
      'x-forwarded-host',
      'x-forwarded-proto',
      'x-forwarded-port',
      'x-forwarded-prefix',
    ],
  });

  app.use(
    basePath,
    express.static(options.browserDistFolder, {
      maxAge: '1y',
      index: false,
      redirect: false,
    }),
  );

  app.use((req, res, next) => {
    res.setHeader('Cache-Control', 'private, no-store');
    if (publicPaths.has(req.path)) {
      next();
      return;
    }

    if (required.length === 0) {
      res.status(500).send('ADMIN_CONSOLE_ROLES is not set for this console.');
      return;
    }

    if (!req.headers['x-user-id']) {
      res.redirect(302, `${SIGN_IN_PATH}?returnUrl=${encodeURIComponent(req.originalUrl)}`);
      return;
    }

    const roles = parseRoles(header(req.headers['x-roles']));
    if (req.headers[CONSOLE_DENIED_HEADER] || !admits(required, roles)) {
      res.redirect(302, FORBIDDEN_PATH);
      return;
    }

    next();
  });

  app.use(ssrTraceMiddleware);
  app.use((req, res, next) => {
    angularApp
      .handle(req, ssrRequestContext(res))
      .then((response) => (response ? writeResponseToNodeResponse(response, res) : next()))
      .catch(next);
  });

  return app;
}

function header(value: string | string[] | undefined): string | undefined {
  return Array.isArray(value) ? value.join(',') : value;
}
