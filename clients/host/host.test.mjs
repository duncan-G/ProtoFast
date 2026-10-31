// Tests for the clients host with stub clients, so no build is needed: `node --test` in host/.
import assert from 'node:assert/strict';
import http from 'node:http';
import { after, describe, it } from 'node:test';
import {
  ADMIN_CSP,
  allowedHosts,
  createHost,
  createLogger,
  hostnameOf,
  hostVariable,
  isConsole,
  loadConfig,
} from './host.mjs';

/** A stub client that answers with its own name. */
const stub = (name) => (req, res) => {
  res.writeHead(200, { 'Content-Type': 'text/plain' });
  res.end(name);
};

const quiet = () => {};

/** The production layout: three clients, each on its own host, protofast the default. */
const env = {
  CLIENTS: 'admin,protofast,theplot',
  DEFAULT_CLIENT: 'protofast',
  HOST_PROTOFAST: 'protofast.dev.localhost',
  HOST_THEPLOT: 'theplot.dev.localhost',
  HOST_ADMIN: 'admin.dev.localhost',
  HOST_AUTH: 'auth.dev.localhost',
};

/** The theplot console's process: one client, which answers every host. */
const consoleEnv = {
  CLIENTS: 'admin-theplot',
  DEFAULT_CLIENT: 'admin-theplot',
  HOST_PROTOFAST: 'protofast.dev.localhost',
  HOST_THEPLOT: 'theplot.dev.localhost',
  HOST_ADMIN: 'admin.dev.localhost',
};

/** start runs a host on a free port with a stub for each client in the environment's CLIENTS. */
async function start(environment = env, options = {}) {
  const config = loadConfig(environment);
  const exits = [];
  let stopped = 0;
  const host = createHost({
    clients: new Map(config.clients.map((name) => [name, stub(name)])),
    defaultClient: config.defaultClient,
    hosts: config.hosts,
    log: quiet,
    exit: (code) => exits.push(code),
    onStop: () => {
      stopped++;
    },
    ...options,
  });
  await new Promise((resolve) => host.server.listen(0, '127.0.0.1', resolve));
  const { port } = host.server.address();
  return {
    host,
    exits,
    stopped: () => stopped,
    request: (path, hostHeader, method = 'GET') => request(port, path, hostHeader, method),
    close: () => new Promise((resolve) => host.server.close(() => resolve())),
  };
}

function request(port, path, hostHeader, method) {
  return new Promise((resolve, reject) => {
    const req = http.request(
      {
        host: '127.0.0.1',
        port,
        path,
        method,
        agent: false,
        headers: hostHeader === undefined ? {} : { host: hostHeader },
      },
      (res) => {
        let body = '';
        res.setEncoding('utf8');
        res.on('data', (chunk) => (body += chunk));
        res.on('end', () => resolve({ status: res.statusCode, headers: res.headers, body }));
      },
    );
    req.on('error', reject);
    req.end();
  });
}

const waitFor = async (condition, ms = 3000) => {
  const deadline = Date.now() + ms;
  while (!condition()) {
    if (Date.now() > deadline) throw new Error('timed out');
    await new Promise((r) => setTimeout(r, 10));
  }
};

describe('picking the client by host', () => {
  const running = start();
  after(async () => (await running).close());

  it('gives each client the requests for its own host, with or without a port', async () => {
    const { request } = await running;
    assert.equal((await request('/', 'protofast.dev.localhost:20000')).body, 'protofast');
    assert.equal((await request('/', 'theplot.dev.localhost:20000')).body, 'theplot');
    assert.equal((await request('/', 'admin.dev.localhost:20000')).body, 'admin');
    assert.equal((await request('/app/x?y=1', 'theplot.dev.localhost')).body, 'theplot');
  });

  it('compares hostnames without regard to case', async () => {
    const { request } = await running;
    assert.equal((await request('/', 'ThePlot.Dev.LocalHost:20000')).body, 'theplot');
  });

  it('gives DEFAULT_CLIENT every other host', async () => {
    const { request } = await running;
    for (const host of [
      'auth.dev.localhost', // a HOST_ for a name that isn't a client
      'admin.dev.localhost.example.com',
      'clients:4000',
      'localhost:4000',
      '',
    ]) {
      assert.equal((await request('/', host)).body, 'protofast', host);
    }
  });

  it('serves static paths from the same client as the page', async () => {
    const { request } = await running;
    assert.equal((await request('/main-ABC123.js', 'theplot.dev.localhost')).body, 'theplot');
    assert.equal((await request('/assets/x.svg', 'admin.dev.localhost')).body, 'admin');
  });
});

