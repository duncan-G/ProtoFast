// The clients host's request handling, apart from what it loads, so tests can give it stub
// clients: which clients to load, picking one by the request's host, readiness, draining, and
// the admin consoles' Content-Security-Policy.
import http from 'node:http';
import { format } from 'node:util';

/**
 * The admin consoles' Content-Security-Policy. Console code is written by each app's agent;
 * this header is not (docs/design/admin-consoles.md): scripts and styles only from the host,
 * and connect-src 'self', so a console can't send data to a third party.
 */
export const ADMIN_CSP =
  "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; " +
  "img-src 'self' data:; font-src 'self' data:; connect-src 'self'; object-src 'none'; " +
  "base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

/**
 * The app consoles the platform console stands in front of. Each is served on the admin host
 * under `/<path>/` by its own process; `/<path>` alone reaches the platform console's process,
 * which redirects it to `/<path>/`. Adding a console adds its path here.
 */
export const CONSOLE_PATHS = ['theplot'];

/** The platform admin console: the one that serves the admin host outside the app consoles. */
export const PLATFORM_CONSOLE = 'admin';

/**
 * isConsole is whether a client is an admin console, the platform's or an app's
 * (`admin-<app>`). Only consoles get the Content-Security-Policy.
 * @param {string} name
 */
export function isConsole(name) {
  return name === PLATFORM_CONSOLE || name.startsWith(`${PLATFORM_CONSOLE}-`);
}

/**
 * isAppConsole is whether a client is an app's console, which runs in a process of its own.
 * @param {string} name
 */
export function isAppConsole(name) {
  return name.startsWith(`${PLATFORM_CONSOLE}-`);
}

/**
 * hostnameOf is a Host header's hostname, in lower case and without its port.
 * @param {string | undefined} host
 */
export function hostnameOf(host) {
  const value = (host ?? '').trim().toLowerCase();
  if (value.startsWith('[')) {
    return value.slice(0, value.indexOf(']') + 1); // An IPv6 address, such as [::1]:4000.
  }
  return value.replace(/:\d*$/, '');
}

/**
 * hostVariable is the variable that holds a client's hostname: `HOST_<NAME>`, upper case, with
 * `-` as `_`, as ProtoStrap names hosts.
 * @param {string} name
 */
export function hostVariable(name) {
  return `HOST_${name.toUpperCase().replaceAll('-', '_')}`;
}

/**
 * @typedef {object} HostConfig
 * @property {string[]} clients The clients to load, from CLIENTS.
 * @property {string} defaultClient The client for a host no client is named for.
 * @property {Map<string, string>} hosts Hostname to client, for the clients with a HOST_<NAME>.
 */

/**
 * loadConfig reads which clients to load and which host each answers. A client is served on
 * the host named for it (client `protofast` on `HOST_PROTOFAST`); any other host gets
 * DEFAULT_CLIENT. It fails, naming the variable, on a configuration it can't serve.
 * @param {NodeJS.ProcessEnv} env
 * @returns {HostConfig}
 */
export function loadConfig(env) {
  const clients = [
    ...new Set(
      (env.CLIENTS ?? '')
        .split(',')
        .map((name) => name.trim())
        .filter(Boolean),
    ),
  ];
  if (clients.length === 0) {
    throw new Error('CLIENTS is empty: name the clients to serve, such as "admin,protofast"');
  }
  for (const name of clients) {
    if (!/^[a-z][a-z0-9-]*$/.test(name)) {
      throw new Error(`CLIENTS: "${name}" is not a client name`);
    }
  }
  // An app console's code is written by that app's agent, so it shares a process with no
  // other client (docs/design/admin-consoles.md §2).
  const consoles = clients.filter(isAppConsole);
  if (consoles.length > 0 && clients.length > 1) {
    throw new Error(
      `CLIENTS: an app console (${consoles.join(', ')}) runs in a process of its own, ` +
        `but CLIENTS is "${clients.join(',')}"`,
    );
  }
  const defaultClient = env.DEFAULT_CLIENT?.trim() || clients[0];
  if (!clients.includes(defaultClient)) {
    throw new Error(`DEFAULT_CLIENT "${defaultClient}" is not in CLIENTS (${clients.join(',')})`);
  }
  const hosts = new Map();
  for (const name of clients) {
    const hostname = hostnameOf(env[hostVariable(name)]);
    if (hostname) {
      hosts.set(hostname, name);
    }
  }
  return { clients, defaultClient, hosts };
}

/**
 * allowedHosts is what Angular's server-side request forgery guard is to accept: every
 * HOST_<NAME> (an app console answers on HOST_ADMIN, which isn't named for it) and any already
 * in NG_ALLOWED_HOSTS. Angular reads NG_ALLOWED_HOSTS when a client's bundle is imported.
 * @param {NodeJS.ProcessEnv} env
 */
export function allowedHosts(env) {
  const hosts = (env.NG_ALLOWED_HOSTS ?? '').split(',').map((host) => host.trim());
  for (const [name, value] of Object.entries(env)) {
    if (/^HOST_[A-Z0-9_]+$/.test(name) && /^[A-Za-z0-9.-]+$/.test(value?.trim() ?? '')) {
      hosts.push(value.trim().toLowerCase());
    }
  }
  return [...new Set(hosts.filter(Boolean))];
}

/**
 * createLogger writes one JSON object per line on stdout, naming the replica.
 * @param {{ component?: string, replica?: string, release?: string }} labels
 * @param {(line: string) => void} [write]
 */
