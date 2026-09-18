# 02 · Local development

*How your laptop's copy of the stack is configured. Self-contained: everything a
developer needs to run, inspect and change the dev environment.*

## Start it

```bash
aspire run
```

That is the whole thing. It builds and starts every resource declared in
`[apphost/Program.cs](../../apphost/Program.cs)` and prints the Aspire dashboard
URL. Stop with `Ctrl+C` or `aspire stop`.

Prerequisites (installed idempotently by `bash scripts/setup-dev-dependencies.sh` on Ubuntu 24): .NET 10 SDK, Aspire CLI, Docker Engine, and Node 24.

AWS CLI v2 and an SSO profile named **`developer`** (the Developer permission set) are required: the AppHost logs in at startup so each service can read `protofast/dev` from Secrets Manager. Configure once with `aws configure sso --profile developer`, and give that profile a region (`aws configure set region <region> --profile developer`) — an SSO profile has none by default, and the AppHost passes it to every service as `AWS_REGION`. See [layer 06](06-secrets.md).

## What the AppHost starts, and what it injects


| Resource                  | Kind                 | Notable dev-only configuration                                                                                   |
| ------------------------- | -------------------- | ---------------------------------------------------------------------------------------------------------------- |
| `otel-collector`          | container            | OTLP gRPC + HTTP receivers; every other resource points at it                                                    |
| `postgres`                | container            | plus pgAdmin and a data volume (publish mode gets neither); hosts `keycloak` and `auth` databases                |
| `auth-db`                 | database             | runs `ProtoFast.Auth.SchemaMigrations` before `auth` starts                                                      |
| `redis`                   | container            | session, correlation and replay stores                                                                           |
| `keycloak`                | container (26.7)     | realm import from `infra/keycloak/realms`, themes and provider JAR bind-mounted, tracing + logs to the collector |
| `smtp4dev`                | container            | local mail catcher; web UI pinned at host `5000`, SMTP allocated; both Keycloak and `auth` are pointed at it     |
| `auth`, `payments`, `api` | .NET projects        | OTLP reference, Redis/Postgres connection strings; JWT and Keycloak secrets from `protofast/dev`                 |
| `envoy`                   | Dockerfile container | one HTTPS listener per client, dev certificate, upstream host/port for every service                             |
| `admin`, `protofast`      | `ng serve`           | `PORT`, `SSL_CERT`, `SSL_KEY`, `SERVER_URL`, OTel endpoints                                                      |


Two details are worth knowing because they explain otherwise-mysterious errors:

- **The client listener ports are pinned** — `20000` for the first registered
client (`admin`), `20001` for the second (`protofast`). Keycloak's realm import
lists `https://localhost:20000|20001/signin-oidc` as exact redirect URIs, so a
randomly-assigned port would fail with `invalid_redirect_uri`.
- **Containers reach host processes via** `host.docker.internal`**.** Envoy and
Keycloak are started with `--add-host=host.docker.internal:host-gateway`, which
is how Envoy dials the .NET services and how Keycloak posts back-channel logout
tokens to `auth`.



## Dev credentials and keys

On startup the AppHost authenticates AWS SSO profile `developer`. Each .NET
service then reads `protofast/dev` in-process (same provider as production).
Populate it once with `scripts/generate-dev-secrets.sh` — that writes the
internal-JWT pair and the Keycloak client secrets.
SES SMTP and the production DB passwords stay out of this map.

- **Keycloak client secrets** in `protofast/dev` match the realm import's
  `${…:default}` placeholders (`dev-protofast-web-secret`, `dev-admin-secret`,
  `dev-account-admin-secret`), so Keycloak and auth-svc agree without the
  AppHost injecting them.
- **The internal JWT key pair** is generated once by the script (EC P-256) and
  read in-process: private PEM by `auth`, public PEM by `payments` and `api`.
  Restarting the stack keeps the same keys; tokens live five minutes either way.
- **Mail** goes to smtp4dev. The web UI is pinned at `http://localhost:5000`; SMTP
  is allocated. The AppHost injects that SMTP host/port into both Keycloak
  (container network) and `auth` (host network) — those are not secrets. The
  `localhost:1025` in `appsettings.Development.json` is only a fallback for
  running `auth` outside the AppHost.



## Variations

**Smoke-test the production SSR host locally.** The unified clients host is what
runs in production; the per-client dev servers are not. To exercise it:

```bash
SsrHost__Dev=true aspire run
```

(equivalently, pass `--SsrHost:Dev=true` to the AppHost).

Envoy switches to `dev-host` mode: the same per-client listener URLs, but every
catch-all routes to the containerised clients host with an `x-client` header. No
HMR.

**Serve one client on its own**, without the rest of the stack:

```bash
CLIENT=admin scripts/dev-client.sh
```

This picks up the client's `.nvmrc` through `nvm`/`fnm` and runs `ng serve`.
`.claude/launch.json` exposes the same thing as `protofast-client` (port 4300)
and `admin-client` (4301). API calls will fail — nothing is proxying them — so
this is for UI work only.

## Common tasks


| Task                              | How                                                                                                                                           |
| --------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------- |
| Regenerate gRPC client code       | `npm run generate:grpc` in the client (runs automatically on `start`/`build`)                                                                 |
| Add an EF migration               | `dotnet ef migrations add <Name> -p services/auth/src/ProtoFast.Auth.Data`                                                                    |
| Apply migrations                  | automatic — `auth-db` runs the migrations project before `auth` starts                                                                        |
| Rebuild the Keycloak provider JAR | `infra/keycloak/providers/build.sh`, then restart Keycloak                                                                                    |
| Edit the Keycloak login theme     | edit under `deploy/keycloak/themes/protofast`; `start-dev` disables theme caching, so a refresh is enough                                     |
| Change the realm                  | edit `infra/keycloak/realms/protofast-realm.json` **and** delete the Keycloak container's data — the import skips a realm that already exists |
| See traces / logs / metrics       | the Aspire dashboard URL printed by `aspire run`                                                                                              |
| Run the auth tests                | `dotnet test services/auth/tests/ProtoFast.Auth.UnitTests` (and `…IntegrationTests`)                                                          |




## Where dev configuration actually lives

```
apphost/Program.cs                     ← the dev environment, in C#
apphost/aspire.config.json             ← dashboard/OTLP URLs, ASPNETCORE_ENVIRONMENT
services/*/…/appsettings.Development.json ← per-service dev defaults (`protofast/dev`)
infra/keycloak/realms/protofast-realm.json ← the realm Keycloak imports on first start
clients/*/angular.json, package.json   ← client build + `ng serve` flags
scripts/dev-client.sh, .claude/launch.json ← standalone client dev servers
```

Nothing in `deploy/` is used by `aspire run`, with one deliberate exception: the
Keycloak **themes** and **provider JAR** are bind-mounted from `deploy/keycloak/`
so dev and prod load the same artefacts.