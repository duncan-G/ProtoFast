# 09 · Reference

*Lookup tables: where to change a given thing, and what every configuration variable is.
Self-contained: no reading order required.*

## Where do I change…?

| …this | Edit here | Then |
|---|---|---|
| A component's settings, routes, authorizer, request, `stop` or scaling | `apphost/Program.cs` | `aspire do protostrap-spec`; commit the spec; merge |
| VMs, pools, replicas, PostgreSQL's and Redis's sizes | `apphost/Program.cs` | the same |
| Something only `aspire run` does (LocalStack credentials, smtp4dev, `start-dev`, `DEV_OPERATORS`, the dev servers) | `apphost/Program.cs` (`if (run)`), `apphost/LocalExtensions.cs`, `apphost/ClientApp/` | restart `aspire run` |
| The local edge's or smtp4dev's port | `--LocalEdge:Port`, `--Smtp4dev:Port` (or user secrets) | restart `aspire run` |
| A secret value in an account | `scripts/set-secrets.sh prod [--rotate <group>]`, or ProtoStrap's `scripts/set-secret.sh protofast/prod Key=-` | nothing: ProtoStrap applies it in about three minutes |
| A local secret value | dev's secret: `scripts/set-secrets.sh dev [--rotate <group>]`, or ProtoStrap's `scripts/set-secret.sh protofast/dev Key=-` | restart the components that read it: `aspire resource <name> restart` |
| Edge routing | `Routes(...)`, `Route(...)` and `Authorize(...)` in `apphost/Program.cs` | write the spec, merge |
| Who reaches an admin console | `Tenants:ByHost:admin:Consoles` and `RequiredRoles` in auth's `appsettings.json`; `ADMIN_CONSOLE_ROLES` in the AppHost | deploy auth and the clients (a merge) |
| The admin consoles' CSP | `ADMIN_CSP` in `clients/host/host.mjs`; the admin dev servers' `angular.json` headers | merge |
| A .NET service default | that service's `appsettings.json` | merge |
| A local-only service default | that service's `appsettings.Development.json` | restart `aspire run` |
| Tenants (host → realm, client) | `Tenants:ByHost` in auth's `appsettings.json` | merge |
| A Keycloak realm | `services/keycloak-config/realms/*.json` | merge: keycloak-config applies it; locally restart `aspire run` |
| Keycloak's login theme or emails | `services/keycloak/themes/<theme>/…` | merge (locally a refresh shows it) |
| Keycloak's Java providers | `services/keycloak/providers/email-otp`, `…/ses-email-sender` | merge: the image builds and tests them |
| Keycloak's build or run-time options | `services/keycloak/Dockerfile`, `services/keycloak/entrypoint.sh` | merge |
| Which clients a host process serves | `CLIENTS`, `DEFAULT_CLIENT` in `apphost/Program.cs`; `clients/Dockerfile` for a new app; `CONSOLE_PATHS` in `clients/host/host.mjs` for a new console | write the spec, merge |
| A client's runtime behaviour | `clients/<name>/src/server.ts`, `app.config.ts` | merge |
| The browsers' collector | `services/otel-collector/config.yaml` | merge |
| Hostnames or email domains in an account | `infra/bootstrap/terraform.tfvars` | apply bootstrap again, then the platform (`infra.yml`) |
| Cloudflare's WAF and cache rules, its rate limit | `AddCloudflareCdn` in `apphost/Program.cs` | write the spec, merge: the infra workflow applies it |
| Retention, destroy protection | `infra/prod/main.tf` | merge: the infra workflow applies it |
| CI | `.github/workflows/ci.yml` | merge |
| The deploy pipeline | `.github/workflows/deploy.yml` | merge |
| ProtoStrap's version | `WithVersion` in the AppHost, every workflow's `@v…` and `protostrap-ref`, `ci.yml`'s checkout, every root's `?ref=`, `.claude/settings.json` | one pull request |

## Variables by component

"Spec" means the spec's `env`, written from the AppHost; "secret" means a key of
`protofast/<environment>`, shown with its prefix; "ProtoStrap" means set in every replica.
Locally the AppHost sets the same names.

### `auth`