export function createLogger(labels, write = (line) => process.stdout.write(line)) {
  return (level, msg, fields = {}) => {
    write(
      `${JSON.stringify({ time: new Date().toISOString(), level, msg, ...labels, ...fields })}\n`,
    );
  };
}

/**
 * routeConsole sends console output (Angular's and Express's own messages, such as a render
 * error) through the logger, so every line on stdout is JSON.
 * @param {ReturnType<typeof createLogger>} log
 */
export function routeConsole(log) {
  const levels = { debug: 'debug', log: 'info', info: 'info', warn: 'warn', error: 'error' };
  for (const [method, level] of Object.entries(levels)) {
    console[method] = (...args) => log(level, format(...args));
  }
}

/**
 * @typedef {(req: http.IncomingMessage, res: http.ServerResponse) => void} Handler
 * @typedef {object} HostOptions
 * @property {Map<string, Handler>} clients Each loaded client's request handler, by name.
 * @property {string} defaultClient The client for a host no client is named for.
 * @property {Map<string, string>} [hosts] Hostname to client.
 * @property {string[]} [consolePaths] The app consoles' paths on the admin host.
 * @property {string} [component] PROTOSTRAP_COMPONENT: /healthz answers only for this name.
 * @property {number} [drainSeconds] How long to keep serving after SIGTERM.
 * @property {ReturnType<typeof createLogger>} log
 * @property {() => Promise<void> | void} [onStop] Runs once the server has closed.
 * @property {(code: number) => void} [exit]
 */

/**
 * createHost makes the server the clients share. It answers GET /healthz itself and gives
 * every other request to the client named for its host, or to the default client.
 * @param {HostOptions} options
 */
export function createHost({
  clients,
  defaultClient,
  hosts = new Map(),
  consolePaths = CONSOLE_PATHS,
  component,
  drainSeconds = 10,
  log,
  onStop,
  exit = (code) => process.exit(code),
}) {
  if (!clients.has(defaultClient)) {
    throw new Error(`the default client "${defaultClient}" is not loaded`);
  }
  const consoleRedirects = new Set(consolePaths.map((path) => `/${path}`));
  let draining = false;

  const server = http.createServer((req, res) => {
    // While draining, close each connection after its response, so the edge's close too.
    if (draining) {
      res.setHeader('Connection', 'close');
    }
    const [path, query] = splitUrl(req.url);
    if (path === '/healthz') {
      healthz(req, res);
      return;
    }
    const name = clientFor(req.headers.host);
    if (isConsole(name)) {
      res.setHeader('Content-Security-Policy', ADMIN_CSP);
    }
    // `/theplot` alone isn't under the console's `/theplot/`, so the platform console's process
    // gets it: send the browser on to the console.
    if (
      name === PLATFORM_CONSOLE &&
      consoleRedirects.has(path) &&
      (req.method === 'GET' || req.method === 'HEAD')
    ) {
      res.writeHead(301, { Location: `${path}/${query}` }).end();
      return;
    }
    try {
      clients.get(name)(req, res);
    } catch (error) {
      log('error', 'request failed', {
        client: name,
        error: error instanceof Error ? error.message : String(error),
      });
      if (!res.headersSent) {
        res.writeHead(500, { 'Content-Type': 'text/plain' });
      }
      res.end();
    }
  });
  // Longer than the edge keeps an idle connection (60 s): Node's default, 5 s, would close
  // connections Envoy is about to reuse.
  server.keepAliveTimeout = 65_000;
  server.headersTimeout = 66_000;

  function clientFor(host) {
    const name = hosts.get(hostnameOf(host));
    return name && clients.has(name) ? name : defaultClient;
  }

  // Ready only when asked for this component by name, and not draining: overlay addresses are
  // reused by other services, so a check that reaches the wrong replica must fail. Outside
  // ProtoStrap (no PROTOSTRAP_COMPONENT) the name isn't checked.
  function healthz(req, res) {
    if (req.method !== 'GET' && req.method !== 'HEAD') {
      res.writeHead(405, { Allow: 'GET, HEAD' }).end();
      return;
    }
    const ready = !draining && (!component || hostnameOf(req.headers.host) === component);
    res.writeHead(ready ? 200 : 503, {
      'Content-Type': 'application/json',
      'Cache-Control': 'no-store',
    });
    res.end(JSON.stringify({ ready }));
  }

  // On SIGTERM: fail readiness at once, keep serving for drainSeconds while the edge notices,
  // then stop taking connections, let requests in flight finish, flush telemetry and exit. A
  // second signal exits at once.
  function shutdown(signal) {
    if (draining) {
      exit(0);
      return;
    }
    draining = true;
    log('info', 'draining', { signal, seconds: drainSeconds });
    setTimeout(() => {
      server.close(async () => {
        try {
          await onStop?.();
        } catch (error) {
          log('warn', 'flushing telemetry failed', {
            error: error instanceof Error ? error.message : String(error),
          });
        }
        log('info', 'stopped');
        exit(0);
      });
      server.closeIdleConnections();
      setTimeout(() => {
        log('warn', 'requests still open after 5 s, exiting');
        exit(0);
      }, 5000).unref();
    }, drainSeconds * 1000).unref();
  }

  return {
    server,
    shutdown,
    get draining() {
      return draining;
    },
  };
}

/** splitUrl is a request URL's path and its query string, `?` included. */
function splitUrl(url = '/') {
  const at = url.indexOf('?');
  return at < 0 ? [url, ''] : [url.slice(0, at), url.slice(at)];
}
