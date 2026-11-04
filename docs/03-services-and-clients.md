# 03 · Services & clients

*How each process reads its own settings: the .NET services and tasks, the conversion
service, the Angular clients, the clients host and the browsers' collector. Self-contained.*

## Where a setting comes from

A component reads four kinds of setting, by the same names locally and in an account:

| Kind                    | In an account                                                      | Locally                                       |
| ----------------------- | ------------------------------------------------------------------ | --------------------------------------------- |
| ProtoStrap's variables  | set by ProtoStrap in every replica: `HOST_<NAME>`, `ORIGIN_<NAME>`, `BUCKET_<NAME>`, `QUEUE_<NAME>_URL`, `OTEL_*`, `PROTOSTRAP_*`, `SECRET_ID`, `SECRET_PREFIX`, `AWS_REGION`, the role's credentials | set by the AppHost and ProtoStrap.Hosting: hosts and origins, LocalStack's bucket and queues, Aspire's `OTEL_*`, and `SECRET_ID`, `SECRET_PREFIX`, `AWS_REGION`, with `AWS_PROFILE`, the developer's sign-in, for the role's credentials |
| Plain settings          | the spec's `env`, from the AppHost's `WithEnvironment`             | the same `WithEnvironment` call               |
| Secrets                 | the environment's secret, read at startup or as secret files      | the same, from `protofast/dev`; Keycloak's are Aspire's |
| Defaults                | `appsettings.json` in the image                                    | the same, plus `appsettings.Development.json` |

Secrets are named like .NET configuration keys:

```
Shared_ConnectionStrings__auth
└──┬──┘ └───────┬───────┘ └┬─┘
   │            │          └── key
   │            └── section
   └── who reads it: Shared_ (any component that lists it) or a component's own prefix
```

- `__` maps to `:` in .NET configuration (`ConnectionStrings:auth`).
- The prefix is dropped when read, so the component sees the same name locally and in an
  account. The spec lists each component's keys without it (`secrets:`).
- [Layer 06](06-secrets.md) has every key and who reads it.

ProtoStrap's [application contract](https://github.com/duncan-G/protostrap/blob/v0.1.12/docs/protostrap.md#application-contract)
lists everything it sets. What ProtoFast reads of it:

| Variable                                  | Read by                                                        |
| ----------------------------------------- | -------------------------------------------------------------- |
| `HOST_<NAME>`                             | auth (tenants, the SES sender's From), the clients host (which app answers a host, Angular's allowed hosts), keycloak-config |
| `ORIGIN_<NAME>`                           | auth (`ORIGIN_AUTH`, Keycloak's public URL; the request's host's origin, for its OIDC `redirect_uri` and post-logout URL), keycloak (`KC_HOSTNAME`), keycloak-config (redirect URIs) |
| `BUCKET_DOCUMENTS`                        | api, document-import, conversion                               |
| `QUEUE_DOCUMENT_UPLOAD_URL`               | api and conversion (send), document-import (receives)          |
| `QUEUE_DOCUMENT_CONVERSION_URL`           | document-import (sends), conversion (receives)                 |
| `QUEUE_WORKFLOW_OUTCOMES_URL`             | document-import (FIFO, the URL ends in `.fifo`)                |
| `SECRET_ID`, `SECRET_PREFIX`              | every .NET component                                           |
| `PROTOSTRAP_COMPONENT`                    | readiness: a check passes only for its own name                |
| `PROTOSTRAP_REPLICA`                      | "running under ProtoStrap": logs by OTLP only, SES instead of SMTP, a 10 s drain |
| `OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_EXPORTER_OTLP_PROTOCOL`, `OTEL_SERVICE_NAME`, `OTEL_RESOURCE_ATTRIBUTES` | every component's OpenTelemetry SDK; `http://telemetry:4318`, `http/protobuf` in an account |

`DRAIN_SECONDS`, how long a component keeps serving after SIGTERM, isn't ProtoStrap's:
neither ProtoStrap nor the spec sets it, so each component uses its own default, 10 s under
ProtoStrap and 0 locally.

## .NET services

auth, the api, payments and document-import call `builder.AddServiceDefaults()`
([`services/shared/ServiceDefaults`](../services/shared/ServiceDefaults)), which gives each
what ProtoStrap's contract asks:

- **Its keys from the environment's secret.** `AddProtoStrapSecret`
  ([`services/shared/ProtoStrap`](../services/shared/ProtoStrap/ProtoStrapSecret.cs)) reads
  `SECRET_ID` once, at startup, with the component's role. It keeps the `Shared_` keys and
  those starting with `SECRET_PREFIX`, drops the prefixes (its own keys win over `Shared_`
  ones), drops empty values, and adds them last, so they win over `appsettings` and the
  environment. Without `SECRET_ID`, as locally, it adds nothing.
- **Telemetry from the `OTEL_` variables.** Traces, metrics and logs go to
  `OTEL_EXPORTER_OTLP_ENDPOINT` when it is set. Under ProtoStrap the console logger is
  removed, so each log line leaves once, by OTLP. Health checks, the OIDC callback's auto span
  and SQS long polls aren't traced.
- **Readiness.** `GET /healthz` answers 200 only when the request's Host is the component's
  name (skipped locally), the replica isn't draining, and the health checks pass (PostgreSQL
  and Redis, through the Aspire integrations). A gRPC service (`MapDefaultEndpoints(grpc:
  true)`) also maps gRPC's health service for its component's name and for `""`.