| Variable | Locally | In an account |
|---|---|---|
| `ConnectionStrings__auth`, `ConnectionStrings__redis` | Aspire references | secret `Shared_ConnectionStrings__auth`, `Shared_ConnectionStrings__redis` |
| `Keycloak__Authority` | Keycloak's endpoint | spec: `http://keycloak:8080` |
| `Keycloak__PublicAuthority` | unset: `ORIGIN_AUTH` | unset: `ORIGIN_AUTH` |
| `Keycloak__ClientSecretProtofastWeb`, `…TheplotWeb`, `…Admin`, `Keycloak__AdminClientSecret` | generated parameters | secret `Shared_Keycloak__*` |
| `InternalJwt__PrivateKeyPem`, `InternalJwt__KeyId` | `protofast/dev`'s `Auth_InternalJwt__*` | secret `Auth_InternalJwt__*` |
| `Smtp__Host`, `Smtp__Port`, `Smtp__StartTls` | smtp4dev, `false` | unset (SES) |
| `Smtp__From` | `no-reply@protofast.dev` (`appsettings.Development.json`) | unset: `no-reply@<HOST_PROTOFAST>` |
| `Tenants__ByHost__*` | `appsettings.json` | `appsettings.json` |
| `HOST_PROTOFAST`, `HOST_THEPLOT`, `HOST_ADMIN`, `ORIGIN_AUTH` | the local hostnames and origin | ProtoStrap |
| `HTTP_PORTS`, `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | Aspire | spec: `8080`, `true` |
| `PROTOSTRAP_REPLICA` | unset: SMTP, console logs | ProtoStrap: SES, logs by OTLP only |
| `SECRET_ID`, `SECRET_PREFIX`, `AWS_REGION`, and the role's credentials | `protofast/dev`, `Auth_`, `us-west-2`, and `AWS_PROFILE=developer` | ProtoStrap (`Auth_`, `AWS_CONTAINER_CREDENTIALS_RELATIVE_URI`) |

### `api`

| Variable | Locally | In an account |
|---|---|---|
| `ConnectionStrings__protofast`, `ConnectionStrings__redis` | Aspire references | secret `Shared_ConnectionStrings__protofast`, `Shared_ConnectionStrings__redis` |
| `InternalJwt__PublicKeyPem` | `protofast/dev`'s `Api_InternalJwt__PublicKeyPem` | secret `Api_InternalJwt__PublicKeyPem` |
| `BUCKET_DOCUMENTS`, `QUEUE_DOCUMENT_UPLOAD_URL` | LocalStack's | ProtoStrap |
| `AWS_ENDPOINT_URL_S3`, `AWS_ENDPOINT_URL_SQS` | LocalStack | unset |
| `AWS_PROFILE` | `developer`, which signs its LocalStack calls too | unset: the role |
| `S3__UploadUrlTtl`, `S3__DownloadUrlTtl` | `appsettings.json`: 5 min | the same |
| `HTTP_PORTS`, `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | Aspire | spec: `8080`, `true` |

### `payments`

| Variable | Locally | In an account |
|---|---|---|
| `InternalJwt__PublicKeyPem` | `protofast/dev`'s `Payments_InternalJwt__PublicKeyPem` | secret `Payments_InternalJwt__PublicKeyPem` |
| `HTTP_PORTS`, `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | Aspire | spec: `8080`, `true` |

### `document-import`

| Variable | Locally | In an account |
|---|---|---|
| `ConnectionStrings__protofast` | Aspire reference | secret `Shared_ConnectionStrings__protofast` |
| `Providers__deepseek__ApiKey`, `…gemini__ApiKey`, `…anthropic__ApiKey` | `protofast/dev`'s `DocumentImport_Providers__*__ApiKey`, empty until set | secret `DocumentImport_Providers__*__ApiKey` |
| `Conversion__BaseUrl` | conversion's endpoint | spec: `http://conversion:8090` |
| `BUCKET_DOCUMENTS`, `QUEUE_DOCUMENT_UPLOAD_URL`, `QUEUE_WORKFLOW_OUTCOMES_URL` | LocalStack's | ProtoStrap |
| `AWS_ENDPOINT_URL_S3`, `AWS_ENDPOINT_URL_SQS`, `AWS_PROFILE` | LocalStack, `developer` | unset: the role |
| `Consumer__MaxConcurrentImports` | 8 | 2 |

