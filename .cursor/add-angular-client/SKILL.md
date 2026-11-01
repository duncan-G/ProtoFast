---
name: add-angular-client
description: >-
  Adds an Angular SSR client to ProtoFast on a host of its own: the app under clients/, its
  dev server and host in apphost/Program.cs, the clients image, browser telemetry, sign-in
  through auth and Keycloak, prod's hostname, CI and the docs. Use when asked to add a new
  frontend, web app or site; an app's admin console uses scripts/scaffold-admin-console.py.
disable-model-invocation: true
---

# Add an Angular client

A client is its own Angular SSR project under `clients/`, on a host of its own: cookies ignore
ports, so clients on one host would share a session. Under `aspire run` it is an `ng serve`
behind ProtoStrap's local edge at `http://<name>.dev.localhost:20000`; in an account the Node
host in the `clients` image (`clients/host`) serves every client in `CLIENTS`, each on its
`HOST_<NAME>`. Pages, gRPC-Web, auth's sign-in paths and browser telemetry share the client's
origin. theplot is the model: a product client with a realm of its own.

ProtoStrap's general rules are in its skills: protostrap-aspire, protostrap-telemetry
("Telemetry from browsers"), protostrap-secrets, protostrap-deploy. The layout this skill fills
in is explained in docs/03-services-and-clients.md and docs/04-edge.md.

## Names

- `<name>`: the client, its host, its folder `clients/<name>`, its Angular project and its
  resource, all one lowercase name (`studio`). The clients host serves client `<name>` on
  `HOST_<NAME>`, so the client and host names must match.
- `<NAME>`: the variable form, upper case with `-` as `_` (`ORIGIN_<NAME>`).
- `<Name>`: PascalCase, for `ClientSecret<Name>Web`.

## Decide first

Ask when the request doesn't say. Each is the developer's call:

- **An app's admin console** (`admin-<app>`, at `/<app>/` on the admin host) isn't this
  skill: run `scripts/scaffold-admin-console.py <app> "<Title>"` and follow what it prints,
  then add `admin-<app>` to ci.yml's `clients` matrix, which its list leaves out.
- **Realm:** one of its own, as theplot has, or a second client in an existing realm.
- **Prod hostname:** `<name>.protofast.dev` unless told otherwise; it must be in the
  Cloudflare zone.
- **Which services it calls:** the buf inputs, and whether its host routes to them.
- **Default client:** stays `protofast`, the client for a host no client is named for.

## Steps

1. **Scaffold** with the Angular CLI major the other clients use (`@angular/cli` in
   `clients/theplot/package.json`):

   ```bash
   npx @angular/cli@<major> new <name> --directory clients/<name> --style scss --routing --ssr \
     --skip-git --package-manager npm --defaults
   ```

2. **Bring it to theplot's shape.**
   - `package.json` scripts as theplot's: `generate:grpc` (`buf generate`), `start`
     (`npm run generate:grpc && ng serve --host 0.0.0.0 --port ${PORT:-4200} --allowed-hosts`),
     `build` (`ng build`, without codegen: the image has no protos) and `test`. Dependencies:
     theplot's `@bufbuild/*`, `@connectrpc/*`, `@opentelemetry/*`, Tailwind and Express
     entries, at its versions. `npm install` writes `package-lock.json`, which CI's `npm ci`
     needs.
   - `.postcssrc.json` and `@import "tailwindcss";` in `src/styles.scss`, as theplot's.
   - `angular.json` build options as theplot's: `"outputMode": "server"`, `ssr.entry`, and
     `"security": { "allowedHosts": [] }`.
   - Copy unchanged from `clients/theplot`: `src/server.ts`, `src/main.ts`,
     `src/lib/telemetry.ssr.ts`, `src/lib/grpc-trace.interceptor.ts`, `src/app/server-url.ts`,
     `src/app/app.config.ts`, `src/app/app.config.server.ts`, `src/app/grpc-transport.ts`,
     `src/app/auth/`.
   - Copy and rename the service names: `src/telemetry.server.ts` (`<name>-client-server`)
     and `src/lib/telemetry.browser.ts` (`<name>-client`). Keep the
     `__nodeOtelSdkStarted` guard: in the clients image the host starts the SDK.
   - In `src/server.ts`, `PROTECTED_PREFIXES`: the paths only a signed-in user sees.

   What these do: `server.ts` passes `NG_ALLOWED_HOSTS` (the clients host sets it to every
   `HOST_<NAME>`) to Angular, trusts `x-forwarded-for` and `x-forwarded-proto`, and redirects
   a protected path without `x-user-id` to `/signin`. `SERVER_URL` reaches the browser through
   `TransferState`, and the browser falls back to its own origin.