describe('a client without a HOST_ variable', () => {
  const running = start({
    CLIENTS: 'admin,protofast',
    DEFAULT_CLIENT: 'admin',
    HOST_PROTOFAST: 'protofast.example.com',
  });
  after(async () => (await running).close());

  it('is reached only as the default', async () => {
    const { request } = await running;
    assert.equal((await request('/', 'protofast.example.com')).body, 'protofast');
    assert.equal((await request('/', 'admin.example.com')).body, 'admin');
  });
});

describe('a host named for a client this process does not serve', () => {
  const running = start({ ...env, CLIENTS: 'protofast,theplot' });
  after(async () => (await running).close());

  it('goes to DEFAULT_CLIENT', async () => {
    const { request } = await running;
    assert.equal((await request('/', 'admin.dev.localhost')).body, 'protofast');
  });
});

describe("the theplot console's process", () => {
  const running = start(consoleEnv);
  after(async () => (await running).close());

  it('serves every request it gets', async () => {
    const { request } = await running;
    assert.equal((await request('/theplot/', 'admin.dev.localhost:20000')).body, 'admin-theplot');
    assert.equal((await request('/theplot/runs', 'admin.dev.localhost')).body, 'admin-theplot');
    assert.equal((await request('/', 'protofast.dev.localhost')).body, 'admin-theplot');
  });

  it('does not redirect /theplot itself: only the platform console does', async () => {
    const { request } = await running;
    const res = await request('/theplot', 'admin.dev.localhost');
    assert.equal(res.status, 200);
    assert.equal(res.body, 'admin-theplot');
  });

  it('sets the admin Content-Security-Policy', async () => {
    const { request } = await running;
    const res = await request('/theplot/', 'admin.dev.localhost');
    assert.equal(res.headers['content-security-policy'], ADMIN_CSP);
  });
});

describe('the Content-Security-Policy', () => {
  const running = start();
  after(async () => (await running).close());

  it("is on the platform console's responses, assets included", async () => {
    const { request } = await running;
    for (const path of ['/', '/app/platform', '/main-ABC123.js', '/forbidden']) {
      const res = await request(path, 'admin.dev.localhost');
      assert.equal(res.body, 'admin');
      assert.equal(res.headers['content-security-policy'], ADMIN_CSP, path);
    }
  });

  it("is not on the product clients' responses", async () => {
    const { request } = await running;
    for (const host of ['protofast.dev.localhost', 'theplot.dev.localhost', 'clients:4000']) {
      const res = await request('/', host);
      assert.equal(res.headers['content-security-policy'], undefined, host);
    }
  });

  it('is not on /healthz, which no client answers', async () => {
    const { request } = await running;
    const res = await request('/healthz', 'admin.dev.localhost');
    assert.equal(res.headers['content-security-policy'], undefined);
  });

  it("pins connect-src to the page's own origin", () => {
    assert.match(ADMIN_CSP, /(^|; )connect-src 'self'(;|$)/);
    assert.match(ADMIN_CSP, /(^|; )frame-ancestors 'none'(;|$)/);
    assert.match(ADMIN_CSP, /(^|; )default-src 'self'(;|$)/);
  });

  it('is for the admin consoles only', () => {
    assert.equal(isConsole('admin'), true);
    assert.equal(isConsole('admin-theplot'), true);
    assert.equal(isConsole('protofast'), false);
    assert.equal(isConsole('theplot'), false);
    assert.equal(isConsole('administrator'), false);
  });
});

