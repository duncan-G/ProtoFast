# How ProtoFast is configured

ProtoFast is a small platform: two Angular clients, three .NET gRPC services, an Envoy edge, Keycloak, Postgres and Redis.

## The one idea that holds it together

> **Dev configuration is written in C#. Production configuration is written in YAML.
> The variable names in the middle are identical.**

In development the Aspire AppHost (`apphost/Program.cs`) starts everything and
injects each process's environment. In production `docker compose` does the same
job from `deploy/docker-compose.host-{a,b}.yml` plus two env files on the box.
Because both sides inject the *same* env var names (`Auth_Keycloak__Authority`,
`CLIENTS`, `SERVER_URL`, `NG_ALLOWED_HOSTS`, …), no application code ever asks
which environment it is in.

## The layers


| #                                | Layer                  | Read it when you want to know…                                    |
| -------------------------------- | ---------------------- | ----------------------------------------------------------------- |
| [01](01-topology.md)             | **Topology**           | what actually runs, on which machine, and how one request travels |
| [02](02-local-development.md)    | **Local development**  | how `aspire run` wires the whole stack on your laptop             |
| [03](03-services-and-clients.md) | **Services & clients** | how a .NET service or an Angular client reads its settings        |
| [04](04-edge.md)                 | **The edge (Envoy)**   | how routing, CORS, auth annotation and TLS are configured         |
| [05](05-identity.md)             | **Identity**           | how Keycloak's realm and the auth service (BFF) are configured    |
| [06](06-secrets.md)              | **Secrets**            | where secret values live and how they reach a process             |
| [07](07-deployment.md)           | **Deployment**         | how code and config get to production, and how to roll back       |
| [08](08-infrastructure.md)       | **Infrastructure**     | what Terraform creates in AWS and Cloudflare, and its knobs       |
| [09](09-reference.md)            | **Reference**          | "where do I change X?" — tables of every variable and file        |




## The 60-second version

- **Dev**: `aspire run` from the repo root. The AppHost starts Postgres, Redis,
Keycloak, smtp4dev, the OTel collector, three .NET services, two Angular dev
servers and Envoy. Browsers use `https://localhost:20000` (admin) and
`https://localhost:20001` (protofast). Everything else is assigned dynamically.
- **Prod**: two EC2 instances. **Host A** is the edge (cloudflared → Envoy → the
unified SSR host + OTel collector + Aspire dashboard). **Host B** is services
and state (auth / payments / api + Keycloak + Postgres + Redis). There are no
inbound ports; Cloudflare reaches Host A through a tunnel, and Host A reaches
Host B over the private subnet.
- **Config in prod** = `deploy/docker-compose.host-*.yml` (shape)
  - `/opt/protofast/.env` (stable values, seeded from cloud-init and Secrets
  Manager) + `/opt/protofast/versions.env` (which image tag each component runs).
- **Secrets** live in exactly one place: the AWS Secrets Manager secret
`protofast/app`. Terraform creates it empty; values are written out of band by
`scripts/populate-secrets.sh`.
- **Deploys** are per-component. Each component's artifact is tagged with a hash
of its own source, so identical input reuses the existing artifact, and a
rollback is "deploy this old tag again".



## Related reading

The files in `[docs/](..)` one level up are **design and migration plans** written
while the system was being built (auth architecture, deployment plans, the
two-instance restructure). They explain *why* decisions were made and go deeper
than these layers do; this set is the current, factual description of how the
system is configured today.

## Conventions used in these docs

- `Auth_Foo__Bar` — a .NET config key delivered as an environment variable.
`__` is the section separator; the leading `Auth_`/`Shared_` is a prefix that
says *which service the value is for* and is stripped when read.
- **Host A / Host B** — the two production instances (edge / services+state).
- **dev / dev-host / publish** — the three modes Envoy renders its config in.

