# 09 · Reference

*Lookup tables: where to change a given thing, and what every configuration
variable is. Self-contained — no reading order required.*

## Where do I change…?

| …this | Edit here | Then |
|---|---|---|
| A dev-only setting (ports, containers, injected env) | `apphost/Program.cs` | restart `aspire run` |
| A production setting for a container | `deploy/docker-compose.host-edge.yml` or `…host-services.yml` | deploy any component on that host |
| A stable production value (domains, `CLIENTS`, region) | `infra/templates/user_data.host_*.sh.tftpl` (new boxes) and `/opt/protofast/.env` (existing) | `terraform apply` and/or a deploy |
| A secret value | `scripts/generate-dev-secrets.sh` (local JWT/Keycloak) or `scripts/populate-secrets.sh Key=value` (`--prod` for prod) | restart aspire (dev) / deploy the consuming component (prod) |
| Edge routing, CORS, allow-lists | `proxy/*.tmpl`, `proxy/entrypoint.sh` | deploy `envoy` |
| A .NET service default | that service's `appsettings.json` | deploy that service |
| A dev-only service default | `appsettings.Development.json` | restart |
| The Keycloak realm | `infra/keycloak/realms/protofast-realm.json` **and** `deploy/keycloak/realms/` | deploy `keycloak`; flows need the manual script |
| The Keycloak login theme or emails | `deploy/keycloak/themes/protofast/…` (or `…/theplot/…` — one theme per realm) | deploy `keycloak` (dev: just refresh) |
| Keycloak's Java extensions | `infra/keycloak/providers/email-otp`, then `build.sh` | commit the JAR under `deploy/keycloak/providers`, deploy `keycloak` |
| A client's runtime behaviour | `clients/<name>/src/server.ts` or `app.config.ts` | deploy `client-<name>` |
| Which clients exist | `apphost/Program.cs` (`proxy.WithClient`), `CLIENTS`/`DEFAULT_CLIENT`, a new `deploy-client-<name>.yml` | both |
| AWS/Cloudflare resources | `infra/*.tf` | run the `infra` workflow |
| Public hostnames | repo secrets `ADMIN_DOMAIN` / `PROTOFAST_DOMAIN` / `THEPLOT_DOMAIN` / `KEYCLOAK_DOMAIN` | `infra` apply, then a deploy so `.env` catches up |
| A segmentation pipeline threshold (window size, triage limits, repair rounds) | `services/segmentation/src/ProtoFast.Segmentation.Worker/appsettings.json` | deploy `segmentation` |
| Which models the router may use, and their limits | `Seg_Routing__Models` / `__Pools` in the worker's `appsettings.json` | deploy `segmentation` |
| A prompt, a skill or a JSON schema | `services/segmentation/src/ProtoFast.Segmentation.Pipeline/Assets/` | deploy `segmentation` — this changes `PromptVersion`, which invalidates every model's qualification for the affected role |
| CI/CD behaviour | `.github/workflows/_component-deploy.yml` or a `deploy-*.yml` | merge to `main` |
| On-host deploy behaviour | `deploy/deploy.sh` | any deploy ships the new copy |

## Variables by component

### `auth`

| Variable | Dev source | Prod source |
|---|---|---|
| `Auth_Keycloak__Authority` | AppHost | compose (`http://keycloak:8080`) |
| `Auth_Keycloak__PublicAuthority` | — | compose (`https://${KEYCLOAK_DOMAIN}`) |
| `Auth_Keycloak__ClientSecretProtofastWeb` / `…Admin` / `AdminClientSecret` | Secrets Manager (`protofast/dev`) | Secrets Manager → compose `.env` |
| `Auth_InternalJwt__PrivateKeyPem` | Secrets Manager (`protofast/dev`) | Secrets Manager (in-process) |
| `Auth_InternalJwt__KeyId` | Secrets Manager (`dev-1`) | `.env` (`INTERNAL_JWT_KEY_ID`) |
| `Auth_Smtp__Host/Port/From/StartTls` | AppHost → smtp4dev (not secrets) | `.env`, from Secrets Manager + SES |
| `Auth_Smtp__User/Password` | — (smtp4dev needs none) | `.env`, from Secrets Manager + SES |
| `Tenants__ByHost__<host>__Realm` / `__ClientId` / `__MaxAge` / `__AcrValues` | `appsettings.Development.json` | compose |
| `ConnectionStrings__redis`, `ConnectionStrings__auth`, `ConnectionStrings__keycloak` | Aspire references | compose |
| `Secrets:SecretId`, `Secrets:Prefix` | `appsettings.Development.json` (`protofast/dev`) | `appsettings.json` (`protofast/app`) |
| `AWS_REGION` / `AWS_DEFAULT_REGION` | SSO profile `developer` | compose (required by the SDK) |
| `AWS_PROFILE` | AppHost (`developer`) | — |

### `payments` and `api`

