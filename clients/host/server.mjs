/**
 * Unified SSR host: serves every client's Angular SSR bundle from a single
 * Node process. Envoy tags each request with an `x-client` header (derived
 * from the subdomain in publish mode, or the per-client listener in dev-host
 * mode); unknown or missing values fall back to DEFAULT_CLIENT.
 *
 * Each client's built server bundle exports `reqHandler` and only calls
 * listen() when run as the main module, so importing them here is safe.
 */
import express from 'express';

// Add new clients here (the add-angular-client skill does this).
const clientLoaders = {
  admin: () => import('./admin/dist/admin/server/server.mjs'),
};

const defaultClient =
  process.env['DEFAULT_CLIENT'] || Object.keys(clientLoaders)[0];

const handlers = new Map();
for (const [name, load] of Object.entries(clientLoaders)) {
  const { reqHandler } = await load();
  handlers.set(name, reqHandler);
}

if (!handlers.has(defaultClient)) {
  throw new Error(`DEFAULT_CLIENT "${defaultClient}" is not a known client`);
}

const app = express();

app.use((req, res, next) => {
  const requested = req.headers['x-client'];
  const handler =
    (typeof requested === 'string' && handlers.get(requested)) ||
    handlers.get(defaultClient);
  handler(req, res, next);
});

const port = process.env['PORT'] || 4000;
app.listen(port, (error) => {
  if (error) {
    throw error;
  }

  console.log(
    `Unified SSR host listening on http://localhost:${port} ` +
      `(clients: ${[...handlers.keys()].join(', ')}; default: ${defaultClient})`,
  );
});