3. **Generate the gRPC code** and wire the transport: `references/proto-codegen.md`.

4. **Accept its browser telemetry.** Add `<name>-client` to both conditions of `filter/clients`
   in `services/otel-collector/config.yaml`: the collector drops every other `service.name`.

5. **Describe it in `apphost/Program.cs`.**
   - `.WithHost("<name>", "<name>.protofast.dev", local: "<name>.dev.localhost")` on the
     environment. Every resource then gets `HOST_<NAME>` and `ORIGIN_<NAME>`.
   - `"<name>"` in `clientHosts`, which gives the host auth's sign-in routes (`/signin`,
     `/signup`, `/signout`, `/reset`, `/add-passkey`, `/account/`), the api's and payments'
     package routes and the clients' `<name>/` route.
   - `.Route("<name>/otlp/", rate: 20)` on `otel-collector`.
   - `<name>` in the `CLIENTS` of `ClientsHost("clients", "admin,protofast,theplot", ...)`.
     `DEFAULT_CLIENT` changes only if decided.
   - `DevServer("<name>", "<name>.dev.localhost", ["<name>/"]);` beside the others.
     `AddClientApp` (apphost/ClientApp) runs `npm run start` in `clients/<name>` with `PORT`,
     `SERVER_URL` (`http://<name>.dev.localhost:20000`) and OTLP to the dashboard.

6. **Add it to the clients image.** In `clients/Dockerfile`, a build stage as theplot's, its
   `COPY --from=<name> /src/<name>/dist/<name> ./<name>/dist/<name>` in the last stage, and
   the header's list of apps. `clients/host` needs no change: it loads
   `../<name>/dist/<name>/server/server.mjs` for each name in `CLIENTS`.

7. **Sign-in:** the tenant, the realm client, its secret and keycloak-config's entrypoint, in
   `references/auth-tenant.md`.

8. **Give it prod's hostname.** Add `<name>` to `hosts` in
   `infra/bootstrap/terraform.tfvars.example` and to the `hosts` descriptions in
   `infra/bootstrap/main.tf` and `infra/prod/main.tf`. An Org Admin adds it to their
   `infra/bootstrap/terraform.tfvars`, which is gitignored, and applies bootstrap again before
   the release merges: that writes prod-infra's `TF_VAR_hosts`, and the platform's `hosts` must
   name every host the spec has. The release's infra job then applies the platform.
   protostrap-deploy has the order.

9. **CI.** Add `<name>` to the `clients` matrix in `.github/workflows/ci.yml`: `npm ci`, the
   check that `src/lib/gen` is what the protos make, `npm test` and `ng build`. The `images`
   job already builds `clients`.

10. **Write the spec.** `aspire do protostrap-spec` and commit `protostrap.yaml`. Its diff
    shows the host, the new routes, `CLIENTS` and the client secret in `auth`'s and
    `keycloak-config`'s `secrets`.

11. **Docs.** Add the client where its peers are listed:
    - docs/01-topology.md: the `clients` row, the hosts table, the local diagram.
    - docs/02-local-development.md: the "Open" table, the dev servers' row.
    - docs/03-services-and-clients.md: the browser service names, the clients host's
      `CLIENTS` and its list of apps, the collector's hosts and names.
    - docs/04-edge.md: the routes table.
    - docs/08-infrastructure.md: "Hosts and mail".
    - docs/09-reference.md: the clients' variables, the file map, `clients/Dockerfile`.
    - services/otel-collector/README.md and README.md.
    - The sign-in docs are listed in `references/auth-tenant.md`.

## Check it works

- `aspire run`, then open `http://<name>.dev.localhost:20000`. The `<name>` dev server shows
  unhealthy on the dashboard until `ng serve` answers, which it doesn't after a failed build.
- The dashboard shows the page load as `<name>-client`, in the same trace as its render.
- `/signin` on the host goes to Keycloak's page for the realm on `http://auth.dev.localhost:20000`,
  and the code arrives in smtp4dev at http://localhost:8025. A 404 is a missing tenant.
- The `protostrap-edge` console logs each request with the service it went to: `NR` is no
  route, `UH` no ready replica.
- `SsrHost__Dev=true aspire run` serves the same URL from the clients image.
- In `clients/<name>`: `npm test -- --watch=false` and
  `npm run generate:grpc && git diff --exit-code -- .`. Then `node --test` in `clients/host`,
  `bash services/keycloak-config/test/entrypoint_test.sh` and `dotnet test ProtoFast.slnx`.
- `aspire do protostrap-spec` leaves `git status -- protostrap.yaml` clean once committed;
  CI fails a spec that differs from what the AppHost writes.
