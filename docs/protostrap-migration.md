# Moving ProtoFast onto ProtoStrap

*What ProtoFast did for itself before, what ProtoStrap does for it now, and what is still open.
Written for the migration (October 2026); the layer docs 01–09 describe the result.*

Before, ProtoFast ran itself: two EC2 hosts, `docker compose` per host, `deploy.sh` pushed over
SSM by one workflow per component, its own Envoy, its own Terraform. Now it is an application
on [ProtoStrap](https://github.com/duncan-G/protostrap) 0.1.5. The AppHost describes every
component once; `aspire run` runs it locally behind ProtoStrap's local edge, and it writes the
spec ProtoStrap deploys to one account, prod. Dev is the local run, with no account of its
own: its secret, `protofast/dev`, sits beside prod's.


## The patterns that changed

| Pattern | Before | Now |
| --- | --- | --- |
| **Where the application is described** | Dev in `apphost/Program.cs`, prod in two compose files plus `.env`/`versions.env` on each host, kept in step by hand | `apphost/Program.cs` alone. `aspire do protostrap-spec` writes `protostrap.yaml`, prod's spec. Nothing is edited on a VM |
| **Orchestration** | `docker compose up` per component, `deploy.sh apply`, a flock, a manifest in S3, cloud-init to re-bootstrap a replaced host | ProtoStrap's scheduler and agents place replicas on pools, replace what changed (by fingerprint), roll back a failed release, and recover a VM |
| **Machines** | Host A (edge) and Host B (services and state), fixed IPs | Pools: ingress (the edge), general, compute (Spot), data-1 (PostgreSQL, Redis, ProtoStrap's telemetry), on the smallest VMs, `t4g.small` and `t4g.medium`. auth, the api and the clients run 1–4 replicas from CPU; keycloak, payments, otel-collector and ThePlot's console one replica each |
| **The edge** | ProtoFast's Envoy image: templates rendered by `entrypoint.sh` in three modes, a listener per client in dev, TLS with the dev certificate | ProtoStrap's edge (cloudflared + Envoy), rendered from each service's `Routes` and `Authorize`. `aspire run` runs the same edge, plain HTTP on one port, hosts `*.dev.localhost:20000` |
| **gRPC-Web routing** | `/api/` and `/payments/` with the prefix stripped | Browsers call `/<package>.<Service>/<Method>` on their own origin; routes match the proto packages (`/document.`, `/document_upload.`, `/story.`, `/admin.` on admin, `/greet.`) |
| **Sign-in paths** | One RE2 regex allowlist on every vhost | Prefix routes per client host (`/signin`, `/signup`, `/signout`, `/reset`, `/add-passkey`, `/account/`). `/backchannel-logout` stays unrouted |
| **Identity annotation** | Envoy's gRPC ext_authz `Check` on auth (h2c), half a second, anonymous when auth can't answer, skipped for static files | ProtoStrap's HTTP authorizer, the same way: the edge asks auth at `/ext-authz/<path>` (`ExtAuthzEndpoint`), strips the client's identity headers, adds auth's; `open`, `timeout: 500ms` and `skip` keep the rest. A rotated session cookie reaches the browser through ProtoStrap's new `authorize.respond` |
| **Admin consoles** | Envoy matched `x-roles` to route `/theplot/` to the console process, else to the platform console with `x-console-denied` | auth's answer gates the console's paths by role (`Tenants:ByHost:admin:Consoles`): 302 to `/signin` or `/forbidden`; the edge routes `admin/theplot/` to `clients-admin-theplot` |
| **Which client a request is for** | Envoy set `x-client` per vhost | The clients host picks the client whose `HOST_<NAME>` is the request's host |
| **CSP, CORS, alt-svc** | Envoy headers | The admin CSP is set by the clients host and the admin dev servers; CORS isn't needed (same origin); alt-svc is gone |
| **Keycloak's public surface** | A vhost with a method filter, regexes, `x-forwarded-proto` forced to https | Prefix routes on the auth host for each realm's `openid-connect/auth`, `logout`, `login-actions/`, `broker/`, and `/resources/`; anything else gets no route (404) |
| **Hostnames** | Written into appsettings, compose, the realm JSON and Terraform variables | The spec names hosts (`protofast`, `theplot`, `admin`, `auth`); prod's Terraform gives the hostnames; every replica gets `HOST_<NAME>` and `ORIGIN_<NAME>`. auth's tenants are keyed by host name; keycloak-config builds redirect URIs from `ORIGIN_*` |
| **Secrets** | `protofast/app` (prod) and `protofast/dev` (laptops, through AWS SSO); each service read it with `Secrets:SecretId`; `deploy.sh` copied values into `.env` and files for Keycloak and Postgres | One secret per deployed environment, `protofast/<environment>` (`protofast/prod`), keys prefixed by audience. Services read `SECRET_ID`/`SECRET_PREFIX` once at startup (`services/shared/ProtoStrap`); Keycloak, keycloak-config, PostgreSQL and Redis get secret files. `scripts/set-secrets.sh` makes every value but the tunnel's token and the model APIs' keys. Locally the services the same way, from `protofast/dev` with the developer's sign-in; the containers take Aspire's parameters |
| **Keycloak's code and themes** | A committed JAR and bind-mounted themes | `services/keycloak` builds the providers (email OTP, Apple, the SES sender) in Maven stages and bakes the themes in; `aspire run` bind-mounts the themes for live edits |
| **Keycloak's realms** | `--import-realm` (first start only), a partial reconcile in `deploy.sh`, three apply scripts run by hand, a `dev-client-urls` executable | The `keycloak-config` configure task (keycloak-config-cli) applies all three realms after Keycloak is ready, and again whenever they, its keys or Keycloak change. It logs in with a bootstrap admin client from the secret |
| **Database users** | Postgres `initdb` scripts (empty volume only) and `deploy.sh`'s `ensure_protofast_db` | The `postgres-config` configure task makes each database's user and ownership from the connection strings, every time they change |
| **Migrations** | Compose one-shot jobs that `deploy.sh` ran before recreating auth or the api | Tasks `auth-migrations` and `protofast-migrations`, named in the services' `migrate`: once per release that changes them, before any new replica |
| **Mail** | SES SMTP credentials in the secret, for auth and Keycloak | SES's v2 API with each component's role (`uses: [email]`): auth's `SesEmailSender`, Keycloak's SES sender provider. smtp4dev locally |
| **Buckets and queues** | Names in appsettings and Terraform; a LocalStack resource and init script locally | The spec's `documents` bucket and `document-upload`, `workflow-outcomes` queues; code reads `BUCKET_DOCUMENTS` and `QUEUE_*_URL`; ProtoStrap.Hosting runs LocalStack locally |
| **Telemetry** | An OTel collector and an Aspire dashboard container on Host A; every exporter hard-coded to gRPC | ProtoStrap's telemetry (Loki, Tempo, Prometheus, Grafana) and its collector, reached from the `OTEL_*` variables; ProtoFast's own collector only takes browsers' traces and logs at `/otlp/` |
| **Readiness and stopping** | `grpc_health_probe`, compose health checks, `deploy.sh`'s per-component probes | `ready` per component: `/healthz` answering only for the component's own name (the .NET services, the clients host, conversion; Keycloak's and otel-collector's own checks don't test it), gRPC health for `""` and the component; readiness fails at SIGTERM and the replica serves on for the drain |
| **Client bundles** | Built per client, synced to S3 by tag, pulled by the clients host's entrypoint with the AWS CLI | One `clients` image with every bundle and the Node host; `clients-admin-theplot` is the same image with `CLIENTS=admin-theplot` |
| **Images and tags** | A content hash per component, computed in each workflow | ProtoStrap tags each image from its sources (the MSBuild closure for projects, the build context minus `.dockerignore`), builds only missing tags, linux/arm64; base images pinned by digest (`Directory.Build.targets`, each Dockerfile) |
| **Deploying** | 20 per-component workflows (test, build, SSM `deploy.sh`), per-component rollback by dispatching an old tag | `deploy.yml`: once the `prod-approval` reviewers approve, ProtoStrap's plan → infra (if the spec's infrastructure changed) → deploy, to prod |
| **Tests** | Inside each deploy workflow, after merge | `ci.yml` on every pull request: .NET tests, the spec's drift and `protostrap check`, each client and the clients host, conversion, the images' builds and entrypoint tests, Terraform |
| **Infrastructure** | One Terraform root for everything (default VPC, two instances, EBS, ECR, SES, Cloudflare tunnel, secrets), a bootstrap root and an identity-center root | `infra/bootstrap` (state, OIDC, Infra and Deploy roles, GitHub environments), `infra/prod` (ProtoStrap's platform module from the spec), `infra/people` (Identity Center groups and permission sets) |
| **Redis** | `redis-server --maxmemory 256mb --maxmemory-policy volatile-ttl`, no persistence | The same flags in the stateful component's command, with ProtoStrap's password file |
| **Agent skills** | `.cursor/` skills that bootstrapped a project with its own Envoy, added a client or a service to it, and added OpenTelemetry | ProtoStrap's plugin skills, pinned in `.claude/settings.json`, for everything ProtoStrap does; `.cursor/add-dotnet-service` and `.cursor/add-angular-client` for the ProtoFast-specific wiring |
| **Local dev** | AWS SSO login at startup, `protofast/dev` read by every service, HTTPS dev servers per port | `protofast/dev` read by every service again, as it reads prod's: `SECRET_ID` and the developer's `AWS_PROFILE`, from ProtoStrap.Hosting's environment, with the AppHost's `aws sso login` when there's no session; Keycloak's keys are Aspire's; dev servers over plain HTTP behind the local edge; `--LocalEdge:Port` to run beside another AppHost |


## What ProtoStrap gained

ProtoFast's session rotation needed one generic feature: **`authorize.respond`**, the headers
of an authorizer's 200 that the edge adds to the client's response (Envoy's
`allowed_client_headers_on_success`). It is in the spec, `protostrap check`, the edge renderer,
the scheduler, ProtoStrap.Hosting's `Authorize(..., respond:)`, the local edge, the docs and the
skills, with tests that run against the real Envoy image.

Keeping the old Envoy's authorization needed three more, released in 0.1.2:
**`authorize.open`** (a request the
authorizer can't answer for goes on without its headers), **`authorize.timeout`** and
**`authorize.skip`** (path prefixes and file extensions sent on unasked, the client's identity
headers still removed). The same release normalises every path at the edge (`..`, `%2e%2e`,
`//`; `%2F` redirected), so a route and its authorizer always see one path. Every other
difference was absorbed in ProtoFast itself.

A review before bootstrap (2026-10-31) found blockers in ProtoStrap itself, fixed before
0.1.0 was tagged: the Infra boundary cut to fit IAM's size limit, with SSM's command actions
for the Deploy role; the deploy bucket made by bootstrap, and the infra workflow putting the
`protostrap` binary in it before an apply, so every VM boots with its agent; `TF_VAR_` names
lowercased again after GitHub uppercases them; data VMs reading their volume list from the
deploy bucket; and an ingress VM taking over its zone's outbound route when no route exists
yet. 0.1.1 added dev's secret for `aspire run`, which the AppHost read for every parameter.
0.1.2 drops that reader: each process gets `SECRET_ID=protofast/dev` and `AWS_PROFILE`, and
reads it as it reads prod's. 0.1.2 also makes the gateway, the tunnel, telemetry and the CDN
resources the AppHost adds (`AddEnvoyGateway`, `AddCloudflareTunnel`, `AddTelemetry`,
`AddCloudflareCdn`). 0.1.3 sets the zone's browser cache to Respect Existing Headers, so
Keycloak's `no-cache` theme files aren't kept 4 hours. 0.1.4 drops an apostrophe from a
security group rule's description, which AWS rejected mid-apply. 0.1.5 lets telemetry's
programs read their configs, which they couldn't on the first prod deploy. ProtoFast pins that
release, `v0.1.5`, everywhere.


## Open items

Before the first deploy:

- **#109 conflicts with `main`,** which has moved on since it started (#107, #108: the
  old Terraform's destroy settings). Those touch files this branch deletes or rewrites; the
  merge keeps the deletion.
- **The order of the first setup matters:** the GitHub environments and their reviewers before
  the merge, since the merge starts a deploy. The [README](../README.md#deploying) has the
  steps.
- **Check the account before bootstrap.** Prod stays in us-west-2, where the old stack and its
  Identity Center were. That Identity Center may still hold groups and permission sets with
  the names `infra/people` makes, and the old SES identity `protofast.dev` may still exist. An
  old `protofast/dev` scheduled for deletion blocks a new one of that name until its 7 days
  pass, or until it is restored.

Still open:

- **Operators in an account.** `scripts/keycloak-grant-admin.py` needs Keycloak's admin API, which
  the edge never routes; `DEV_OPERATORS` is local only. Proposed: an `OPERATORS` key in the secret
  that keycloak-config applies.
- **keycloak-config-cli is built from source** against a newer client library, because no release
  knows Keycloak 26.7's realm fields. Drop that stage once a release does.
- **A flow change briefly fails sign-ins** while keycloak-config recreates the bound flow.
- **Nothing has run on AWS:** SES sending, the bootstrap client login, the forwarded scheme through
  cloudflared, ProtoStrap's telemetry.
- **Keycloak runs one replica,** in prod too, but a release runs the new one beside the old: the
  two cluster on the overlay (`services/keycloak/README.md`), untested across VMs. A minor
  Keycloak upgrade needs the old replica stopped first, which ProtoStrap's rollout doesn't do.
- **Local gaps:** live reload doesn't pass the local edge; Keycloak's and conversion's telemetry
  doesn't reach the dashboard's HTTPS endpoint.
