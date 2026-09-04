# 09 · Reference

*Lookup tables: where to change a given thing, and what every configuration
variable is. Self-contained — no reading order required.*

## Where do I change…?

| …this | Edit here | Then |
|---|---|---|
| A dev-only setting (ports, containers, injected env) | `apphost/Program.cs` | restart `aspire run` |
| A production setting for a container | `deploy/docker-compose.host-edge.yml` or `…host-services.yml` | deploy any component on that host |
| A stable production value (domains, `CLIENTS`, region) | `infra/templates/user_data.host_*.sh.tftpl` (new boxes) and `/opt/protofast/.env` (existing) | `terraform apply` and/or a deploy |
| A secret value | `scripts/populate-secrets.sh Key=value` | deploy the consuming component |
| Edge routing, CORS, allow-lists | `proxy/*.tmpl`, `proxy/entrypoint.sh` | deploy `envoy` |
| A .NET service default | that service's `appsettings.json` | deploy that service |
| A dev-only service default | `appsettings.Development.json` | restart |
| The Keycloak realm | `infra/keycloak/realms/protofast-realm.json` **and** `deploy/keycloak/realms/` | deploy `keycloak`; flows need the manual script |
| The Keycloak login theme or emails | `deploy/keycloak/themes/protofast/…` | deploy `keycloak` (dev: just refresh) |
| Keycloak's Java extensions | `infra/keycloak/providers/email-otp`, then `build.sh` | commit the JAR under `deploy/keycloak/providers`, deploy `keycloak` |
| A client's runtime behaviour | `clients/<name>/src/server.ts` or `app.config.ts` | deploy `client-<name>` |
| Which clients exist | `apphost/Program.cs` (`proxy.WithClient`), `CLIENTS`/`DEFAULT_CLIENT`, a new `deploy-client-<name>.yml` | both |
| AWS/Cloudflare resources | `infra/*.tf` | run the `infra` workflow |
| Public hostnames | repo secrets `ADMIN_DOMAIN` / `PROTOFAST_DOMAIN` / `KEYCLOAK_DOMAIN` | `infra` apply, then a deploy so `.env` catches up |
| CI/CD behaviour | `.github/workflows/_component-deploy.yml` or a `deploy-*.yml` | merge to `main` |
| On-host deploy behaviour | `deploy/deploy.sh` | any deploy ships the new copy |

## Variables by component

### `auth`

| Variable | Dev source | Prod source |
|---|---|---|
| `Auth_Keycloak__Authority` | AppHost | compose (`http://keycloak:8080`) |
| `Auth_Keycloak__PublicAuthority` | — | compose (`https://${KEYCLOAK_DOMAIN}`) |
| `Auth_Keycloak__ClientSecretProtofastWeb` / `…Admin` / `AdminClientSecret` | `appsettings.Development.json` | Secrets Manager → compose `.env` |
| `Auth_InternalJwt__PrivateKeyPem` | AppHost (generated) | Secrets Manager (in-process) |
| `Auth_InternalJwt__KeyId` | `appsettings.Development.json` | `.env` (`INTERNAL_JWT_KEY_ID`) |
| `Auth_Smtp__Host/Port/From/StartTls/User/Password` | AppHost → smtp4dev | `.env`, from Secrets Manager + SES |
| `Tenants__ByHost__<host>__Realm` / `__ClientId` / `__MaxAge` / `__AcrValues` | `appsettings.Development.json` | compose |
| `ConnectionStrings__redis`, `ConnectionStrings__auth`, `ConnectionStrings__keycloak` | Aspire references | compose |
| `Secrets:SecretId`, `Secrets:Prefix` | unused (Production only) | `appsettings.json` |
| `AWS_REGION` / `AWS_DEFAULT_REGION` | — | compose (required by the SDK) |

### `payments` and `api`

| Variable | Purpose |
|---|---|
| `Shared_InternalJwt__PublicKeyPem` (dev) / `…PublicKeyPemFile` (prod) | verify the internal JWT |
| `ASPNETCORE_HTTP_PORTS`, `ASPNETCORE_KESTREL__ENDPOINTDEFAULTS__PROTOCOLS` | gRPC over HTTP/2 on 8080 |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | telemetry; unset disables the exporters |

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
`KEYCLOAK_DOMAIN`, plus the secret-derived values `AUTH_DB_PASSWORD`,
`PROTOFAST_WEB_CLIENT_SECRET`, `ADMIN_CLIENT_SECRET`, `ACCOUNT_ADMIN_CLIENT_SECRET`,
`INTERNAL_JWT_KEY_ID`, `SMTP_*`, `GOOGLE_*`, `APPLE_*`.

### `/opt/protofast/versions.env` (production)

One tag per component: `AUTH_TAG`, `AUTH_MIGRATIONS_TAG`, `PAYMENTS_TAG`, `API_TAG`,
`ENVOY_TAG`, `OTEL_TAG`, `CLIENTS_HOST_TAG`, `CLIENT_ADMIN_TAG`,
`CLIENT_PROTOFAST_TAG`, `KEYCLOAK_TAG`, `POSTGRES_TAG`, `REDIS_TAG`,
`CLOUDFLARED_TAG`, `ASPIRE_TAG`.

### GitHub repo settings

Variables: `AWS_REGION`, `TFSTATE_BUCKET`, `ASSETS_BUCKET`.
Secrets: `AWS_INFRA_ROLE_ARN`, `AWS_DEPLOY_ROLE_ARN`, `ECR_REGISTRY`,
`CLOUDFLARE_API_TOKEN`, `CLOUDFLARE_ACCOUNT_ID`, `CLOUDFLARE_ZONE`, `ADMIN_DOMAIN`,
`PROTOFAST_DOMAIN`, `KEYCLOAK_DOMAIN`, optional `TELEMETRY_DOMAIN` and
`TELEMETRY_ACCESS_EMAILS`.

## File map

```
apphost/                  Aspire AppHost — the dev environment in C#
clients/<name>/           Angular SSR clients
clients/host/             unified SSR host image (pulls client assets from S3)
proxy/                    Envoy templates + entrypoint
services/auth/            BFF: sign-in, sessions, accounts, ext_authz
services/payments|api/    gRPC services behind the internal JWT
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
| 20000 / 20001 | dev, host | Envoy listeners for `admin` / `protofast` |
| 8443 | prod, Host A | Envoy publish listener (tunnel target) |
| 9901 | both | Envoy admin (`/ready`) |
| 4000 | both | unified SSR clients host |
| 8080 / 8081 / 8082 | prod, Host B | auth / payments / api (gRPC, cross-host) |
| 8083 | prod, Host B | Keycloak HTTP (cross-host, via Envoy's vhost only) |
| 4317 / 4318 | prod, Host A | OTLP gRPC / HTTP receivers |
| 5432, 6379 | prod, Host B | Postgres, Redis — **not** published |