describe('/theplot on the admin host', () => {
  const running = start();
  after(async () => (await running).close());

  it('redirects to /theplot/, keeping the query string', async () => {
    const { request } = await running;
    let res = await request('/theplot', 'admin.dev.localhost:20000');
    assert.equal(res.status, 301);
    assert.equal(res.headers.location, '/theplot/');
    res = await request('/theplot?tab=runs', 'admin.dev.localhost');
    assert.equal(res.status, 301);
    assert.equal(res.headers.location, '/theplot/?tab=runs');
    assert.equal((await request('/theplot', 'admin.dev.localhost', 'HEAD')).status, 301);
  });

  it('leaves /theplot/ and longer paths to the client', async () => {
    const { request } = await running;
    // The edge sends /theplot/ to the console's own process; one that arrives here anyway is
    // the platform console's to answer.
    assert.equal((await request('/theplot/', 'admin.dev.localhost')).body, 'admin');
    assert.equal((await request('/theplotx', 'admin.dev.localhost')).body, 'admin');
  });

  it('leaves other methods to the client', async () => {
    const { request } = await running;
    assert.equal((await request('/theplot', 'admin.dev.localhost', 'POST')).body, 'admin');
  });

  it('is not redirected on the product hosts', async () => {
    const { request } = await running;
    for (const host of ['protofast.dev.localhost', 'theplot.dev.localhost', 'clients']) {
      const res = await request('/theplot', host);
      assert.equal(res.status, 200, host);
      assert.notEqual(res.body, 'admin', host);
    }
  });
});

describe('a process without admin', () => {
  const running = start({ ...env, CLIENTS: 'protofast,theplot' });
  after(async () => (await running).close());

  it('does not redirect /theplot, even for HOST_ADMIN', async () => {
    const { request } = await running;
    const res = await request('/theplot', 'admin.dev.localhost');
    assert.equal(res.status, 200);
    assert.equal(res.body, 'protofast');
  });
});

describe('/healthz under ProtoStrap', () => {
  const running = start(env, { component: 'clients' });
  after(async () => (await running).close());

  it('answers 200 when asked for its own name, with or without a port', async () => {
    const { request } = await running;
    for (const host of ['clients', 'clients:4000']) {
      const res = await request('/healthz', host);
      assert.equal(res.status, 200, host);
      assert.deepEqual(JSON.parse(res.body), { ready: true });
      assert.equal(res.headers['cache-control'], 'no-store');
    }
  });

  it('answers 503 when asked for another name', async () => {
    const { request } = await running;
    for (const host of [
      'api:8080',
      'clients-admin-theplot',
      'protofast.dev.localhost:20000',
      'admin.dev.localhost',
      'clients.example.com',
    ]) {
      assert.equal((await request('/healthz', host)).status, 503, host);
    }
  });

  it('answers itself, never through a client, query string or not', async () => {
    const { request } = await running;
    const res = await request('/healthz?probe=1', 'admin.dev.localhost');
    assert.equal(res.status, 503);
    assert.equal(res.headers['content-type'], 'application/json');
    assert.equal((await request('/healthz?probe=1', 'clients')).status, 200);
  });

  it('answers HEAD, and refuses other methods', async () => {
    const { request } = await running;
    assert.equal((await request('/healthz', 'clients', 'HEAD')).status, 200);
    assert.equal((await request('/healthz', 'clients', 'POST')).status, 405);
  });

  it('leaves other paths to the clients', async () => {
    const { request } = await running;
    assert.equal((await request('/healthz/more', 'clients')).body, 'protofast');
    assert.equal((await request('/healthzz', 'admin.dev.localhost')).body, 'admin');
  });
});

describe("/healthz in the console's process", () => {
  const running = start(consoleEnv, { component: 'clients-admin-theplot' });
  after(async () => (await running).close());

  it('answers for its own component name only', async () => {
    const { request } = await running;
    assert.equal((await request('/healthz', 'clients-admin-theplot:4000')).status, 200);
    assert.equal((await request('/healthz', 'clients:4000')).status, 503);
  });
});

describe('/healthz outside ProtoStrap', () => {
  const running = start(env, { component: undefined });
  after(async () => (await running).close());

  it('answers 200 for any host when PROTOSTRAP_COMPONENT is unset', async () => {
    const { request } = await running;
    for (const host of ['localhost:4000', 'protofast.dev.localhost:20000', 'anything']) {
      assert.equal((await request('/healthz', host)).status, 200, host);
    }
  });
});

describe('keep-alive', () => {
  it('outlasts the edge idle timeout of 60 s', async () => {
    const { host, close } = await start();
    assert.equal(host.server.keepAliveTimeout, 65_000);
    assert.equal(host.server.headersTimeout, 66_000);
    await close();
  });
});

