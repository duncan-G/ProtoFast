# How ProtoFast is configured

ProtoFast is a small platform: four Angular SSR clients served by one Node host, four .NET
services (auth, the api, payments, document-import), a Python conversion service, Keycloak,
PostgreSQL and Redis. It deploys with [ProtoStrap](https://github.com/duncan-G/protostrap)
0.1.5, to one AWS account, prod. Dev is the local run, `aspire run`.

## The one idea that holds it together

> **The AppHost is the application, written once. Every component reads the same names
> locally and in an account.**

[`apphost/Program.cs`](../apphost/Program.cs) declares every component, how they connect,
their routes and their sizes. It has two outputs:

- `aspire run` runs it on a laptop, behind ProtoStrap's local edge, the same Envoy an account
  runs.
- `aspire do protostrap-spec` writes the spec ProtoStrap deploys to prod, `protostrap.yaml`.

A component never asks where it runs. It reads its hostnames from `HOST_<NAME>` and
`ORIGIN_<NAME>`, its bucket from `BUCKET_DOCUMENTS`, its queues from `QUEUE_<NAME>_URL`, its
telemetry endpoint from the `OTEL_` variables, and its secrets by their names
(`ConnectionStrings__auth`, `InternalJwt__PublicKeyPem`, ...). In an account ProtoStrap sets
these and the component reads its secrets from `protofast/<environment>`; locally the AppHost
sets the same names, and a service reads its secrets from `protofast/dev`. The few places that check
`PROTOSTRAP_REPLICA` (mail sender, log output, drain time) are named in the layers.

## The layers

| #                                | Layer                  | Read it when you want to know…                                         |
| -------------------------------- | ---------------------- | ---------------------------------------------------------------------- |
| [01](01-topology.md)             | **Topology**           | what runs, on which VM or laptop process, and how one request travels  |
| [02](02-local-development.md)    | **Local development**  | how `aspire run` wires the whole stack on your laptop                  |
| [03](03-services-and-clients.md) | **Services & clients** | how a .NET service, a task or an Angular client reads its settings     |
| [04](04-edge.md)                 | **The edge**           | how routes, gRPC-Web and auth's authorizer are configured              |
| [05](05-identity.md)             | **Identity**           | how Keycloak's realms and the auth service (BFF) are configured        |
| [06](06-secrets.md)              | **Secrets**            | where secret values live and how they reach a process                  |
| [07](07-deployment.md)           | **Deployment**         | how a commit gets to prod, and how to roll back                        |
| [08](08-infrastructure.md)       | **Infrastructure**     | which Terraform roots exist, what they create, and their knobs         |
| [09](09-reference.md)            | **Reference**          | "where do I change X?": tables of every variable, port and file        |

## The 60-second version

- **Locally**: `aspire run` from the repository root. The AppHost starts PostgreSQL, Redis,
  LocalStack, Keycloak, smtp4dev, the services, the four Angular dev servers and ProtoStrap's
  local edge. Browsers use `http://protofast.dev.localhost:20000`,
  `http://theplot.dev.localhost:20000`, `http://admin.dev.localhost:20000` and
  `http://auth.dev.localhost:20000`. The services read `protofast/dev` with your AWS SSO sign-in.
- **In an account**: ProtoStrap's VMs. An ingress VM runs the edge (cloudflared and Envoy),
  the general pool runs the services and clients, conversion and document-import start in the
  general VMs' spare room and get Spot compute VMs when they outgrow it, and data-1 runs
  PostgreSQL, Redis and ProtoStrap's telemetry. There are no inbound ports: Cloudflare reaches
  the edge through a tunnel.
- **Config** is the spec: each component's `env`, `secrets`, routes, requests and scaling,
  written by the AppHost. The AppHost names prod's hostnames too; bootstrap's `hosts` is what
  prod's platform root uses.
- **Secrets** live in one AWS Secrets Manager secret, `protofast/prod`, with keys prefixed by
  who reads them. An Org Admin fills it with `scripts/set-secrets.sh`. Locally the
  services read dev's secret, `protofast/dev`, the same way, with the developer's sign-in:
  it is beside prod's in the same account, and Developers manage it.
- **Deploys** go through ProtoStrap's reusable workflows: every push to `main` deploys to prod
  once someone approves. Each image is tagged from its own sources, so an unchanged
  component isn't rebuilt or restarted. A rollback is deploying an earlier commit again.

> **Status.** Nothing has run on AWS yet. Everything these docs say about an account comes
> from the code, the spec and ProtoStrap's docs, and is unverified until prod's first deploy.
> Each layer lists its own known gaps.

## ProtoStrap's own docs

These docs say what ProtoFast does with ProtoStrap, not how ProtoStrap works. For that:

| Topic                                      | ProtoStrap doc                                                                                        |
| ------------------------------------------ | ----------------------------------------------------------------------------------------------------- |
| The spec, the edge, the scheduler, the contract every component keeps | [protostrap.md](https://github.com/duncan-G/protostrap/blob/v0.1.5/docs/protostrap.md) |
| The AppHost, the spec generator and the local edge | [protostrap-aspire.md](https://github.com/duncan-G/protostrap/blob/v0.1.5/docs/protostrap-aspire.md) |
| The environment's secret                   | [secrets.md](https://github.com/duncan-G/protostrap/blob/v0.1.5/docs/secrets.md)                       |
| Configure tasks (keycloak-config, postgres-config) | [config.md](https://github.com/duncan-G/protostrap/blob/v0.1.5/docs/config.md)                 |
| Email through SES                          | [email.md](https://github.com/duncan-G/protostrap/blob/v0.1.5/docs/email.md)                           |
| Telemetry, Grafana                         | [telemetry.md](https://github.com/duncan-G/protostrap/blob/v0.1.5/docs/telemetry.md)                   |
| VMs, pools, data-1                         | [vms.md](https://github.com/duncan-G/protostrap/blob/v0.1.5/docs/vms.md)                               |
| Network, Cloudflare, the tunnel            | [network.md](https://github.com/duncan-G/protostrap/blob/v0.1.5/docs/network.md)                       |
| Roles and people                           | [identity.md](https://github.com/duncan-G/protostrap/blob/v0.1.5/docs/identity.md)                     |
| Going to production, rolling back          | [production.md](https://github.com/duncan-G/protostrap/blob/v0.1.5/docs/production.md)                 |
| The Terraform modules                      | [infra/README.md](https://github.com/duncan-G/protostrap/blob/v0.1.5/infra/README.md)                 |

ProtoStrap's Claude Code skills (`protostrap-spec`, `protostrap-secrets`, `protostrap-operate`,
...) are pinned to the same version in [`.claude/settings.json`](../.claude/settings.json).

## Related reading

[protostrap-migration.md](protostrap-migration.md) says what ProtoFast did for itself before
ProtoStrap and what moved. [`docs/design/`](design) holds design plans written while features were built (admin
consoles, the run ledger, the workflow engine, billing). They explain why, and go deeper than
these layers; where a plan and these layers disagree about how something is configured, the
layers describe what is there now.

## Conventions used in these docs

- **Locally** means under `aspire run`, which is dev. **An account** means ProtoFast's one AWS
  account, where ProtoStrap runs prod.
- `Shared_Foo__Bar` is a key in the environment's secret. The part before the first `_` says
  who reads it and is dropped when read; `__` is the .NET section separator, so the component
  sees `Foo:Bar`. See [layer 06](06-secrets.md).
- `admin/theplot/` is a route: the host's name, `admin`, then a path prefix.
- `HOST_<NAME>` and `ORIGIN_<NAME>` are a host's hostname and origin in the account the
  replica runs in, such as `HOST_ADMIN=admin.protofast.dev`.
