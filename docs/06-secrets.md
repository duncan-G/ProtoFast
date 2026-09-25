# 06 · Secrets

*Where secret values live, how they get to a process, and what to do when you add
or rotate one. Self-contained.*

## Two secrets, one layout

Production holds **every** runtime secret in AWS Secrets Manager secret
`protofast/app`. Local development uses a sibling secret, `protofast/dev`, with
the same flat JSON map whose keys are prefixed by audience:

```json
{
  "Infra_KcDbPassword": "…",
  "Auth_DbPassword": "…",
  "Api_DbPassword": "…",
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

**Terraform creates both secret shells and never a version.** The CI infra role is
explicitly denied `GetSecretValue`/`PutSecretValue`, so no secret value ever passes
through CI or lands in Terraform state. Values are written out of band:

```bash
scripts/generate-dev-secrets.sh                      # protofast/dev: JWT pair + Keycloak client secrets
scripts/populate-secrets.sh Payments_StripeKey=sk_test_...
scripts/populate-secrets.sh --prod Payments_StripeKey=sk_live_...   # protofast/app: generate missing DB passwords
```

`populate-secrets.sh` is additive and idempotent: it merges your `Key=value`
arguments into the current map and, for `protofast/app` only, generates a fresh
32-character password for any *managed* key that is still missing
(`Infra_KcDbPassword`, `Auth_DbPassword`, `Api_DbPassword`). First-time local values come from
`scripts/generate-dev-secrets.sh` — JWT pair, `Auth_InternalJwt__KeyId`, and the
three Keycloak client secrets. SES SMTP and the DB passwords stay out of the
DEV map — local mail is smtp4dev, local databases are Aspire.

> Because no value is in Terraform state, replacing or destroying the secret loses
> everything in it. Recreate by re-running the script.

## How a value reaches a process

There are three delivery paths, chosen by what the consumer can accept.

**1. The service reads Secrets Manager itself.** `auth`, `payments` and `api`
each add the Secrets Manager configuration provider in `Program.cs`.
`appsettings.json` points at `protofast/app`; `appsettings.Development.json`
overlays `protofast/dev`:

```json
"Secrets": { "SecretId": "protofast/app", "Prefix": "Auth_" }
```

The secret must exist and have a current version or startup fails.

Every prefix-matched key becomes configuration with the prefix stripped, so
`Auth_InternalJwt__PrivateKeyPem` arrives as `InternalJwt:PrivateKeyPem` (and
`Payments_` / `Api_` / `Shared_` the same way). In production, credentials come
from the instance role over IMDS, which is why both instances set a metadata
hop limit of 2 (containers are one hop behind the host) and why `AWS_REGION` is
injected — the SDK resolves no region on its own. In development the AppHost
logs into SSO profile `developer` before starting the services; they then use
that profile to read `protofast/dev`.

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
| Dev | `Auth_InternalJwt__PrivateKeyPem` in `protofast/dev`, read in-process by `auth` | `Shared_InternalJwt__PublicKeyPem` in the same secret, read in-process by `payments` and `api` |
| Prod | `Auth_InternalJwt__PrivateKeyPem` in `protofast/app`, read in-process by `auth` | written to `/opt/protofast/internal-jwt-pub` and mounted read-only |

The private key deliberately never becomes a file, an env var or a host copy.
Note that `InternalJwt:PrivateKeyPemFile` **shadows** `PrivateKeyPem` when set — so
prod must leave it empty.

## Development secrets

On `aspire run` the AppHost authenticates to AWS with SSO profile **`developer`**
(`aws sso login` if the session is expired) so the services can read
`protofast/dev` in-process the same way they read `protofast/app` in production.
The DEV secret must have a current version with the JWT pair and Keycloak client
secrets; startup fails without them. The AppHost still injects non-secrets:
Keycloak's URL, smtp4dev's allocated SMTP host/port, and Aspire's Postgres/Redis
connection strings.

Configure the profile once, choosing the **Developer** permission set:

```bash
aws configure sso --profile developer
aws configure set region <region> --profile developer
```

The region is a separate step: `aws configure sso` only writes `sso_region` (the
Identity Center region), and the AWS SDK does not read that as the client region.
Without it every service fails at startup with `No RegionEndpoint or ServiceURL
configured`. Use the region the `protofast/*` secrets live in.

Populate as that identity. Generate the local JWT pair and Keycloak secrets
once, then add any extras:

```bash
export AWS_PROFILE=developer
scripts/generate-dev-secrets.sh
scripts/populate-secrets.sh Payments_StripeKey=sk_test_...
```

The Developer SSO set can Get/Put `protofast/dev` and is explicitly denied the
value APIs on `protofast/app`. After the first `infra.yml` apply, run
`generate-dev-secrets.sh` once. What it writes:

- **Internal JWT:** an EC P-256 pair (`Auth_InternalJwt__PrivateKeyPem` /
  `Shared_InternalJwt__PublicKeyPem`) and `Auth_InternalJwt__KeyId=dev-1`.
- **Keycloak client secrets:** the realm-import defaults
  (`dev-protofast-web-secret`, `dev-admin-secret`, `dev-account-admin-secret`)
  so Keycloak and auth-svc agree without the AppHost injecting them.

Left out of the DEV map on purpose:

- Mail: smtp4dev; the AppHost injects host/port. No SES credentials.
- Database passwords: managed by Aspire's Postgres resource.

## Adding or rotating a secret

1. Add the key to the right secret:
   `scripts/populate-secrets.sh Auth_Foo__Bar=value` (local) or
   `scripts/populate-secrets.sh --prod Auth_Foo__Bar=value` (prod).
   Local JWT + Keycloak secrets: `scripts/generate-dev-secrets.sh`. Prod DB
   passwords: add the key to `MANAGED_KEYS` and run `--prod`.
2. Decide the delivery path:
   - needed by `auth`? Prefix it `Auth_` — nothing else to do.
   - needed by Keycloak or by compose interpolation? Add a line to
     `ensure_secret_files`/the `.env` seeding block in `deploy/deploy.sh`, and
     reference `${YOUR_VAR}` from the compose file.
   - needed by `payments`/`api`? Prefix it `Payments_` / `Api_` (or `Shared_`) —
     the in-process provider picks it up in both environments.
3. Deploy the affected component — a same-tag apply still recreates a container
   whose resolved compose config changed.

Values must avoid `;`, `=` and shell/URL metacharacters: they travel through env
files, a Postgres connection string and a JDBC URL. `populate-secrets.sh` enforces
this for the passwords it generates.