describe('draining on SIGTERM', () => {
  it('fails readiness at once, keeps serving, then closes, flushes and exits', async () => {
    const { host, request, exits, stopped } = await start(env, {
      component: 'clients',
      drainSeconds: 0.3,
    });
    host.shutdown('SIGTERM');
    assert.equal(host.draining, true);

    const health = await request('/healthz', 'clients');
    assert.equal(health.status, 503);
    assert.deepEqual(JSON.parse(health.body), { ready: false });

    // Pages are still served while the edge notices, each closing its connection.
    const page = await request('/', 'theplot.dev.localhost');
    assert.equal(page.body, 'theplot');
    assert.equal(page.headers.connection, 'close');
    assert.deepEqual(exits, []);

    await waitFor(() => exits.length > 0);
    assert.deepEqual(exits, [0]);
    assert.equal(stopped(), 1);
    assert.equal(host.server.listening, false);
  });

  it('fails readiness while draining when PROTOSTRAP_COMPONENT is unset too', async () => {
    const { host, request, exits } = await start(env, { component: undefined, drainSeconds: 0.2 });
    assert.equal((await request('/healthz', 'localhost')).status, 200);
    host.shutdown('SIGTERM');
    assert.equal((await request('/healthz', 'localhost')).status, 503);
    await waitFor(() => exits.length > 0);
  });

  it('exits even when flushing telemetry fails', async () => {
    const lines = [];
    const log = createLogger({}, (line) => lines.push(JSON.parse(line)));
    const { host, exits } = await start(env, {
      drainSeconds: 0,
      log,
      onStop: () => Promise.reject(new Error('collector down')),
    });
    host.shutdown('SIGTERM');
    await waitFor(() => exits.length > 0);
    assert.deepEqual(exits, [0]);
    assert.ok(lines.some((l) => l.level === 'warn' && l.error === 'collector down'));
  });

  it('exits at once on a second signal', async () => {
    const { host, exits, close } = await start(env, { drainSeconds: 30 });
    host.shutdown('SIGTERM');
    assert.deepEqual(exits, []);
    host.shutdown('SIGTERM');
    assert.deepEqual(exits, [0]);
    host.server.closeAllConnections();
    await close();
  });
});

describe('a client that throws', () => {
  it('answers 500 and logs the error as JSON, naming the client', async () => {
    const lines = [];
    const log = createLogger({ component: 'clients' }, (line) => lines.push(line));
    const { request, close } = await start(env, {
      log,
      clients: new Map([
        ['protofast', stub('protofast')],
        [
          'theplot',
          () => {
            throw new Error('boom');
          },
        ],
      ]),
    });
    assert.equal((await request('/', 'theplot.dev.localhost')).status, 500);
    const entry = JSON.parse(lines[0]);
    assert.equal(entry.level, 'error');
    assert.equal(entry.error, 'boom');
    assert.equal(entry.client, 'theplot');
    await close();
  });
});

describe('createHost', () => {
  it('refuses a default client it was not given', () => {
    assert.throws(
      () =>
        createHost({
          clients: new Map([['admin', stub('admin')]]),
          defaultClient: 'protofast',
          log: quiet,
        }),
      /protofast/,
    );
  });
});

