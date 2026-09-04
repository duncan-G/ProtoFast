# 07 · Deployment

*How code and configuration reach production, what the box does with them, and how
to roll back. Self-contained.*

## The model: one component at a time

A **component** is the atomic unit of deployment. Deploying one rewrites a single
line of the version manifest and recreates a single container. Mixed versions
across components are normal and expected.

Every component's artefact is tagged with a **content hash of its own sources**
(`c-<16 hex>`, from `git ls-files -s` over that component's paths). Identical
source ⇒ identical tag ⇒ the build is skipped and the existing artefact is
reused. That is also what makes rollback trivial: an old tag is still there.

| Component | Host | Artefact | Sources hashed |
|---|---|---|---|
| `auth` (+ `auth-migrations`) | B | ECR image | `services/auth services/shared` |
| `payments` | B | ECR image | `services/payments services/shared` |
| `api` | B | ECR image | `services/api services/shared` |
| `envoy` | A | ECR image | `proxy` |
| `otel-collector` | A | ECR image | `otel-collector` |
| `clients-host` | A | ECR image | `clients/host` |
| `client-admin` / `client-protofast` | A | S3 prefix `clients/<name>/<tag>/` | that client + all `Protos` |
| `keycloak`, `postgres`, `redis`, `cloudflared`, `aspire-dashboard` | pinned upstream images | — | compose/config paths |

The first group runs through the reusable workflow
[`_component-deploy.yml`](../../.github/workflows/_component-deploy.yml); the pinned
upstream tiers have their own small workflows because there is nothing to build.

## The pipeline

Each `deploy-<component>.yml` triggers on pushes to `main` that touch its paths,
or on manual dispatch with an explicit tag. It then runs three jobs:

1. **test** — `dotnet build` + every discovered test project, or `npm ci && npm test`
   for a client, or nothing for a pure Dockerfile component.
2. **build** — resolve the content tag; if the artefact already exists, reuse it;
   otherwise build and push (dotnet SDK container publish → ECR, buildx cross-build
   for arm64 → ECR, or `ng build` → `aws s3 sync`).
3. **deploy** — resolve the target instance by tags (`protofast:role=app-server`
   plus `Role=edge|services`), gzip the host's compose file and `deploy.sh`, ship
   them over **SSM Run Command** and run
   `deploy.sh apply <component>=<tag>`. No SSH; no inbound ports. Full output goes
   to the CloudWatch log group `/protofast/deploy`.

Authentication to AWS is GitHub OIDC — `protofast-deploy` for deploys (trust scoped
to `refs/heads/main`), `protofast-infra` for Terraform (trust scoped to the `infra`
GitHub Environment).

## What the box holds

```
/opt/protofast/
  docker-compose.yml    ← synced by each deploy (host A or host B variant)
  deploy.sh             ← synced by each deploy
  .env                  ← STABLE: ECR, AWS_REGION, HOST_ROLE, peer IP, CLIENTS,
                          domains, ASSETS_BUCKET + secret-derived values
  versions.env          ← MANIFEST: one *_TAG per component (what should run)
  versions.env.prev     ← pre-apply snapshot, for per-component rollback
  versions.env.lock     ← flock target; serialises manifest writes
  kc-db-password, auth-db-password, internal-jwt-pub, tunnel-token
```

Compose reads **both** env files: `.env` for shape and `versions.env` for every
image tag. `.env` is seeded by cloud-init and then only ever re-asserted (ECR,
region, peer IP, secret-derived values) — deploys never rewrite the rest of it.

## What `deploy.sh apply` does

For each `component=tag` pair, under a lock:

1. **Decide whether there is work.** A no-op requires all four of: same tag, a
   running container, the same resolved compose config, and a passing health check.
   Stale config, a stopped container or an unhealthy one all trigger a recreate.
2. **Snapshot** `versions.env` and write the new tag.
3. **Apply**, by kind:
   - *service / envoy / otel / edge*: `compose pull` then `up -d --no-deps`.
     For `auth`, the schema-migrations job runs first and **fails closed** — if it
     fails, `auth` is not recreated and the old version keeps serving.
   - *host / client*: force-recreate the clients host so its entrypoint re-syncs
     every pinned client from S3.
   - *stateful* (`postgres`, `redis`, `keycloak`): plain `up -d --no-deps`, never a
     blind force-recreate. A Postgres **major** bump is refused unless
     `ALLOW_PG_MAJOR=1` — that is a migration, not a deploy.
4. **Health-check** just that component (gRPC health probe, Envoy admin `/ready`,
   collector `:13133`, Keycloak `/health/ready`, an SSR render, `redis-cli PING`,
   `pg_isready`).
5. **On failure, roll back that component** to the previous tag and re-apply it —
   except Postgres, which is never auto-rolled-back, because a downgrade after a
   catalog upgrade corrupts data.
6. **Publish the manifest to S3** so a replaced instance can self-heal to it.

Keycloak gets extra care: a config-only deploy (new realm JSON or themes at an
unchanged image tag) restarts it, re-imports the realm if it is missing, and runs
the allow-listed realm/client reconcile. Flows are never reconciled automatically
(see [layer 05](05-identity.md)).

Two other modes exist: `deploy.sh bootstrap` (bring the whole stack up from the
persisted manifest — what cloud-init runs on a fresh box) and `deploy.sh drain`
(graceful Host B teardown, unmounting the data volume).

## Everyday operations

| I want to… | Do this |
|---|---|
| Ship a change | merge to `main`; the matching workflow fires on its paths |
| Roll back | run that component's workflow with `workflow_dispatch` and the previous tag; it must already exist as an artefact |
| Redeploy without changes | same, with the current tag — the apply still recreates if config drifted |
| Change only config (compose/env) | touch the compose file and deploy any component on that host; the resolved-config check catches it |
| Update Keycloak / Postgres / Redis | run `deploy-keycloak` / `deploy-postgres` / `deploy-redis` with the new upstream tag, and keep the compose default in sync |
| Read a failed deploy's full output | CloudWatch log group `/protofast/deploy` (the SSM inline view is truncated) |
| Recover a replaced instance | nothing — cloud-init pulls `versions.env`, the compose file and `deploy.sh` from S3 and bootstraps |

## Rules that keep this safe

- Concurrency groups mean two deploys of the *same* component never race; a flock
  serialises manifest writes across different components.
- The deploy job deliberately declares **no** GitHub Environment: adding one would
  change the OIDC subject and break the deploy role's trust. Release control is
  branch protection on `main` plus who may dispatch workflows.
- A dispatch rollback never rebuilds from current source under an old tag — if the
  artefact is missing, the job fails.