### The tasks

| Component | Variable | Locally | In an account |
|---|---|---|---|
| `auth-migrations` | `ConnectionStrings__auth` | Aspire reference | secret `Shared_ConnectionStrings__auth` |
| `protofast-migrations` | `ConnectionStrings__protofast` | Aspire reference | secret `Shared_ConnectionStrings__protofast` |
| `postgres-config` | `ConnectionStrings__postgres` | Aspire reference (the superuser) | secret `PostgresConfig_ConnectionStrings__postgres` |
| `postgres-config` | `ConnectionStrings__keycloak`, `__auth`, `__protofast` | Aspire references | secret `Shared_ConnectionStrings__*` |
| `postgres-config` | `Databases__Names` | spec: `keycloak,auth,protofast` | the same |

### `conversion`

`BUCKET_DOCUMENTS`, `AWS_REGION`, `AWS_ENDPOINT_URL_S3` (locally), `AWS_ACCESS_KEY_ID` and
`AWS_SECRET_ACCESS_KEY` (locally `test`), `PROTOSTRAP_COMPONENT`, `PROTOSTRAP_REPLICA`,
`PROTOSTRAP_RELEASE`, `DRAIN_SECONDS`, the `OTEL_` variables, and the `CONVERSION_*` limits:
`MAX_SOURCE_BYTES`, `MAX_OUTPUT_BYTES`, `TIMEOUT_SECONDS`, `OCR_LANGUAGES`,
`OCR_MIN_CONFIDENCE`, `MAX_OCR_PAGES`, `MAX_PAGES`, `PORT`. Its
[README](../services/conversion/README.md) has defaults.

### `keycloak`

| Variable | Locally | In an account |
|---|---|---|
| `ConnectionStrings__keycloak` → `KC_DB_URL_*`, `KC_DB_USERNAME`, `KC_DB_PASSWORD` | Aspire reference | secret file, from `Shared_ConnectionStrings__keycloak` |
| `KC_BOOTSTRAP_ADMIN_CLIENT_ID` | `protofast-admin` | spec: `protofast-admin` |
| `KC_BOOTSTRAP_ADMIN_CLIENT_SECRET` | generated parameter | secret file, from `Keycloak_KC_BOOTSTRAP_ADMIN_CLIENT_SECRET` |
| `KC_BOOTSTRAP_ADMIN_USERNAME`, `KC_BOOTSTRAP_ADMIN_PASSWORD` | `admin`, generated `keycloak-password` | unset |
| `ORIGIN_AUTH` → `KC_HOSTNAME` | `http://auth.dev.localhost:20000` | ProtoStrap |
| `KC_SPI_EMAIL_SENDER__PROVIDER` | `default` (SMTP) | `ses` (the image) |
| `OTEL_EXPORTER_OTLP_*`, `OTEL_SERVICE_NAME`, `OTEL_RESOURCE_ATTRIBUTES` → `KC_TRACING_*`, `KC_TELEMETRY_*` | unset: nothing exported (the JVM doesn't trust the dashboard's certificate) | ProtoStrap's collector |
| The image's: `KC_DB`, `KC_HEALTH_ENABLED`, `KC_FEATURES`, `KC_TRACING_ENABLED`, `KC_TRACING_SAMPLER_TYPE`, `KC_TELEMETRY_LOGS_ENABLED`, `KC_HTTP_ENABLED`, `KC_PROXY_HEADERS`, `KC_HOSTNAME_BACKCHANNEL_DYNAMIC`, `KC_SPI_THEME__STATIC_MAX_AGE`, `KC_TRACING_INFINISPAN_ENABLED`, `KC_SHUTDOWN_DELAY` | the same | the same |
| Command | `start-dev` | `start --optimized` |

### `keycloak-config`

