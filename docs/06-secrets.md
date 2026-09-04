# 06 · Secrets

*Where secret values live, how they get to a process, and what to do when you add
or rotate one. Self-contained.*

## One secret, one place

Production holds **every** secret in a single AWS Secrets Manager secret named
`protofast/app`. Its value is a flat JSON map whose keys are prefixed by audience:

```json
{
  "Infra_KcDbPassword": "…",
  "Auth_DbPassword": "…",
  "Auth_Keycloak__ClientSecretProtofastWeb": "…",
  "Auth_InternalJwt__PrivateKeyPem": "-----BEGIN PRIVATE KEY-----…",
  "Auth_Smtp__Password": "…",
  "Payments_StripeKey": "…"
}
```

| Prefix | Audience |
|---|---|
| `Infra_` | host-level plumbing (the Postgres superuser password) |
| `Auth_` | the auth service (and, via `deploy.sh`, Keycloak's own inputs) |
| `Payments_`, `Api_` | those services |
| `Shared_` | every backend |

**Terraform creates the secret shell and never a version.** The CI infra role is
explicitly denied `GetSecretValue`/`PutSecretValue`, so no secret value ever passes
through CI or lands in Terraform state. Values are written out of band:

```bash
scripts/populate-secrets.sh                          # generate any missing managed keys
scripts/populate-secrets.sh Payments_StripeKey=sk_live_...
```

The script is additive and idempotent: it merges your `Key=value` arguments into
the current map and generates a fresh 32-character password for any *managed* key
that is still missing (`Infra_KcDbPassword`, `Auth_DbPassword`).

> Because no value is in Terraform state, replacing or destroying the secret loses
> everything in it. Recreate by re-running the script.

## How a value reaches a process

There are exactly three delivery paths, chosen by what the consumer can accept.

**1. The service reads Secrets Manager itself.** `auth` — and only `auth` — adds
the Secrets Manager configuration provider, and only when
`ASPNETCORE_ENVIRONMENT=Production`. It is configured by `appsettings.json`:

```json
"Secrets": { "SecretId": "protofast/app", "Prefix": "Auth_" }
```

Every `Auth_`-prefixed key becomes configuration with the prefix stripped, so
`Auth_InternalJwt__PrivateKeyPem` arrives as `InternalJwt:PrivateKeyPem`. Credentials
come from the instance role over IMDS, which is why both instances set a metadata
hop limit of 2 (containers are one hop behind the host) and why `AWS_REGION` is
injected — the SDK resolves no region on its own.

**2. `deploy.sh` seeds `/opt/protofast/.env`.** On every apply, the deploy script
pulls the secret and writes single-line values into the env file that compose
interpolates: the Keycloak client secrets, `SMTP_*`, `INTERNAL_JWT_KEY_ID`,
`AUTH_DB_PASSWORD` and the optional Google/Apple credentials. These are the values
Keycloak needs, and Keycloak has no Secrets Manager provider.

**3. `deploy.sh` writes files that compose mounts as secrets.**

| File on the host | Contents | Consumed by |
|---|---|---|
| `/opt/protofast/kc-db-password` | Postgres superuser / Keycloak DB password | Postgres (`POSTGRES_PASSWORD_FILE`), Keycloak (read into `KC_DB_PASSWORD` at start) |
| `/opt/protofast/auth-db-password` | the `auth` role's password | Postgres init |
| `/opt/protofast/internal-jwt-pub` | the EC P-256 **public** key | `payments`, `api` (`Shared_InternalJwt__PublicKeyPemFile`) |
| `/opt/protofast/tunnel-token` | the Cloudflare tunnel token | `cloudflared` (root-owned, written by cloud-init) |

Every apply re-asserts these files, so a box whose first boot happened before the
secret had a value still converges.

## The internal JWT keys

| | Private key | Public key |
|---|---|---|
| Dev | generated per run by the AppHost, injected into `auth` | injected into `payments` and `api` |
| Prod | `Auth_InternalJwt__PrivateKeyPem` in Secrets Manager, read in-process by `auth` | written to `/opt/protofast/internal-jwt-pub` and mounted read-only |

The private key deliberately never becomes a file, an env var or a host copy.
Note that `InternalJwt:PrivateKeyPemFile` **shadows** `PrivateKeyPem` when set — so
prod must leave it empty.

## Development secrets

Dev has no Secrets Manager. Its secrets are non-secret by design:

- Keycloak client secrets: `dev-protofast-web-secret`, `dev-admin-secret`,
  `dev-account-admin-secret` — in `appsettings.Development.json` and as the realm
  import's placeholder defaults.
- Internal JWT: generated fresh on each `aspire run`.
- Mail: smtp4dev, no credentials.
- Database passwords: managed by Aspire's Postgres resource.

## Adding or rotating a secret

1. Add the key to `protofast/app`:
   `scripts/populate-secrets.sh Auth_Foo__Bar=value` (or let the script generate it
   if you add it to `MANAGED_KEYS`).
2. Decide the delivery path:
   - needed by `auth`? Prefix it `Auth_` — nothing else to do.
   - needed by Keycloak or by compose interpolation? Add a line to
     `ensure_secret_files`/the `.env` seeding block in `deploy/deploy.sh`, and
     reference `${YOUR_VAR}` from the compose file.
   - needed by `payments`/`api`? Prefix it `Shared_` (or `Payments_`/`Api_`) and add
     the env var to that service in compose.
3. Deploy the affected component — a same-tag apply still recreates a container
   whose resolved compose config changed.

Values must avoid `;`, `=` and shell/URL metacharacters: they travel through env
files, a Postgres connection string and a JDBC URL. `populate-secrets.sh` enforces
this for the passwords it generates.
