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
| `Seg_Storage__Region` | AppHost (the `developer` profile's region, which LocalStack's init script also creates in) | unused (the SDK resolves the region) |
| `Seg_Storage__ObjectLockEnabled` | `appsettings.Development.json` (`false`) | `appsettings.json` (`true`) |
| `Seg_Queues__Runs` / `__Bulk` / `__BatchPoll` | AppHost (LocalStack queue URLs) | compose ← `.env` |
| `Seg_Queues__MaxConcurrentRuns` | `appsettings.Development.json` (`2`) | compose (`8`; tune with Host B sizing) |
| `Seg_Conversion__Endpoint` | AppHost (the `conversion` resource's endpoint) | compose (`http://conversion:8090`) |
| `Seg_Conversion__Timeout` | `appsettings.json` (`00:10:00`) | compose ← `.env` `SEGMENTATION_CONVERSION_TIMEOUT` |
| `Seg_Conversion__OcrEnabled` / `__OcrLanguages__0` / `__MaxOcrPages` | `appsettings.json` (`true`, `eng`, `200`) | compose ← `.env` |
| `Seg_Pipeline__*` | `appsettings.json` | same, overridable per environment |
| `Seg_Routing__*` | `appsettings.json` | same |
| `Seg_Providers__<provider>__ApiKey` | Aspire parameter ← user secrets | Secrets Manager, read in-process |
| `Seg_Providers__<provider>__BaseUrl` | `appsettings.json` | same |
| `ConnectionStrings__segmentation`, `ConnectionStrings__redis` | Aspire references | compose |
| `Secrets:SecretId` / `Secrets:Prefix` | unused (Production only) | `appsettings.json` (`protofast/app`, `Seg_`) |
| `AWS_REGION` / `AWS_DEFAULT_REGION` | AppHost `WithSsoProfile` (the `developer` profile's region, for Secrets Manager) | compose ← `.env` |
| `OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT` | AppHost (`true`, run mode only) | **unset** — the prompts are document text |

`OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT` is the OpenTelemetry semantic-convention
switch for prompt and completion bodies. Set to `true` (the only value either layer accepts), every
model call's messages appear as `gen_ai.input.messages` / `gen_ai.output.messages` on its span, and
the workflow's executor spans carry the payloads on their edges. Microsoft.Extensions.AI reads the
variable itself; `GenAiTelemetry` re-reads it for the workflow instrumentation, which has no
default of its own. Left unset, both layers still emit spans — model, provider, tokens, latency,
graph — just without the text.

`Seg_Storage__Region` / `Api_Segmentation__Region` is what the LocalStack clients sign with, and
the AppHost hands the same value to the init script's `AWS_DEFAULT_REGION` so both sides agree —
the emulator keeps queues per region, so a mismatch is a `QueueDoesNotExist` against a queue that
was created. The value follows the `developer` profile's region (`us-west-2`, the workload's own
region), so it also matches the `AWS_REGION` that `WithSsoProfile` sets for Secrets Manager.

`Seg_Conversion__Endpoint` left empty disables conversion: Markdown and plain-text uploads still
run end to end, and anything else fails phase 0 with a message saying so rather than being ingested
as if it were already Markdown. That is the state a fresh clone is in only if the `conversion`
resource failed to start — the AppHost sets the variable and the worker waits for it.

`api` additionally reads `Api_Segmentation__Bucket`, `__Region`, `__Runs`, `__Bulk`, `__BatchPoll`,
`__ReviewerRole`, `__AdminRole`, `__MaxUploadBytes`, `__AllowedAugmentations`, and
`ConnectionStrings__segmentation`. It presigns, enqueues and reads Postgres; it holds no provider
credentials and references neither the routing nor the pipeline project.

`Api_Segmentation__MaxUploadBytes` is **10 MiB** (`10485760`) and is not only a check: it becomes
the `content-length-range` condition of the signed POST policy, which S3 evaluates against the
bytes that actually arrive. Raising it here raises what the bucket accepts; a client that ignores
it is refused with `EntityTooLarge` (ingest plan §7).

### `conversion`

The document-conversion sidecar (ingest plan §17.3). It is not a .NET process, so it does not
follow the `Prefix_Section__Key` convention — the same deviation the Envoy and OTel containers
already make. It holds **no secrets and no credentials**: S3 access comes from the instance role
over IMDS in production and from LocalStack's throwaway pair in development.

| Variable | Dev source | Prod source |
|---|---|---|
| `CONVERSION_BUCKET` | AppHost (LocalStack bucket) | compose ← `.env` `SEGMENTATION_BUCKET` |
| `CONVERSION_S3_ENDPOINT` | AppHost → LocalStack, by container DNS | **unset** (real AWS) |
| `AWS_REGION` / `AWS_DEFAULT_REGION` | AppHost | compose ← `.env` |
| `CONVERSION_MAX_SOURCE_BYTES` | default (`10485760`) | compose (same) |
| `CONVERSION_MAX_OUTPUT_BYTES` | default (`26214400`) | compose (same) |
| `CONVERSION_TIMEOUT_SECONDS` | default (`480`) | compose (same) |
| `CONVERSION_OCR_LANGUAGES` | default (`eng`) | compose ← `.env` |
| `CONVERSION_OCR_MIN_CONFIDENCE` | default (`60`) | compose ← `.env` |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | AppHost | compose (Host A's collector) |

`CONVERSION_S3_ENDPOINT` points at LocalStack by **container DNS**, not at
`https://localhost.localstack.cloud:4566` — that hostname resolves to 127.0.0.1, which inside the
converter's container is the converter. Plain HTTP for the same reason it is HTTPS elsewhere:
nothing here is a browser on an HTTPS page, so there is no mixed content to avoid.

`CONVERSION_MAX_SOURCE_BYTES` is a backstop, not the limit. The POST policy already refused
anything larger at the edge of the platform; this catches an object written before the limit was
lowered, and it is checked against S3's `ContentLength` before any bytes are loaded.

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
services/conversion/      Python conversion sidecar: MarkItDown, CPU OCR, layout extraction
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
| 8090 | dev + prod, Host B | `conversion`, **unpublished** in prod: only the segmentation worker on the same host dials it, by compose DNS. In dev Aspire allocates a host port so the worker (a host process) can reach it |
| 4566 | dev, host | LocalStack (S3 + SQS), plain HTTP and TLS on the same port — clients use `https://localhost.localstack.cloud:4566` (LocalStack's own publicly-trusted certificate) so the browser's presigned `POST` from an HTTPS client page isn't blocked as mixed content |
| 5000 | dev, host | smtp4dev web UI |
