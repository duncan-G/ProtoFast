# Moving ProtoFast onto ProtoStrap

*What ProtoFast did for itself before, what ProtoStrap does for it now, and what is still open.
Written for the migration (October 2026); the layer docs 01–09 describe the result.*

Before, ProtoFast ran itself: two EC2 hosts, `docker compose` per host, `deploy.sh` pushed over
SSM by one workflow per component, its own Envoy, its own Terraform. Now it is an application
on [ProtoStrap](https://github.com/duncan-G/protostrap) 0.1.0. The AppHost describes every
component once; `aspire run` runs it locally behind ProtoStrap's local edge, and it writes the
spec ProtoStrap deploys to one account, prod. Dev is the local run: its account holds only
development secrets.


## The patterns that changed

| Pattern | Before | Now |
| --- | --- | --- |
| **Where the application is described** | Dev in `apphost/Program.cs`, prod in two compose files plus `.env`/`versions.env` on each host, kept in step by hand | `apphost/Program.cs` alone. `aspire do protostrap-spec` writes `protostrap.yaml`, prod's spec. Nothing is edited on a VM |
| **Orchestration** | `docker compose up` per component, `deploy.sh apply`, a flock, a manifest in S3, cloud-init to re-bootstrap a replaced host | ProtoStrap's scheduler and agents place replicas on pools, replace what changed (by fingerprint), roll back a failed release, and recover a VM |
| **Machines** | Host A (edge) and Host B (services and state), fixed IPs | Pools: ingress (the edge), general, compute (Spot), data-1 (PostgreSQL, Redis, ProtoStrap's telemetry), on the smallest VMs, `t4g.small` and `t4g.medium`. Every service runs in pairs |
| **The edge** | ProtoFast's Envoy image: templates rendered by `entrypoint.sh` in three modes, a listener per client in dev, TLS with the dev certificate | ProtoStrap's edge (cloudflared + Envoy), rendered from each service's `Routes` and `Authorize`. `aspire run` runs the same edge, plain HTTP on one port, hosts `*.dev.localhost:20000` |
| **gRPC-Web routing** | `/api/` and `/payments/` with the prefix stripped | Browsers call `/<package>.<Service>/<Method>` on their own origin; routes match the proto packages (`/document.`, `/document_upload.`, `/story.`, `/admin.` on admin, `/greet.`) |
| **Sign-in paths** | One RE2 regex allowlist on every vhost | Prefix routes per client host (`/signin`, `/signup`, `/signout`, `/reset`, `/add-passkey`, `/account/`). `/backchannel-logout` stays unrouted |
| **Identity annotation** | Envoy's gRPC ext_authz `Check` on auth (h2c) | ProtoStrap's HTTP authorizer: the edge asks auth at `/ext-authz/<path>` (`ExtAuthzEndpoint`), strips the client's identity headers, adds auth's. A rotated session cookie reaches the browser through ProtoStrap's new `authorize.respond` |
| **Admin consoles** | Envoy matched `x-roles` to route `/theplot/` to the console process, else to the platform console with `x-console-denied` | auth's answer gates the console's paths by role (`Tenants:ByHost:admin:Consoles`): 302 to `/signin` or `/forbidden`; the edge routes `admin/theplot/` to `clients-admin-theplot` |
| **Which client a request is for** | Envoy set `x-client` per vhost | The clients host picks the client whose `HOST_<NAME>` is the request's host |
| **CSP, CORS, alt-svc** | Envoy headers | The admin CSP is set by the clients host and the admin dev servers; CORS isn't needed (same origin); alt-svc is gone |
| **Keycloak's public surface** | A vhost with a method filter, regexes, `x-forwarded-proto` forced to https | Prefix routes on the auth host for each realm's `openid-connect/auth`, `logout`, `login-actions/`, `broker/`, and `/resources/`; anything else gets no route (404) |
| **Hostnames** | Written into appsettings, compose, the realm JSON and Terraform variables | The spec names hosts (`protofast`, `theplot`, `admin`, `auth`); prod's Terraform gives the hostnames; every replica gets `HOST_<NAME>` and `ORIGIN_<NAME>`. auth's tenants are keyed by host name; keycloak-config builds redirect URIs from `ORIGIN_*` |
| **Secrets** | `protofast/app` (prod) and `protofast/dev` (laptops, through AWS SSO); each service read it with `Secrets:SecretId`; `deploy.sh` copied values into `.env` and files for Keycloak and Postgres | One secret per deployed environment, `protofast/<environment>` (`protofast/prod`), keys prefixed by audience. Services read `SECRET_ID`/`SECRET_PREFIX` once at startup (`services/shared/ProtoStrap`); Keycloak, keycloak-config, PostgreSQL and Redis get secret files. `scripts/set-secrets.sh` makes every value. Locally the AppHost's parameters stand in; no AWS is needed |
| **Keycloak's code and themes** | A committed JAR and bind-mounted themes | `services/keycloak` builds the providers (email OTP, Apple, the SES sender) in Maven stages and bakes the themes in; `aspire run` bind-mounts the themes for live edits |
| **Keycloak's realms** | `--import-realm` (first start only), a partial reconcile in `deploy.sh`, three apply scripts run by hand, a `dev-client-urls` executable | The `keycloak-config` configure task (keycloak-config-cli) applies all three realms after Keycloak is ready, and again whenever they, its keys or Keycloak change. It logs in with a bootstrap admin client from the secret |
| **Database users** | Postgres `initdb` scripts (empty volume only) and `deploy.sh`'s `ensure_protofast_db` | The `postgres-config` configure task makes each database's user and ownership from the connection strings, every time they change |
| **Migrations** | Compose one-shot jobs that `deploy.sh` ran before recreating auth or the api | Tasks `auth-migrations` and `protofast-migrations`, named in the services' `migrate`: once per release that changes them, before any new replica |
| **Mail** | SES SMTP credentials in the secret, for auth and Keycloak | SES's v2 API with each component's role (`uses: [email]`): auth's `SesEmailSender`, Keycloak's SES sender provider. smtp4dev locally |
| **Buckets and queues** | Names in appsettings and Terraform; a LocalStack resource and init script locally | The spec's `documents` bucket and `document-upload`, `workflow-outcomes` queues; code reads `BUCKET_DOCUMENTS` and `QUEUE_*_URL`; ProtoStrap.Hosting runs LocalStack locally |
| **Telemetry** | An OTel collector and an Aspire dashboard container on Host A; every exporter hard-coded to gRPC | ProtoStrap's telemetry (Loki, Tempo, Prometheus, Grafana) and its collector, reached from the `OTEL_*` variables; ProtoFast's own collector only takes browsers' traces and logs at `/otlp/` |
| **Readiness and stopping** | `grpc_health_probe`, compose health checks, `deploy.sh`'s per-component probes | `ready` per component: `/healthz` answering only for the component's own name, gRPC health for `""` and the component; readiness fails at SIGTERM and the replica serves on for the drain |
| **Client bundles** | Built per client, synced to S3 by tag, pulled by the clients host's entrypoint with the AWS CLI | One `clients` image with every bundle and the Node host; `clients-admin-theplot` is the same image with `CLIENTS=admin-theplot` |
| **Images and tags** | A content hash per component, computed in each workflow | ProtoStrap tags each image from its sources (the MSBuild closure for projects, the build context minus `.dockerignore`), builds only missing tags, linux/arm64; base images pinned by digest (`Directory.Build.targets`, each Dockerfile) |
| **Deploying** | 20 per-component workflows (test, build, SSM `deploy.sh`), per-component rollback by dispatching an old tag | `deploy.yml`: once the `prod-approval` reviewers approve, ProtoStrap's plan → infra (if the spec's infrastructure changed) → deploy, to prod |
| **Tests** | Inside each deploy workflow, after merge | `ci.yml` on every pull request: .NET tests, the spec's drift and `protostrap check`, each client and the clients host, conversion, the images' builds and entrypoint tests, Terraform |
| **Infrastructure** | One Terraform root for everything (default VPC, two instances, EBS, ECR, SES, Cloudflare tunnel, secrets), a bootstrap root and an identity-center root | `infra/bootstrap/prod` (state, OIDC, Infra and Deploy roles, GitHub environments), `infra/prod` (ProtoStrap's platform module from the spec), `infra/people` (Identity Center groups and permission sets) |
| **Redis** | `redis-server --maxmemory 256mb --maxmemory-policy volatile-ttl`, no persistence | The same flags in the stateful component's command, with ProtoStrap's password file |
| **Local dev** | AWS SSO login at startup, `protofast/dev` read by every service, HTTPS dev servers per port | No AWS. Parameters in the AppHost's user secrets; the internal JWT pair is made each run; dev servers over plain HTTP behind the local edge; `--LocalEdge:Port` to run beside another AppHost |


## What ProtoStrap gained

ProtoFast's session rotation needed one generic feature: **`authorize.respond`**, the headers
of an authorizer's 2xx answer that the edge adds to the client's response (Envoy's
`allowed_client_headers_on_success`). It is in the spec, `protostrap check`, the edge renderer,
the scheduler, ProtoStrap.Hosting's `Authorize(..., respond:)`, the local edge, the docs and the
skills, with tests that run against the real Envoy image. Every other difference was absorbed
in ProtoFast itself.


## Open items

- **ProtoStrap v0.1.0 must be published first.** Every workflow and Terraform root names
  `duncan-G/protostrap@v0.1.0`, and ProtoStrap is untagged; `authorize.respond` must be in it.
- **Operators in an account.** `scripts/keycloak-grant-admin.py` needs Keycloak's admin API, which
  the edge never routes; `DEV_OPERATORS` is local only. Proposed: an `OPERATORS` key in the secret
  that keycloak-config applies.
- **keycloak-config-cli is built from source** against a newer client library, because no release
  knows Keycloak 26.7's realm fields. Drop that stage once a release does.
- **A flow change briefly fails sign-ins** while keycloak-config recreates the bound flow.
- **Nothing has run on AWS:** SES sending, the bootstrap client login, the forwarded scheme through
  cloudflared, ProtoStrap's telemetry.
- **Local gaps:** live reload doesn't pass the local edge; containers' telemetry doesn't reach the
  dashboard's HTTPS endpoint; Keycloak runs one replica.
- **`.cursor/` skills** still describe the old deployment.