| Variable | Purpose |
|---|---|
| `Shared_InternalJwt__PublicKeyPem` (dev) / `…PublicKeyPemFile` (prod) | verify the internal JWT |
| `Secrets:SecretId`, `Secrets:Prefix` | `protofast/dev` + `Payments_` / `Api_` in Development; `protofast/app` in Production |
| `ASPNETCORE_HTTP_PORTS`, `ASPNETCORE_KESTREL__ENDPOINTDEFAULTS__PROTOCOLS` | gRPC over HTTP/2 on 8080 |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | telemetry; unset disables the exporters |

### `segmentation`

The worker (plan §20.2). Everything is `Seg_`-prefixed, so the same names work in dev and prod
with a different injector.

| Variable | Dev source | Prod source |
|---|---|---|
| `Seg_Storage__Bucket` | AppHost (LocalStack bucket) | compose ← `.env` `SEGMENTATION_BUCKET` |
| `Seg_Storage__ServiceUrl` | AppHost → LocalStack | **unset** (real AWS) |
| `Seg_Storage__ObjectLockEnabled` | `appsettings.Development.json` (`false`) | `appsettings.json` (`true`) |
| `Seg_Queues__Runs` / `__Bulk` / `__BatchPoll` | AppHost (LocalStack queue URLs) | compose ← `.env` |
| `Seg_Queues__MaxConcurrentRuns` | `appsettings.Development.json` (`2`) | compose (`8`; tune with Host B sizing) |
| `Seg_Pipeline__*` | `appsettings.json` | same, overridable per environment |
| `Seg_Routing__*` | `appsettings.json` | same |
| `Seg_Providers__<provider>__ApiKey` | Aspire parameter ← user secrets | Secrets Manager, read in-process |
| `Seg_Providers__<provider>__BaseUrl` | `appsettings.json` | same |
| `ConnectionStrings__segmentation`, `ConnectionStrings__redis` | Aspire references | compose |
| `Secrets:SecretId` / `Secrets:Prefix` | unused (Production only) | `appsettings.json` (`protofast/app`, `Seg_`) |
| `AWS_REGION` / `AWS_DEFAULT_REGION` | AppHost (`us-east-1`) | compose ← `.env` |

`api` additionally reads `Api_Segmentation__Bucket`, `__Runs`, `__Bulk`, `__BatchPoll`,
`__ReviewerRole`, `__AdminRole`, `__MaxUploadBytes`, `__AllowedAugmentations`, and
`ConnectionStrings__segmentation`. It presigns, enqueues and reads Postgres; it holds no provider
credentials and references neither the routing nor the pipeline project.

### `envoy`

`ENVOY_MODE`, `CLIENTS`, `DEFAULT_CLIENT`, `PORT` / `CLIENT_<NAME>_LISTENER_PORT`,
`CLIENT_<NAME>_DOMAIN`, `CLIENT_<NAME>_HOST/_PORT`, `CLIENTS_HOST_HOST/_PORT`,
`AUTH_HOST/_PORT`, `PAYMENTS_HOST/_PORT`, `API_HOST/_PORT`,
`KEYCLOAK_HOST/_PORT/_DOMAIN`, `OTEL_GRPC_HOST/_PORT`, `OTEL_HTTP_HOST/_PORT`,
`OTEL_INSTANCE_ID`, `ENVOY_ADMIN_PORT`, `ENVOY_TLS_CERT`, `ENVOY_TLS_KEY`.
Details in [layer 04](04-edge.md).

### Clients and the SSR host

`SERVER_URL`, `NG_ALLOWED_HOSTS`, `NG_TRUST_PROXY_HEADERS`, `SERVER_OTEL_ENDPOINT`,
`BROWSER_OTEL_ENDPOINT`, `PORT`, `SSL_CERT`/`SSL_KEY` (dev), and for the host
`CLIENTS`, `DEFAULT_CLIENT`, `ASSETS_BUCKET`, `ASSETS_DIR`, `CLIENT_<NAME>_TAG`.
Details in [layer 03](03-services-and-clients.md).

### Keycloak

`KC_DB`, `KC_DB_URL`, `KC_DB_USERNAME`, `KC_DB_PASSWORD` (from a mounted secret),
`KC_HOSTNAME`, `KC_HOSTNAME_BACKCHANNEL_DYNAMIC`, `KC_PROXY_HEADERS`,
`KC_HTTP_ENABLED`, `KC_HEALTH_ENABLED`, `KC_SPI_THEME__STATIC_MAX_AGE`,
`KC_FEATURES`, `KC_TELEMETRY_LOGS_*`, `KC_TRACING_*`, `JAVA_OPTS_APPEND`, plus the
realm-import placeholders (`*_CLIENT_SECRET`, `*_BASE_URL`, `BACKCHANNEL_LOGOUT_URL`,
`SMTP_*`, `GOOGLE_*`, `APPLE_*`, `WEBAUTHN_RP_ID`). Details in
[layer 05](05-identity.md).

