// The clients component: one Node server for the Angular SSR apps named in CLIENTS. A request
// goes to the client named for its host (HOST_<NAME>), any other to DEFAULT_CLIENT; GET
// /healthz is answered here. The image runs it as `node host/server.mjs`; from clients/, after
// each app's `npm run build`, so can you.
import os from 'node:os';

// Telemetry starts first, before the apps load. ProtoStrap and `aspire run` set the endpoint;
// without it nothing is exported.
const telemetry = process.env.OTEL_EXPORTER_OTLP_ENDPOINT
  ? await import('./telemetry.mjs')
  : undefined;

const { allowedHosts, createHost, createLogger, loadConfig, routeConsole } =
  await import('./host.mjs');

const log = createLogger({
  component: process.env.PROTOSTRAP_COMPONENT || 'clients',
  replica: process.env.PROTOSTRAP_REPLICA || os.hostname(),
  release: process.env.PROTOSTRAP_RELEASE || 'local',
});
routeConsole(log);

function numberFromEnv(name, fallback) {
  const raw = process.env[name];
  const value = raw === undefined || raw === '' ? fallback : Number(raw);
  if (!Number.isFinite(value) || value < 0) {
    throw new Error(`${name} must be a number, not "${raw}"`);
  }
  return value;
}

let port, drainSeconds, config;
try {
  port = numberFromEnv('PORT', 4000);
  drainSeconds = numberFromEnv('DRAIN_SECONDS', process.env.PROTOSTRAP_REPLICA ? 10 : 0);
  config = loadConfig(process.env);
} catch (error) {
  log('error', error.message);
  process.exit(1);
}

// Angular reads NG_ALLOWED_HOSTS when a bundle is imported, and renders for no other host.
process.env.NG_ALLOWED_HOSTS = allowedHosts(process.env).join(',');

// The host owns OpenTelemetry: it starts the SDK above and flushes it on the way out. Each
// app's own server instrumentation starts one only when this flag is unset, as under `ng serve`.
globalThis.__nodeOtelSdkStarted = true;

// Each app's server bundle exports reqHandler and listens only when run on its own. The bundles
// sit where each app's `npm run build` leaves them, in the image as in clients/.
const clients = new Map(
  await Promise.all(
    config.clients.map(async (name) => {
      const bundle = new URL(`../${name}/dist/${name}/server/server.mjs`, import.meta.url);
      const { reqHandler } = await import(bundle.href);
      return [name, reqHandler];
    }),
  ),
);

const host = createHost({
  clients,
  defaultClient: config.defaultClient,
  hosts: config.hosts,
  component: process.env.PROTOSTRAP_COMPONENT,
  drainSeconds,
  log,
  onStop: () => telemetry?.sdk.shutdown(),
});

host.server.listen(port, () => {
  log('info', 'listening', {
    port,
    clients: config.clients,
    defaultClient: config.defaultClient,
    hosts: Object.fromEntries(config.hosts),
  });
});

process.on('SIGTERM', () => host.shutdown('SIGTERM'));
process.on('SIGINT', () => host.shutdown('SIGINT'));