describe('loadConfig', () => {
  it("reads CLIENTS, DEFAULT_CLIENT and each client's HOST_ variable", () => {
    const config = loadConfig(env);
    assert.deepEqual(config.clients, ['admin', 'protofast', 'theplot']);
    assert.equal(config.defaultClient, 'protofast');
    assert.deepEqual(Object.fromEntries(config.hosts), {
      'admin.dev.localhost': 'admin',
      'protofast.dev.localhost': 'protofast',
      'theplot.dev.localhost': 'theplot',
    });
  });

  it('trims names, drops empty ones and duplicates', () => {
    const config = loadConfig({ CLIENTS: ' admin , protofast,,admin ' });
    assert.deepEqual(config.clients, ['admin', 'protofast']);
  });

  it('defaults DEFAULT_CLIENT to the first client', () => {
    assert.equal(loadConfig({ CLIENTS: 'theplot,admin' }).defaultClient, 'theplot');
  });

  it('reads a hostname with a port or in upper case as the bare hostname', () => {
    const config = loadConfig({ CLIENTS: 'admin', HOST_ADMIN: 'Admin.Example.com:443' });
    assert.deepEqual([...config.hosts.keys()], ['admin.example.com']);
  });

  it("names an app console's host variable with _ for -", () => {
    assert.equal(hostVariable('admin-theplot'), 'HOST_ADMIN_THEPLOT');
    const config = loadConfig({
      CLIENTS: 'admin-theplot',
      HOST_ADMIN_THEPLOT: 'console.example.com',
    });
    assert.equal(config.hosts.get('console.example.com'), 'admin-theplot');
  });

  it('fails without CLIENTS, naming it', () => {
    assert.throws(() => loadConfig({}), /CLIENTS/);
    assert.throws(() => loadConfig({ CLIENTS: ' , ' }), /CLIENTS/);
  });

  it('fails on a DEFAULT_CLIENT that is not in CLIENTS', () => {
    assert.throws(
      () => loadConfig({ CLIENTS: 'admin', DEFAULT_CLIENT: 'protofast' }),
      /DEFAULT_CLIENT/,
    );
  });

  it('fails on a name that could leave the bundle directory', () => {
    assert.throws(() => loadConfig({ CLIENTS: '../etc' }), /not a client name/);
    assert.throws(() => loadConfig({ CLIENTS: 'Admin' }), /not a client name/);
  });

  it('keeps an app console in a process of its own', () => {
    assert.throws(() => loadConfig({ CLIENTS: 'admin,admin-theplot' }), /app console/);
    assert.throws(() => loadConfig({ CLIENTS: 'admin-theplot,admin-protofast' }), /app console/);
    assert.throws(() => loadConfig({ CLIENTS: 'protofast,admin-theplot' }), /app console/);
    assert.deepEqual(loadConfig({ CLIENTS: 'admin-theplot' }).clients, ['admin-theplot']);
  });
});

describe('allowedHosts', () => {
  it('is every HOST_ value, so an app console allows the admin host it answers on', () => {
    assert.deepEqual(allowedHosts(consoleEnv).sort(), [
      'admin.dev.localhost',
      'protofast.dev.localhost',
      'theplot.dev.localhost',
    ]);
  });

  it('keeps what NG_ALLOWED_HOSTS already holds, without duplicates', () => {
    assert.deepEqual(
      allowedHosts({
        NG_ALLOWED_HOSTS: 'localhost, *.example.com,',
        HOST_ADMIN: 'admin.example.com',
        HOST_WEB: 'localhost',
      }),
      ['localhost', '*.example.com', 'admin.example.com'],
    );
  });

  it('ignores variables that only look like hosts', () => {
    assert.deepEqual(
      allowedHosts({
        HOSTNAME: 'abc123',
        HOST_ROLE: 'edge role',
        HOST_URL: 'http://x',
        HOST_A: 'a.example.com',
      }),
      ['a.example.com'],
    );
  });
});

describe('hostnameOf', () => {
  it('drops the port and lowers the case', () => {
    assert.equal(hostnameOf('Admin.Dev.LocalHost:20000'), 'admin.dev.localhost');
    assert.equal(hostnameOf('clients'), 'clients');
    assert.equal(hostnameOf('[::1]:4000'), '[::1]');
    assert.equal(hostnameOf(undefined), '');
    assert.equal(hostnameOf(''), '');
  });
});

describe('createLogger', () => {
  it('writes one JSON object per line, naming the replica', () => {
    const lines = [];
    const log = createLogger({ component: 'clients', replica: 'r1', release: 'v1' }, (line) =>
      lines.push(line),
    );
    log('info', 'listening', { port: 4000 });
    assert.equal(lines.length, 1);
    assert.ok(lines[0].endsWith('\n'));
    const entry = JSON.parse(lines[0]);
    assert.equal(entry.msg, 'listening');
    assert.equal(entry.level, 'info');
    assert.equal(entry.component, 'clients');
    assert.equal(entry.replica, 'r1');
    assert.equal(entry.release, 'v1');
    assert.equal(entry.port, 4000);
    assert.ok(!Number.isNaN(Date.parse(entry.time)));
  });
});
