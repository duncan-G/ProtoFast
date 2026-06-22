/**
 * Unified SSR host: serves every client's Angular SSR bundle from a single
 * Node process. Envoy tags each request with an `x-client` header (derived
 * from the subdomain in publish mode, or the per-client listener in dev-host
 * mode); unknown or missing values fall back to DEFAULT_CLIENT.
 *
 * Clients are NOT baked into this image. The entrypoint (entrypoint.sh) pulls
 * each pinned client's built assets from S3 into ASSETS_DIR/<name>/ before this
 * process starts, so the set of clients is discovered at runtime from the
 * `CLIENTS` env var (comma-separated) rather than a hard-coded loader map. Each
 * client's built server bundle exports `reqHandler` and only calls listen()
 * when run as the main module, so importing them here is safe.
 */
import express from 'express';
import { pathToFileURL } from 'node:url';

const assetsDir = process.env['ASSETS_DIR'] || '/assets';

const clientNames = (process.env['CLIENTS'] || '')
  .split(',')
  .map((name) => name.trim())
  .filter(Boolean);

if (clientNames.length === 0) {
  throw new Error('CLIENTS env var is empty; no clients to serve');
}

const defaultClient = process.env['DEFAULT_CLIENT'] || clientNames[0];

const handlers = new Map();
for (const name of clientNames) {
  // Each client's assets were synced to <assetsDir>/<name>/{server,browser}/
  // by the entrypoint; import its self-contained server bundle by absolute path.
  const entry = pathToFileURL(`${assetsDir}/${name}/server/server.mjs`).href;
  const { reqHandler } = await import(entry);
  handlers.set(name, reqHandler);
}

if (!handlers.has(defaultClient)) {
  throw new Error(`DEFAULT_CLIENT "${defaultClient}" is not in CLIENTS`);
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
