# 06 · Secrets

*Where secret values live, how they get to a process, and what to do when you add or rotate
one. Self-contained.*

## One secret per environment

Each deployed environment's account holds every runtime secret in one AWS Secrets Manager
secret, `protofast/<environment>`: `protofast/prod` in prod. It is one JSON object of keys,
each starting with who reads it
([ProtoStrap's secrets](https://github.com/duncan-G/protostrap/blob/v0.1.12/docs/secrets.md)):

| Prefix                 | Read by                                                          |
| ---------------------- | ---------------------------------------------------------------- |
| `Shared_`              | any component that lists the key in its spec's `secrets`         |
| `<Component>_`         | that component: its name in PascalCase, such as `Auth_`, `DocumentImport_`, `PostgresConfig_`; keycloak-config shares `Keycloak_` |
| `Edge_`                | ProtoStrap's edge: `Edge_TUNNEL_TOKEN`                           |
| `Reporter_`            | ProtoStrap's daily report: `Reporter_ANTHROPIC_API_KEY`, optional |

ProtoFast's keys:

| Key                                                    | Read by                                      | Made by `set-secrets.sh` |
| ------------------------------------------------------ | -------------------------------------------- | ------------------------ |
| `Postgres_POSTGRES_PASSWORD`                           | postgres (secret file)                       | yes                      |
| `PostgresConfig_ConnectionStrings__postgres`           | postgres-config: the superuser's connection  | yes                      |
| `Shared_ConnectionStrings__keycloak`                   | keycloak (secret file), postgres-config      | yes                      |
| `Shared_ConnectionStrings__auth`                       | auth, auth-migrations, postgres-config       | yes                      |
| `Shared_ConnectionStrings__protofast`                  | api, document-import, protofast-migrations, postgres-config | yes       |
| `Redis_REDIS_PASSWORD`                                 | redis (secret file)                          | yes                      |
| `Shared_ConnectionStrings__redis`                      | auth, api                                    | yes                      |
| `Keycloak_KC_BOOTSTRAP_ADMIN_CLIENT_SECRET`            | keycloak, keycloak-config (secret files): the `protofast-admin` client in `master` | yes, once |
| `Shared_Keycloak__ClientSecretProtofastWeb`, `…ClientSecretTheplotWeb`, `…ClientSecretOfftheloopWeb`, `…ClientSecretAdmin` | auth, keycloak-config (secret files) | yes |
| `Shared_Keycloak__AdminClientSecret`                   | auth, keycloak-config: every realm's `account-admin` client | yes       |
| `Auth_InternalJwt__PrivateKeyPem`, `Auth_InternalJwt__KeyId` | auth                                   | yes                      |
| `Api_InternalJwt__PublicKeyPem`, `Payments_InternalJwt__PublicKeyPem` | api, payments: a copy each, so a rotation replaces both | yes |
| `DocumentImport_Providers__deepseek__ApiKey`, `…gemini__ApiKey`, `…anthropic__ApiKey` | document-import | no: set the ones the environment uses |
| `Api_Providers__deepseek__ApiKey`                      | api: writing in OffTheLoop's notes           | no: without it the notes' AI answers unavailable |
| `Api_Providers__gemini__ApiKey`                        | api: OffTheLoop's company and posting research | no: without it research answers unavailable |
| `Edge_TUNNEL_TOKEN`                                    | ProtoStrap's edge                            | no: ProtoStrap's `set-edge-token.sh` |

The connection strings name components on the overlay: `Host=postgres;Port=5432;Database=<db>;Username=<db>;Password=…;GSS Encryption Mode=Disable`
for each database, and `redis:6379,password=…` for Redis. Each database has its own user,
which postgres-config makes from its connection string, so no service holds the superuser's
password.

**Terraform creates the secret empty and never writes a value**, so no value passes through
Terraform's state, CI or an image. An Org Admin fills it in from their own machine.

## Dev

Dev is the local run, with no account of its own. Its secret, `protofast/dev`, sits beside
`protofast/prod` in ProtoFast's one account, named like it. The services read it as they read
prod's: under `aspire run` ProtoStrap.Hosting's environment gives each .NET process
`SECRET_ID=protofast/dev`, its `SECRET_PREFIX`, `AWS_REGION` and `AWS_PROFILE=developer`, and
`AddProtoStrapSecret` reads it with the SDK's default chain, which finds your SSO profile where
a replica finds its role ([ProtoStrap's dev
secret](https://github.com/duncan-G/protostrap/blob/v0.1.12/docs/secrets.md#dev),
[layer 02](02-local-development.md#local-secrets-and-keys)).

It holds only what the processes read: the internal JWT and the model APIs' keys. The local
resources' keys are Aspire's, generated and kept in the AppHost's user secrets: the connection
strings, PostgreSQL's and Redis's passwords, and Keycloak's bootstrap client, `admin` password
and client secrets, which keycloak and keycloak-config can't read from Secrets Manager. A
Keycloak client secret left in `protofast/dev` would reach auth over Aspire's, so
`scripts/set-secrets.sh dev` removes any it finds.

No Terraform makes it. A Developer makes it once for the team and fills it, with the
Developer permission set:

```bash
aws secretsmanager create-secret --region us-west-2 --name protofast/dev
```

```bash
AWS_PROFILE=developer AWS_REGION=us-west-2 scripts/set-secrets.sh dev
```

The model API keys are empty until someone sets theirs. Don't tag it `protostrap:app`: the
Developer permission set can't read a secret with that tag, which is how it keeps Developers
out of prod's.

## Filling an environment's secret

```bash
scripts/set-secrets.sh prod                     # every key the secret lacks
scripts/set-secrets.sh prod --rotate redis      # new values for a group of keys
```

[`scripts/set-secrets.sh`](../scripts/set-secrets.sh) makes every password and key itself, in
a private temporary directory, and hands them to ProtoStrap's `scripts/set-secret.sh` as
files, so no value is shown, typed, logged or put on a command line. One run is one new
version of the secret. It reads only the secret's key names, never their values, to see what
is missing. It needs jq, openssl, the AWS CLI with the Org Admin's credentials, and a
ProtoStrap checkout (`PROTOSTRAP_SOURCE`, `.protostrap` or `../ProtoStrap`).

| What it makes                       | How                                                         |
| ----------------------------------- | ----------------------------------------------------------- |
| Passwords                           | `openssl rand -hex 24`: no characters a connection string or a URL would need escaped |
| Connection strings                  | each with its database's user and password, and the superuser's for postgres-config |
| The internal JWT pair               | EC P-256, PKCS#8 private key; key id `<environment>-<UTC minute>`, new with every pair |

`--rotate` groups: `postgres` (the superuser), `keycloak-db`, `auth-db`, `protofast-db`,
`redis`, `clients` (the four Keycloak client secrets) and `internal-jwt` (the pair and its
key id). Each rotates every key that must change with it in the same run.

Keycloak's bootstrap admin client isn't rotated: Keycloak makes it once, when it first makes
its `master` realm. A new `Keycloak_KC_BOOTSTRAP_ADMIN_CLIENT_SECRET` written later doesn't
reach Keycloak's database, so keycloak-config can't log in until the client's secret is
changed to match by hand.

The other keys: the tunnel's token, once the first infra apply has made the tunnel, with
`CLOUDFLARE_ACCOUNT_ID` and a `CLOUDFLARE_API_TOKEN` that may edit tunnels (the infra
workflow's has that) set beside the Org Admin's AWS credentials; and the model APIs' keys prod
uses:

```bash
../ProtoStrap/scripts/set-edge-token.sh protofast prod
../ProtoStrap/scripts/set-secret.sh protofast/prod DocumentImport_Providers__deepseek__ApiKey=-
```

`KEY=-` asks for the value without echoing it.

[`scripts/fill-prod-secret.sh`](../scripts/fill-prod-secret.sh) does all three for prod in one
go, setting only what the secret lacks, and fails listing any required key still missing.

## How a value reaches a process

**1. The service reads the secret itself.** Every .NET component (auth, the api, payments,
document-import and the three .NET tasks) calls `AddProtoStrapSecret`
([`services/shared/ProtoStrap`](../services/shared/ProtoStrap/ProtoStrapSecret.cs)) at
startup. With `SECRET_ID` and `SECRET_PREFIX` from ProtoStrap, and the component's role, it
reads the secret once, keeps the `Shared_` keys and its own, drops the prefixes (its own win),
drops empty values, and adds them as configuration: `Auth_InternalJwt__PrivateKeyPem` arrives
as `InternalJwt:PrivateKeyPem`. A service reads its keys only when it starts; ProtoStrap
replaces its replicas when they change.

**2. Secret files, for images that can't read AWS.** keycloak, keycloak-config, postgres and
redis have `secret_files`: ProtoStrap's agent writes each key the component lists, prefix
dropped, to `/run/secrets/<KEY>` on tmpfs, read-only, with no trailing newline. A listed key
that's missing fails the replica.

| Component         | Files                                                              | Read by                          |
| ----------------- | ------------------------------------------------------------------ | -------------------------------- |
| `postgres`        | `POSTGRES_PASSWORD`                                                | `POSTGRES_PASSWORD_FILE`, on an empty data directory |
| `redis`           | `REDIS_PASSWORD`                                                   | its command: `--requirepass "$(cat /run/secrets/REDIS_PASSWORD)"` |
| `keycloak`        | `ConnectionStrings__keycloak`, `KC_BOOTSTRAP_ADMIN_CLIENT_SECRET`  | its entrypoint, which turns them into `KC_DB_*` and the bootstrap client ([layer 05](05-identity.md#2-its-entrypoint-and-variables)) |
| `keycloak-config` | `KC_BOOTSTRAP_ADMIN_CLIENT_SECRET`, `Keycloak__ClientSecretProtofastWeb`, `…TheplotWeb`, `…OfftheloopWeb`, `…Admin`, `Keycloak__AdminClientSecret` | its entrypoint, as variables the realm files substitute |

**3. Locally, the services the same way, from dev's secret.** Under `aspire run`, `SECRET_ID`
is `protofast/dev` and `AWS_PROFILE` your sign-in. The containers with `secret_files` are local
resources there, and take Aspire's parameters as variables, which their entrypoints read
before any file ([Dev](#dev), [layer 02](02-local-development.md#local-secrets-and-keys)).

## Changing a value

Write the new value with `set-secrets.sh --rotate <group>` or ProtoStrap's `set-secret.sh`.
ProtoStrap notices within a minute, waits two minutes for the secret to settle, then applies
it, in `depends_on` order
([changing a value](https://github.com/duncan-G/protostrap/blob/v0.1.12/docs/secrets.md#changing-a-value)):

| Component           | Takes a new key by                                                    |
| ------------------- | --------------------------------------------------------------------- |
| postgres            | its `on_secret_change` command: `ALTER ROLE postgres PASSWORD` from the new file. It never restarts |
| redis               | its command: `CONFIG SET requirepass`, logged in with the old password if the new one fails |
| postgres-config     | it runs again (a configure task's keys changed), giving each database's user the password in its new connection string, before the services that connect get it |
| keycloak-config     | it runs again, setting the new client secrets on the realms before auth gets them |
| keycloak            | its replicas are replaced on a change to any `Keycloak_` key: its own prefix, from its name, is the one keycloak-config's keys use |
| everything else     | its replicas are replaced, a quarter at a time                         |

There is no going back to an old value: once PostgreSQL has a new password, the old one no
longer works. A wrong value is fixed by writing the right one.

## The internal JWT keys

|        | Private key                                                  | Public key                                       |
| ------ | ------------------------------------------------------------ | ------------------------------------------------ |
| Locally | `protofast/dev`'s `Auth_InternalJwt__PrivateKeyPem`, read in-process by auth only | `protofast/dev`'s `Api_InternalJwt__PublicKeyPem` and `Payments_InternalJwt__PublicKeyPem`, read in-process by the api and payments |
| In an account | `Auth_InternalJwt__PrivateKeyPem`, read in-process by auth only | `Api_InternalJwt__PublicKeyPem` and `Payments_InternalJwt__PublicKeyPem`, read in-process by the api and payments |

A rotation writes the private key, both copies of the public key and a new key id in one run. Tokens live
five minutes.

## Who can read it

Secrets Manager can't grant one key alone: every component's role can read every key in the
environment's secret, and so can the edge's role and data-1's. Platform Admins and Developers
can't read it; Org Admins set it. ProtoStrap names this an accepted risk
([who can read it](https://github.com/duncan-G/protostrap/blob/v0.1.12/docs/secrets.md#who-can-read-it)),
which is why each database has its own user rather than every service holding the
superuser's password.

> Because no value is in Terraform's state, nothing else holds the secret's values. A
> destroyed secret can be restored for 7 days, its recovery window. After that,
> `set-secrets.sh` makes a fresh set, which ProtoStrap applies like a rotation of every key,
> but not all of it: the tunnel's token and the model APIs' keys must be set again, and
> Keycloak's database keeps the old bootstrap admin client secret, so keycloak-config can't
> log in until it is changed by hand ([above](#filling-an-environments-secret)).

## Adding a secret

1. **A .NET component's key needs nothing in the AppHost.** It reads every key under its own
   prefix, and a change to one replaces its replicas. Give each reader its own copy, under its
   prefix, rather than a `Shared_` key: a `Shared_` key reaches only the components whose spec
   lists it, as references list the connection strings.
2. **A key for keycloak or keycloak-config goes in the AppHost.** Pass `Generated("name")` with
   `WithEnvironment("KEY", parameter)`: in the spec it names the component's secret file, and
   under `aspire run` it is Aspire's value, kept out of `protofast/dev`. Then write the spec
   (`aspire do protostrap-spec`).
3. **Set it in each environment's secret** under the reader's prefix, and in `protofast/dev`
   for a .NET component. A key that can be made belongs in `scripts/set-secrets.sh`; otherwise
   `set-secret.sh protofast/<environment> Prefix_Section__Key=-`.
4. **Set it before the release that reads it.** A .NET service reads whatever it finds; a
   component with `secret_files` fails if a listed key is missing.

## Known gaps

- **Unverified.** `scripts/set-secrets.sh` hasn't run against an account yet, and no component
  has read the secret or its secret files there.