| Variable | Locally | In an account |
|---|---|---|
| `KEYCLOAK_URL` | Keycloak's endpoint | spec: `http://keycloak:8080` |
| `AUTH_URL` | auth's endpoint | spec: `http://auth:8080` |
| `KC_BOOTSTRAP_ADMIN_CLIENT_ID`, `KC_BOOTSTRAP_ADMIN_CLIENT_SECRET` | `protofast-admin`, generated | spec; secret file from `Keycloak_KC_BOOTSTRAP_ADMIN_CLIENT_SECRET` |
| `KEYCLOAK_GRANTTYPE`, `KEYCLOAK_USER`, `KEYCLOAK_PASSWORD` | `password`, `admin`, generated | unset: client credentials |
| `Keycloak__ClientSecretProtofastWeb`, `…TheplotWeb`, `…Admin`, `Keycloak__AdminClientSecret` | generated parameters | secret files from `Shared_Keycloak__*` |
| `ORIGIN_PROTOFAST`, `ORIGIN_THEPLOT`, `ORIGIN_ADMIN`, `HOST_PROTOFAST`, `HOST_AUTH` | local | ProtoStrap |
| `SMTP_HOST`, `SMTP_PORT` | smtp4dev | unset (realm defaults; SES ignores them) |
| `DEV_OPERATORS` | two operators | unset |
| `WEBAUTHN_RP_ID`, `THEPLOT_WEBAUTHN_RP_ID`, `OPERATORS_WEBAUTHN_RP_ID`, `SMTP_FROM`, `GOOGLE_*`, `APPLE_*` | derived or defaulted | derived or defaulted |

### Clients, dev servers and the clients host

| Variable | `ng serve` (locally) | The clients image |
|---|---|---|
| `CLIENTS`, `DEFAULT_CLIENT` | — | spec: `admin,protofast,theplot` / `protofast`; `admin-theplot` / `admin-theplot` |
| `ADMIN_CONSOLE_ROLES` | admin: `*`; admin-theplot: `admin-theplot` | spec: the same |
| `HOST_<NAME>` | the local hostnames | ProtoStrap |
| `NG_ALLOWED_HOSTS` | — | set by the host from every `HOST_<NAME>` |
| `SERVER_URL` | the host's local origin | unset: the browser uses its own origin |
| `PORT` | Aspire's | 4000 |
| `DRAIN_SECONDS` | — | unset: the host's default, 10 under ProtoStrap |
| `OTEL_*` | Aspire's | ProtoStrap |

### `otel-collector`