- **A drain.** On SIGTERM, readiness fails at once and the service keeps serving for
  `DRAIN_SECONDS`, then stops. Kestrel's own keep-alive (130 s) is longer than the edge's 60 s.

Configuration sources, in increasing precedence:

1. `appsettings.json`: logging, `AllowedHosts`, Kestrel `Protocols: Http2` for the api and
   payments (cleartext HTTP/2, which the edge speaks to a `grpc` service), auth's tenants, the
   api's S3 URL lifetimes, document-import's providers and concurrency.
2. `appsettings.Development.json`, locally only (`ASPNETCORE_ENVIRONMENT=Development` comes
   from the AppHost; in an account nothing sets it, so it is Production).
3. Environment variables: the spec's `env` (`HTTP_PORTS=8080`,
   `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, `Keycloak__Authority`, ...) or the AppHost's.
4. The environment's secret, in an account.

### auth's option classes

| Section          | Class                  | What it controls                                                    |
| ---------------- | ---------------------- | ------------------------------------------------------------------- |
| `Keycloak`       | `KeycloakOptions`      | back-channel `Authority`, browser-facing `PublicAuthority` (default `ORIGIN_AUTH`), the four web clients' secrets, the `account-admin` client |
| `Tenants:ByHost` | `TenantOptions`        | host name → realm and client, plus `MaxAge`, `AcrValues`, `RequiredRoles` and `Consoles` on the admin host |
| `Session`        | `SessionPolicyOptions` | cookie name (`pf_session`), 8 h idle, 7 d absolute, id rotation on refresh |
| `InternalJwt`    | `InternalJwtOptions` (auth), `InternalJwtValidationOptions` (api, payments) | ES256 key material, `kid`, issuer `protofast-auth`, audience `protofast-internal`, 5 min lifetime |
| `Smtp`           | `SmtpOptions`          | locally, the SMTP host auth sends through; in an account only `From` and `FromDisplayName` count |
| `Subscriptions`  | `SubscriptionOptions`  | whether an account must be subscribed before reaching the app (off until billing exists) |

[Layer 05](05-identity.md) says what these mean for sign-in.

### The internal JWT, in one paragraph

auth holds an EC P-256 **private** key and signs a short-lived JWT for each authenticated
request, which it hands the edge in `x-internal-jwt`; the api and payments hold only the
matching **public** key and reject any call without a valid one, in a gRPC interceptor. So a
compromised backend can read identity but can't mint it. In an account the pair is in the
environment's secret (`Auth_InternalJwt__PrivateKeyPem` and `Auth_InternalJwt__KeyId`, and a
copy of the public key for each verifier, `Api_InternalJwt__PublicKeyPem` and
`Payments_InternalJwt__PublicKeyPem`); locally in `protofast/dev`.

### The bucket and the queues

[`services/shared/Storage`](../services/shared/Storage) registers the S3 client and each SQS
queue:

- `AddS3ObjectStorage(..., bucket: "documents")` takes the bucket's name from `S3:Bucket`, or
  else `BUCKET_DOCUMENTS`, and fails at startup without either.
- `AddSqsQueue(..., queue: "document-upload")` takes the queue's URL from the options, or else
  `QUEUE_DOCUMENT_UPLOAD_URL`, so a queue is never looked up by name. A URL ending in `.fifo`
  is a FIFO queue.
- `AWS_ENDPOINT_URL_S3` and `AWS_ENDPOINT_URL_SQS`, set only locally, point both at LocalStack,
  with path-style addressing and LocalStack's credentials. In an account they are unset and
  the SDK finds the real endpoints and the component's role itself.
- The api's presigned upload and download URLs live five minutes (`S3:UploadUrlTtl`,
  `S3:DownloadUrlTtl`).

### document-import

| Setting                                  | From                                               |
| ---------------------------------------- | -------------------------------------------------- |
| `Providers:<name>:ApiKey` (deepseek, gemini, anthropic) | the secret, `DocumentImport_Providers__<name>__ApiKey`, `protofast/dev`'s locally |
| `Providers:Default`, `Providers:Fallback` | `appsettings.json`: deepseek, then gemini         |
| `Consumer:MaxConcurrentImports`          | 2 (`appsettings.json`), 8 locally                  |

It stops within 40 s of SIGTERM, inside the spec's 45 s: imports in progress are cancelled
and their messages come back once their lease lapses. It scales from zero on the
`document-upload` queue. An upload that needs converting goes to the `document-conversion`
queue, and conversion sends it back on `document-upload` with the outcome in `conversion`.

### The tasks

`auth-migrations`, `protofast-migrations` and `postgres-config` don't use ServiceDefaults.
Each calls `AddProtoStrapSecret` itself for its connection strings and exits when done:

- **The migrations** take a PostgreSQL advisory lock, apply EF Core's migrations and exit 0.
  `--rebuild-schema` drops the schema first, and is refused in Production.
  `protofast-migrations` migrates three schemas in the `protofast` database: ThePlot's `plot`,
  OffTheLoop's `loop` and the import engine's `engine`, each with its own history table.
- **postgres-config** logs in with `ConnectionStrings__postgres` (the superuser) and, for each
  database in `Databases__Names` (`keycloak,auth,protofast`), makes the user its connection
  string names, gives it the password there, and makes the database owned by it. It applies
  everything every run, and logs JSON lines on stdout.

## conversion

The Python worker is configured by environment variables alone; its
[README](../services/conversion/README.md) has every one. In short: `BUCKET_DOCUMENTS`,
`QUEUE_DOCUMENT_CONVERSION_URL` and `QUEUE_DOCUMENT_UPLOAD_URL` (required), `AWS_REGION`, the
role's credentials, `AWS_ENDPOINT_URL_S3` and `AWS_ENDPOINT_URL_SQS` locally, the `OTEL_`
variables, and `CONVERSION_*` limits with defaults. It reads no keys from the secret and
listens on no port.

## Angular clients

The clients are built once and configured at **runtime**; there is no `environment.prod.ts`
fork.

| Variable                 | Consumed by                     | Purpose                                                       |
| ------------------------ | ------------------------------- | ------------------------------------------------------------- |
| `SERVER_URL`             | SSR, handed to the browser through `TransferState` | the base of the gRPC-Web transport and of browser telemetry. Set only for the dev servers (the host's local origin); the clients image leaves it empty, and the browser uses its own origin |
| `NG_ALLOWED_HOSTS`       | `src/server.ts`, admin-kit's server | the hosts Angular's SSRF guard renders for. The clients host sets it to every `HOST_<NAME>` before loading the apps |
| `ADMIN_CONSOLE_ROLES`    | admin-kit's SSR gate            | the roles that open a console (`*` for the platform console, `admin-theplot` for theplot's). Set by the AppHost, never by a console |
| `OTEL_*`                 | `src/telemetry.server.ts` (protofast, theplot, offtheloop), `src/instrumentation.ts` (the admin consoles) | the Node SDK, configured from the variables alone; under the clients host, the host starts it once for every app |
| `PORT`                   | `ng serve`                      | the port Aspire assigns                                       |

Behaviour that lives in the SSR server rather than in config:

- **Proxy headers.** Of the `x-forwarded-*` headers, Angular trusts `x-forwarded-for` and
  `x-forwarded-proto`.
- **The protected-area gate.** protofast's `/app` and `/subscribe`, and theplot's and offtheloop's `/app`,
  need an `x-user-id` header (from auth's answer to the edge); without it SSR redirects to
  `/signin?returnUrl=…`, and those responses are `private, no-store`. admin-kit's gate does
  the same for a console, and sends an operator without its roles to `/forbidden`.
- **Static assets** are served with `max-age=1y`, since Angular hashes their names.
- **Page-load tracing.** Each render runs inside a server span (`src/lib/telemetry.ssr.ts`)
  and the page carries its traceparent in `<meta name="traceparent">`, so the browser's
  `documentLoad` span, the render and its backend calls share one trace. Browsers send their
  spans and logs to `/otlp/v1/traces` and `/otlp/v1/logs` on their own origin, as
  `protofast-client`, `theplot-client`, `offtheloop-client`, `admin-client` or
  `admin-theplot-client`.

gRPC stubs are generated from the services' `.proto` files by `buf` (`buf.gen.yaml` →
`src/lib/gen`) and committed, so the image builds from `clients/` alone. `npm start` and
`npm run generate:grpc` regenerate them; CI fails if they differ from what the protos make.
auth is deliberately not an input: browsers talk to it over plain HTTP.

## The clients host

One Node process serves several clients' SSR bundles ([`clients/host`](../clients/host)). The
image ([`clients/Dockerfile`](../clients/Dockerfile)) holds all five apps; `CLIENTS` says
which this replica loads.

| Variable          | Purpose                                                               |
| ----------------- | --------------------------------------------------------------------- |
| `CLIENTS`         | the clients to load: `admin,protofast,theplot,offtheloop` for `clients`, `admin-theplot` for `clients-admin-theplot` |
| `DEFAULT_CLIENT`  | the client for a host no client is named for: `protofast`, or `admin-theplot` |
| `HOST_<NAME>`     | a client is served on the host of its name (client `protofast` on `HOST_PROTOFAST`) |
| `PORT`            | 4000                                                                  |
| `DRAIN_SECONDS`   | ProtoFast's own, unset in the spec: 10 under ProtoStrap, 0 locally     |
| `ADMIN_CONSOLE_ROLES`, `OTEL_*` | passed on to the apps                                   |

What it does itself:

- **Picks the app by Host.** There is no `x-client` header: the Host decides.
- **Keeps app consoles apart.** An app console (`admin-<app>`) must be alone in `CLIENTS`; the
  host refuses to start otherwise.
- **Sets the admin consoles' Content-Security-Policy** on every response from a console
  (`admin` and `admin-*`): scripts and styles only from the host, `connect-src 'self'`,
  `frame-ancestors 'none'`. The admin dev servers set the same header in `angular.json`.
- **Redirects `/theplot`** to `/theplot/` on the admin host, since only `/theplot/` reaches
  the console's process.
- **Answers `GET /healthz`** for its component's name only, and not while draining.
- **Drains** on SIGTERM: fails readiness, serves `DRAIN_SECONDS`, then closes and flushes
  telemetry. Idle connections are kept 65 s, longer than the edge's 60 s.
- **Logs JSON lines** on stdout, Angular's and Express's console output included.

## The browsers' collector

[`services/otel-collector`](../services/otel-collector) is an OpenTelemetry collector for
browsers only; its README has the details. The edge routes `/otlp/` on the protofast, theplot,
offtheloop and admin hosts to it at 20 requests a second. It takes traces and logs on
`/otlp/v1/traces` and `/otlp/v1/logs`, up to 512 KiB a request, drops anything whose
`service.name` isn't one of the five browser clients, and passes the rest to
`OTEL_EXPORTER_OTLP_ENDPOINT`: ProtoStrap's collector in an account, the dashboard locally. Its
config is in the image. Its readiness (`13133/`) doesn't test the Host, and it doesn't drain on
SIGTERM, so a browser's batch sent during a deploy may be lost.

## Known gaps

- **Unverified in an account.** The components' AWS calls with their roles (S3, SQS, SES,
  Secrets Manager), readiness behind the edge and the drain on a real rollout haven't run yet.