### `/opt/protofast/.env` (production, both hosts)

`ECR`, `AWS_REGION`, `HOST_ROLE`, `HOST_A_IP` / `HOST_B_IP`, `CLIENTS`,
`DEFAULT_CLIENT`, `ASSETS_BUCKET`, `CLIENT_ADMIN_DOMAIN`, `CLIENT_PROTOFAST_DOMAIN`,
`CLIENT_THEPLOT_DOMAIN`, `THEPLOT_DOMAIN`, `KEYCLOAK_DOMAIN`, plus the secret-derived values
`AUTH_DB_PASSWORD`, `SEGMENTATION_DB_PASSWORD`, `PROTOFAST_WEB_CLIENT_SECRET`,
`THEPLOT_WEB_CLIENT_SECRET`, `ADMIN_CLIENT_SECRET`, `ACCOUNT_ADMIN_CLIENT_SECRET`,
`INTERNAL_JWT_KEY_ID`, `SMTP_*`, `GOOGLE_*`, `APPLE_*`.

Host B also carries `SEGMENTATION_BUCKET`, `SEGMENTATION_RUNS_QUEUE_URL`,
`SEGMENTATION_BULK_QUEUE_URL` and `SEGMENTATION_BATCH_POLL_QUEUE_URL`, seeded by cloud-init from
the Terraform outputs. Host A needs only `THEPLOT_DOMAIN`.

### `/opt/protofast/versions.env` (production)

One tag per component: `AUTH_TAG`, `AUTH_MIGRATIONS_TAG`, `PAYMENTS_TAG`, `API_TAG`,
`SEGMENTATION_TAG`, `SEGMENTATION_MIGRATIONS_TAG`, `ENVOY_TAG`, `OTEL_TAG`,
`CLIENTS_HOST_TAG`, `CLIENT_ADMIN_TAG`, `CLIENT_PROTOFAST_TAG`, `CLIENT_THEPLOT_TAG`,
`KEYCLOAK_TAG`, `POSTGRES_TAG`, `REDIS_TAG`, `CLOUDFLARED_TAG`, `ASPIRE_TAG`.

### GitHub repo settings

Variables: `AWS_REGION`, `TFSTATE_BUCKET`, `ASSETS_BUCKET`, `SEGMENTATION_BUCKET`.
Secrets: `AWS_INFRA_ROLE_ARN`, `AWS_DEPLOY_ROLE_ARN`, `ECR_REGISTRY`,
`CLOUDFLARE_API_TOKEN`, `CLOUDFLARE_ACCOUNT_ID`, `CLOUDFLARE_ZONE`, `ADMIN_DOMAIN`,
`PROTOFAST_DOMAIN`, `THEPLOT_DOMAIN`, `KEYCLOAK_DOMAIN`, optional `TELEMETRY_DOMAIN` and
`TELEMETRY_ACCESS_EMAILS`.

## File map

```
apphost/                  Aspire AppHost — the dev environment in C#
clients/<name>/           Angular SSR clients
clients/host/             unified SSR host image (pulls client assets from S3)
proxy/                    Envoy templates + entrypoint
services/auth/            BFF: sign-in, sessions, accounts, ext_authz
services/payments|api/    gRPC services behind the internal JWT
services/segmentation/    document segmentation: pure core, MAF pipeline, provider routing,
                          S3/SQS storage, EF Core data, the SQS worker, and segctl
services/shared/          ServiceDefaults (telemetry, health, secrets, internal JWT)
infra/                    Terraform: AWS + Cloudflare (run in CI)
infra/bootstrap/          one-time local Terraform: state bucket + OIDC roles
infra/identity-center/    one-time local Terraform: SSO permission sets, SES user
infra/keycloak/           realm source of truth + Java providers
infra/templates/          cloud-init for both hosts
deploy/                   compose files, deploy.sh, Keycloak realm/theme/JAR bundle
otel-collector/           collector image + pipeline config
scripts/                  secrets, dev helpers, Keycloak apply scripts
.github/workflows/        one workflow per component + infra
```

## Ports

| Port | Where | What |
|---|---|---|
| 20000 / 20001 / 20002 | dev, host | Envoy listeners for `admin` / `protofast` / `theplot` |
| 8443 | prod, Host A | Envoy publish listener (tunnel target) |
| 9901 | both | Envoy admin (`/ready`) |
| 4000 | both | unified SSR clients host |
| 8080 / 8081 / 8082 | prod, Host B | auth / payments / api (gRPC, cross-host) |
| 8083 | prod, Host B | Keycloak HTTP (cross-host, via Envoy's vhost only) |
| 4317 / 4318 | prod, Host A | OTLP gRPC / HTTP receivers |
| 5432, 6379 | prod, Host B | Postgres, Redis — **not** published |
| — | prod, Host B | `segmentation` publishes **no** port; nothing dials it. It pulls from SQS and writes to S3 and Postgres |
| 4566 | dev, host | LocalStack (S3 + SQS) |
