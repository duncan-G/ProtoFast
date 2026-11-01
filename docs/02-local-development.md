# 02 · Local development

*How your laptop's copy of the stack is configured. Self-contained: everything a
developer needs to run, inspect and change the local environment.*

## Start it

```bash
aspire run
```

That builds and starts every resource the AppHost declares
([`apphost/Program.cs`](../apphost/Program.cs)) and prints the Aspire dashboard's URL. Stop
with `Ctrl+C` or `aspire stop`.

Prerequisites:

- the .NET SDK [`global.json`](../global.json) pins (10.0.113, latest patch), Docker, Node 24
  and the Aspire CLI 13.6.2;
- Go at the version ProtoStrap's `go.mod` names;
- a ProtoStrap checkout beside this repository, `../ProtoStrap`, at the spec's version. The
  AppHost builds ProtoStrap.Hosting from it (`ProtoStrapSource` in
  [`ProtoFast.AppHost.csproj`](../apphost/ProtoFast.AppHost.csproj) chooses another, and CI
  uses `.protostrap`), and builds the `protostrap` CLI that renders the local edge.

On Ubuntu, `bash scripts/setup-dev-dependencies.sh` installs Docker, Node's current LTS and
the latest Aspire CLI when they're missing, not the versions above; the .NET SDK and Go are
yours to install. The .NET services read `protofast/dev` with an AWS SSO profile in ProtoFast's
account ([Local secrets and keys](#local-secrets-and-keys)).

| Open                                 | For                                                    |
| ------------------------------------ | ------------------------------------------------------ |
| http://protofast.dev.localhost:20000 | The product site                                       |
| http://theplot.dev.localhost:20000   | ThePlot                                                |
| http://admin.dev.localhost:20000     | The admin console, and theplot's at `/theplot/`        |
| http://auth.dev.localhost:20000      | Keycloak's sign-in pages (the edge routes only these)  |
| http://localhost:8025                | smtp4dev, where every mail goes, sign-in codes included |
| The dashboard's link                 | Logs, traces and metrics, browsers' included           |

## What the AppHost starts, and what it injects

| Resource                         | Kind                | Notable local configuration                                              |
| -------------------------------- | ------------------- | ------------------------------------------------------------------------ |
| `protostrap-edge`                | container (Envoy)   | ProtoStrap's local edge on port 20000, rendered from the services' routes and authorizer; `protostrap-build`, nested under it, builds the `protostrap` that renders it |
| `protostrap-localstack`          | container           | S3 and SQS for the `documents` bucket and both queues; sets `BUCKET_DOCUMENTS`, `QUEUE_*_URL`, `AWS_ENDPOINT_URL_S3/SQS` on what references them |
| `postgres`                       | container           | a data volume and pgAdmin; databases `keycloak`, `auth`, `protofast`     |
| `postgres-config`                | project             | PostgreSQL's configure task; locally every database logs in as the superuser, so it only makes the databases |
| `redis`                          | container           | Aspire's Redis with its own password                                     |
| `keycloak`                       | Dockerfile          | `start-dev`, so theme edits show on a refresh; `services/keycloak/themes` bind-mounted read-only; the built-in SMTP sender instead of the image's SES one (`KC_SPI_EMAIL_SENDER__PROVIDER=default`); a bootstrap admin user `admin` and the bootstrap admin client `protofast-admin` |
| `keycloak-config`                | Dockerfile          | applies the realms once Keycloak is ready, logging in as `admin` (`KEYCLOAK_GRANTTYPE=password`); realm mail to smtp4dev; seeds `DEV_OPERATORS` |
| `smtp4dev`                       | container           | mail catcher on port 8025 (web) and its SMTP port                        |
| `auth-migrations`, `protofast-migrations` | projects   | run to completion before auth, and before the api and document-import; each has a **Rebuild** command that drops its schema and migrates again |
| `auth`                           | project             | `Keycloak__Authority` to Keycloak's own URL, the client secrets, the internal JWT's private key, `Smtp__Host/Port` to smtp4dev with `Smtp__StartTls=false` |
| `api`, `payments`                | projects            | the internal JWT's public key, from `protofast/dev`                       |
| `conversion`                     | Dockerfile          | LocalStack's bucket and test credentials, OTLP to the dashboard          |
| `document-import`                | project             | `Conversion__BaseUrl` to conversion; the model APIs' keys from `protofast/dev` |
| `otel-collector`                 | Dockerfile          | exports browsers' telemetry to the dashboard, with its key in `ASPIRE_OTLP_API_KEY` |
| `protofast`, `theplot`, `admin`, `admin-theplot` | `ng serve` | `PORT`, `SERVER_URL` (the host's local origin), OTLP to the dashboard; the admin ones `ADMIN_CONSOLE_ROLES` |

What waits for what follows the spec: auth waits for its database, Redis, Keycloak (and so
keycloak-config) and its migrations; the api and document-import wait for theirs; anything
that uses one of PostgreSQL's databases waits for postgres-config. Every resource gets
`HOST_<NAME>` with the local hostname and `ORIGIN_<NAME>` with `http://` and port 20000.

Things worth knowing because they explain otherwise odd behaviour:

- **Each client has its own hostname, on one port.** Cookies ignore ports, so clients on one
  host would share one `pf_session`. The realms' redirect URIs are built from
  `ORIGIN_<NAME>`, so they are right whatever the port.
- **The edge's console logs each request**: method, host and path (no query), the service it
  went to, the status and Envoy's flags, such as `UAEX` for a request auth's answer refused,
  `NR` for one no route matched, or `UH` for one no ready replica could take.
- **A resource's own URL on the dashboard skips the edge,** its routes and the authorizer. Go
  through `http://«host».dev.localhost:20000` to see what a browser sees.
- **The local edge reaches projects at `host.docker.internal`** on their port and containers by
  name on Aspire's network ([ProtoStrap's local edge](https://github.com/duncan-G/protostrap/blob/v0.1.2/docs/protostrap-aspire.md#the-local-edge)).
  When the ProtoStrap checkout changes, `protostrap-build` says `Behind`; its **Rebuild**
  command rebuilds `protostrap` and restarts the edge.
- **Readiness skips the host test locally.** Services answer `/healthz` for any Host when
  `PROTOSTRAP_COMPONENT` isn't set, and the edge's gRPC checks ask for the server as a whole.
- **A dev server shows unhealthy until it serves HTTP:** `ng serve` stays up after a failed
  build, so the AppHost probes it. Nothing waits on that check.

## Local secrets and keys

The services read dev's secret, `protofast/dev`, in ProtoFast's account beside prod's, as they
read prod's ([layer 06](06-secrets.md#dev)). Each .NET process gets `SECRET_ID=protofast/dev`,
its `SECRET_PREFIX`, `AWS_REGION` (the launch profile's `us-west-2`, LocalStack's too) and
`AWS_PROFILE=developer`, and reads its keys at startup with your sign-in, which also signs its
calls to LocalStack. A component reads them when it starts: restart one to take a new value.

Once per machine, make the profile in ProtoFast's account with the Developer permission set:

```bash
aws configure sso --profile developer
```

`aspire run` checks for a session for the launch profile's `AWS_PROFILE` and, when there is none
or it has expired, runs `aws sso login --profile developer` before it starts anything. A session
that expires while the AppHost runs fails the next process to start with the SDK's SSO error:
sign in again, then restart it.

| Value                                                     | From                                                          |
| --------------------------------------------------------- | ------------------------------------------------------------- |
| The internal JWT's key pair and key id                    | `protofast/dev`: `Auth_InternalJwt__PrivateKeyPem`, `Auth_InternalJwt__KeyId`, and the public key under `Api_` and `Payments_` |
| The model APIs' keys                                      | `protofast/dev`: `DocumentImport_Providers__{deepseek,gemini,anthropic}__ApiKey`; empty until set, and imports fail at the model call |
| PostgreSQL's and Redis's passwords, the connection strings | Aspire, generated and kept in the AppHost's user secrets      |
| Keycloak's bootstrap admin client secret, the four realm client secrets, its local `admin` password | Aspire, generated and kept in the AppHost's user secrets: keycloak and keycloak-config can't read Secrets Manager |

`scripts/set-secrets.sh dev` fills `protofast/dev` once for the team, and removes the local
resources' keys if it finds any ([layer 06](06-secrets.md#dev)). Set a model API's key with
ProtoStrap's script, typed without echo:

```bash
../ProtoStrap/scripts/set-secret.sh protofast/dev DocumentImport_Providers__deepseek__ApiKey=-
```

Keeping the generated values matters because the data volumes keep their passwords: a new
Keycloak `admin` password would not match the user an existing `keycloak` database already
has.

## Mail and operators

- **Mail goes to smtp4dev.** auth sends over SMTP (`Smtp__Host`, `Smtp__Port`), and Keycloak
  with its built-in SMTP sender, which keycloak-config points at smtp4dev's address on the
  container network (`SMTP_HOST`, `SMTP_PORT`). In an account both send through SES instead
  ([layer 05](05-identity.md#mail)).
- **Two operators exist:** `DEV_OPERATORS` in the AppHost has keycloak-config create
  `protofast@admin.test` (roles `platform`, `admin-protofast`) and `theplot@admin.test`
  (`admin-theplot`) in the `operators` realm. Sign in at the admin host with the email; the
  code arrives in smtp4dev.

## Known gaps

- **Live reload doesn't work through the local edge.** Refresh the page after a change.
- **Some containers' telemetry doesn't reach the dashboard over HTTPS.** The dashboard's OTLP
  endpoint uses the development certificate, which Keycloak's JVM and conversion's Python don't
  trust, so their exports fail locally (Keycloak gets no local exporter at all). The .NET
  services, the dev servers and otel-collector, which skips the check, and so browsers'
  telemetry, arrive.
- **The local edge isn't Cloudflare:** no TLS and no cache. Route rates and the authorizer
  apply in both; Cloudflare's WAF and cache rules apply only in an account
  ([layer 04](04-edge.md#tls-and-forwarded-headers)).

## Variations

**Run the clients image instead of the dev servers.** `clients` and `clients-admin-theplot`
are what run in an account:

```bash
SsrHost__Dev=true aspire run
```

(the `https-ssr-host` launch profile, or `--SsrHost:Dev=true`). Same hosts, same routes and
authorizer; a client change shows once the image is built again, on the next start.

**Run beside another AppHost** that holds port 20000 or 8025:

```bash
aspire run -- --LocalEdge:Port 20100 --Smtp4dev:Port 8125
```

`dotnet user-secrets --project apphost set LocalEdge:Port 20100` keeps the port. Every local
origin, and so every redirect URI, follows it.

**Serve one client on its own**, without the rest of the stack:

```bash
CLIENT=admin scripts/dev-client.sh
```

This picks up the client's `.nvmrc` through `nvm`/`fnm` and runs `ng serve`.
`.claude/launch.json` exposes the same as `protofast-client` (port 4300) and `admin-client`
(4301). Nothing routes its API calls, so it is for UI work only.

## Common tasks

| Task                              | How                                                                                                       |
| --------------------------------- | --------------------------------------------------------------------------------------------------------- |
| Regenerate gRPC client code       | `npm run generate:grpc` in the client (`npm start` runs it too); commit `src/lib/gen`, which the image builds from and CI checks |
| Add an EF migration (auth)        | `dotnet ef migrations add <Name> -p services/auth/src/ProtoFast.Auth.Data`                                |
| Add an EF migration (protofast)   | in `services/api/src/ProtoFast.Data`: `dotnet ef migrations add <Name> --startup-project ../ProtoFast.SchemaMigrations` |
| Apply migrations                  | automatic: the migrations resources run before the services that use them. **Rebuild** on one drops its schema and migrates again |
| Edit a Keycloak theme             | under `services/keycloak/themes/`; bind-mounted, and `start-dev` doesn't cache, so a refresh shows it      |
| Change Keycloak's providers       | under `services/keycloak/providers/`; the keycloak image, which builds and tests them with Maven, is built when `aspire run` starts |
| Change a realm                    | edit `services/keycloak-config/realms/*.json` and restart the AppHost: keycloak-config's image holds the files, and it applies all of them to the existing realms ([layer 05](05-identity.md#changing-the-realms)). No database drop |
| Add or change a local operator    | `DEV_OPERATORS` in `apphost/Program.cs`                                                                   |
| Write the spec after an AppHost change | `aspire do protostrap-spec`; commit `protostrap.yaml`                                                  |
| See traces, logs, metrics         | the dashboard; the edge's console for each request                                                        |
| Run the tests                     | `dotnet test ProtoFast.slnx`; `npm test` in each client; `node --test` in `clients/host`; `python -m pytest` in `services/conversion`; `test/entrypoint_test.sh` in `services/keycloak` and `services/keycloak-config` |

## Where local configuration lives

```
apphost/Program.cs                        ← the application, for aspire run and the spec
apphost/LocalExtensions.cs                ← what only aspire run adds (LocalStack credentials, OTLP)
apphost/ClientApp/, apphost/Postgres/     ← the dev servers; the migrations resources and their Rebuild command
apphost/Properties/launchSettings.json    ← dashboard URLs, ASPNETCORE_ENVIRONMENT, the https-ssr-host profile
protofast/dev (Secrets Manager)           ← the services' local secrets, read as prod's are
services/*/…/appsettings.Development.json ← per-service local defaults
services/keycloak-config/realms/          ← the realms, applied on every start
services/keycloak/themes/                 ← login and email themes, bind-mounted
clients/*/angular.json, package.json      ← client builds and ng serve flags
scripts/dev-client.sh, .claude/launch.json ← standalone client dev servers
```

The pools, requests and scaling in `apphost/Program.cs` only matter to the spec: `aspire run`
ignores them.