`OTEL_EXPORTER_OTLP_ENDPOINT` (ProtoStrap's collector, or the dashboard locally) and, locally
only, `ASPIRE_OTLP_API_KEY`.

### `postgres` and `redis`

| Component | Variable | In an account |
|---|---|---|
| `postgres` | `POSTGRES_PASSWORD_FILE` | spec: `/run/secrets/POSTGRES_PASSWORD`, from `Postgres_POSTGRES_PASSWORD` |
| `postgres` | `POSTGRES_HOST_AUTH_METHOD` | spec: `scram-sha-256` (applies when it first makes its data) |
| `redis` | its command | `--requirepass` from `/run/secrets/REDIS_PASSWORD`, `--maxmemory 256mb`, `volatile-ttl`, no persistence |

Locally both are Aspire's containers with Aspire's generated passwords.

### What ProtoStrap sets in every replica

`PROTOSTRAP_APP`, `PROTOSTRAP_COMPONENT`, `PROTOSTRAP_REPLICA`, `PROTOSTRAP_RELEASE`,
`SECRET_ID`, `SECRET_PREFIX`, `AWS_REGION`, `AWS_CONTAINER_CREDENTIALS_RELATIVE_URI`,
`QUEUE_<NAME>_URL`, `BUCKET_<NAME>`, `FILES_BUCKET`, `HOST_<NAME>`, `ORIGIN_<NAME>`,
`OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_EXPORTER_OTLP_PROTOCOL`, `OTEL_SERVICE_NAME` and
`OTEL_RESOURCE_ATTRIBUTES`
([application contract](https://github.com/duncan-G/protostrap/blob/v0.1.9/docs/protostrap.md#application-contract)).
`DRAIN_SECONDS` isn't among them: each component defaults it itself, 10 s when
`PROTOSTRAP_REPLICA` is set and 0 otherwise.

### GitHub

| Where | What | Set by |
|---|---|---|
| `prod` environment | `AWS_ROLE_ARN` (Deploy role), `AWS_REGION`, `PROTOSTRAP_*` bucket, registry and role names | `infra/bootstrap` |
| `prod-infra` environment | the same with the Infra role, plus `TF_VAR_cloudflare_account_id`, `TF_VAR_cloudflare_zone`, `TF_VAR_hosts`, `TF_VAR_email_domains`, `TF_VAR_alert_emails` (GitHub shows them in uppercase; the infra workflow lowercases them) | `infra/bootstrap` |
| `prod-infra` secrets | `CLOUDFLARE_API_TOKEN` | an Org Admin, `gh secret set` |
| `prod-approval` environment | required reviewers | by hand |

Secret keys are listed in [layer 06](06-secrets.md#one-secret-per-environment).

## File map

```
apphost/                    the AppHost: the application for aspire run and the spec
protostrap.yaml             prod's spec, written by the AppHost: never edit it
Directory.Build.targets     the .NET images' base image, pinned by digest
global.json                 the .NET SDK every project and image builds with
clients/<name>/             the Angular SSR apps: protofast, theplot, admin, admin-theplot
clients/admin-kit/          the consoles' shared platform layer, linked into each console
clients/host/               the Node host that serves the apps (host.mjs, server.mjs)
clients/Dockerfile          the clients image: all four apps and the host
services/auth/              BFF: sign-in, sessions, accounts, the edge's authorizer
services/api/               gRPC: documents, stories, admin RPCs; ProtoFast.SchemaMigrations
services/payments/          gRPC behind the internal JWT
services/document_import/   the import worker and its engine
services/conversion/        document → Markdown (Python, MarkItDown, Tesseract)
services/keycloak/          Keycloak's image: providers, themes, entrypoint
services/keycloak-config/   keycloak's configure task: the realms and keycloak-config-cli
services/postgres_config/   PostgreSQL's configure task
services/otel-collector/    the browsers' collector
services/shared/ProtoStrap/ what ProtoStrap tells a replica; reading the environment's secret
services/shared/ServiceDefaults/ telemetry, readiness, drain, the internal JWT check
services/shared/Storage/    S3 and SQS from BUCKET_<NAME> and QUEUE_<NAME>_URL
infra/people/               Identity Center groups and permission sets (once)
infra/bootstrap/            the account's bootstrap (once, by an Org Admin)
infra/prod/                 prod's platform (the infra workflow)
scripts/set-secrets.sh      fills or rotates a deployed environment's secret
scripts/operator-ui.sh      opens prod's operator UI through SSM
scripts/telemetry.sh        opens prod's Grafana (Loki, Tempo, Prometheus) through SSM
scripts/                    dev helpers, admin console scaffold and checks, the operator grant script
.github/workflows/          ci, deploy, infra
.claude/settings.json       ProtoStrap's skills, pinned to v0.1.9
```

## Ports

| Port | Where | What |
|---|---|---|
| 20000 | locally | ProtoStrap's local edge, every host (`--LocalEdge:Port`) |
| 8025 | locally | smtp4dev's web page (`--Smtp4dev:Port`) |
| 17000 / 15000 | locally | the AppHost's dashboard, https / http (`launchSettings.json`) |
| 8080 | overlay | auth (HTTP/1.1), api and payments (cleartext HTTP/2, gRPC), keycloak (HTTP) |
| 9000 | overlay | Keycloak's management port: `/health/ready` |
| 4000 | overlay | the clients host (`clients`, `clients-admin-theplot`) |
| 8090 | overlay | conversion; no route |
| 4318 | overlay | otel-collector's OTLP/HTTP (routed at `/otlp/`), and ProtoStrap's collector, `telemetry:4318` |
| 13133 | overlay | otel-collector's health check |
| 5432, 6379 | overlay | PostgreSQL, Redis on data-1; never routed |
| 7480, 7481, 7482 | data-1 loopback, through SSM | ProtoStrap's operator UI (Platform Admins, Org Admins) and Grafana (Developers too) |

Locally projects and dev servers get ports from Aspire; their containers' targets are the
same as in an account.
