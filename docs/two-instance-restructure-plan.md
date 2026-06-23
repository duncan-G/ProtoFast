# ProtoFast Two-Instance Restructure — Plan

Move the production topology from **one EC2 box running the whole compose stack**
to **two EC2 boxes**, and introduce three new backend dependencies — **Redis**
(session/token lookup), **Keycloak** (identity), and **Postgres** (Keycloak's
durable store, on a persistent EBS volume).

In **dev**, Redis, Keycloak, and Postgres are run as **Aspire-managed container
resources** (`apphost/Program.cs`) so a developer still gets the whole system
from a single `dotnet run` on the AppHost — no compose, no manual containers.

This plan keeps the existing **independent per-component deploy** model
(`deploy/deploy.sh` + `versions.env`, one `*_TAG` per component) and the
**zero-public-ingress / cloudflared-only edge** posture.

---

## 0. Goals, non-goals, decisions

### Goals
- Split the running stack across two instances without losing the
  name-based service wiring or the per-component deploy flow.
- Add Redis + Keycloak + Postgres to both dev (Aspire) and prod (compose).
- Keep Keycloak's database **durable across instance replacement** (the box
  stays "cattle"; the data does not — see [compute.tf](../infra/compute.tf)).
- Preserve the security posture: no public inbound, cross-host traffic only via
  a self-referencing security group over private IPs (**no NAT gateway**).

### Non-goals (explicitly out of scope for this pass)
- High availability / multi-AZ / Redis or Postgres replication.
- Swarm / Kubernetes. We stay on plain Compose across two hosts.
- Managed RDS/ElastiCache (called out as an escape hatch, not the chosen path).

### Decisions (chosen; revisit only if a constraint below changes)
| # | Decision | Choice | Rationale |
|---|----------|--------|-----------|
| D1 | Host split | **Host A = edge**, **Host B = services + state** | One clean cross-host boundary (Envoy → backend). |
| D2 | Postgres persistence | **Dedicated EBS volume**, lifecycle decoupled from the instance | User requirement; survives `terraform destroy/apply` of Host B. |
| D3 | Redis role | **In-memory cache (cache-aside)** | Source of truth is the Postgres `sessions` DB; a Redis restart causes cache misses that repopulate, not logouts. No persistence/volume. |
| D7 | Durable session store | **Shared Postgres `sessions` DB owned by `auth`** | Durable on the pgdata EBS volume; dev parity via the Aspire Postgres resource; no extra infra. |
| D4 | Cross-host transport | **Private IP + published ports + self-referencing SG** | No NAT, no overlay network, no Swarm. |
| D5 | Dev backing services | **Aspire integrations** (`AddPostgres`/`AddRedis`/`AddKeycloak`) | One-command dev; dev mirrors prod wiring. |
| D6 | Subnets | **Both hosts in the existing default public subnet** | Zero-ingress SG already isolates them; avoids NAT. |
| D8 | Host boot config | **Separate per-host user_data + graceful drain** | Edge edits don't replace the DB host; Postgres is stopped cleanly before any teardown. |

### Resolved inputs (confirmed)
- **Q1 — Token model: opaque sessions, cache-aside.** Cookie holds an opaque
  session id. `auth` reads Redis first; on a miss it falls back to the durable
  **Postgres `sessions` DB** — repopulating Redis (with TTL) on hit, clearing the
  cookie (401) on miss. Keycloak is the upstream IdP; `auth` mints + durably
  stores the app session. No stateless-JWT path.
- **Session store (D7): shared Postgres.** A `sessions` database owned by `auth`,
  in the same Postgres instance as Keycloak (separate DB, not shared schema).
  This **overturns the earlier "services don't touch Postgres" note** — `auth`
  now holds a Postgres connection for the sessions table.
- **Q2 — Keycloak realm: committed import.** Realm export JSON lives in
  `infra/keycloak/realms/` and is imported on start (`--import-realm`) for a
  reproducible identity config.
- **Q3 — Stateful components: separate deploy path.** `postgres`/`keycloak`/
  `redis` are their own components but use a **separate, stateful-aware apply
  path** (`KIND=stateful`, §5.3), independent of the app-service
  recreate+health+rollback flow. Postgres never auto-rolls-back its tag.
- **Q4 — Host B sizing: `t4g.medium`** (4 GB, Graviton/arm64 — matches the
  `instance_arch = arm64` tooling that already builds `aarch64`; use `t3.medium`
  if Host B is x86). Host A can stay on the smaller current type.

---

## 1. Target topology

```
Host A — edge (public subnet, zero ingress)      Host B — services + state (public, zero ingress)
  cloudflared  ──tunnel──> Cloudflare              auth      (gRPC :8080  ->  published :8080)
  envoy        ──────────────────────────┐        payments  (gRPC :8080  ->  published :8081)
  clients (SSR, S3 pull)                  │        api       (gRPC :8080  ->  published :8082)
  otel-collector  <───OTLP :4317/4318─────┼──┐     keycloak  (HTTP :8080  ->  published :8083)
  aspire-dashboard                        │  │     redis     (:6379, internal only)
                                          │  │     postgres  (:5432, internal only) ── EBS (pgdata)
                                          │  └─ services export telemetry to Host A
                                          └──── Envoy dials Host B private IP on :8080-8083
```

### Component → host mapping
| Component | Host | New? | Published on host? | Persistence |
|-----------|------|------|--------------------|-------------|
| cloudflared | A | no | no (egress tunnel) | — |
| envoy | A | no | no | — |
| clients (SSR) | A | no | no | S3 pull |
| otel-collector | A | no | **:4317/:4318** (for B) | — |
| aspire-dashboard | A | no | no | ephemeral |
| auth / payments / api | B | no | **:8080 / :8081 / :8082** | — |
| keycloak | B | **yes** | **:8083** | via Postgres |
| postgres | B | **yes** | no (internal) | **EBS volume** (`keycloak` + `sessions` DBs) |
| redis | B | **yes** | no (internal) | none — cache only |

**Same-host wiring stays name-based** (Docker DNS on each host's compose
network): `keycloak → postgres:5432` (keycloak DB), `auth → postgres:5432`
(sessions DB), `auth → redis:6379`, `auth → keycloak:8080`. **Only cross-host
edges** get published ports.

---

## 2. Phase 1 — Dev: Aspire integrations

Goal: `dotnet run` on the AppHost brings up Postgres, Redis, and Keycloak as
containers, wired into the services exactly as in prod, with the Aspire
dashboard showing all of them.

### 2.1 AppHost package references
Add to [apphost/ProtoFast.AppHost.csproj](../apphost/ProtoFast.AppHost.csproj)
(match the existing `13.4.3` Aspire version):

```xml
<PackageReference Include="Aspire.Hosting.PostgreSQL" Version="13.4.3" />
<PackageReference Include="Aspire.Hosting.Redis" Version="13.4.3" />
<PackageReference Include="Aspire.Hosting.Keycloak" Version="13.4.3" />
```

### 2.2 AppHost wiring
In [apphost/Program.cs](../apphost/Program.cs), before the service definitions:

```csharp
// Durable store for Keycloak. WithDataVolume keeps dev realm/user data across
// `dotnet run` restarts (the prod equivalent is the EBS volume).
var postgres = builder.AddPostgres("postgres").WithDataVolume();
var keycloakDb = postgres.AddDatabase("keycloak");
var sessionsDb = postgres.AddDatabase("sessions");   // auth's durable session store (D7)

// Session/token store. In-memory only (no persistence) — matches prod (D3);
// a restart just forces re-login.
var redis = builder.AddRedis("redis");

// Identity provider, backed by the same Postgres so dev mirrors prod.
// WithRealmImport seeds a committed realm export (Q2) so dev is reproducible.
var keycloak = builder.AddKeycloak("keycloak", 8080)
    .WithReference(keycloakDb)
    .WaitFor(keycloakDb)
    .WithRealmImport("../infra/keycloak/realms");
```

Then attach references to the services that consume them (auth is the primary
consumer; add to payments/api only if they validate sessions directly):

```csharp
var auth = builder.AddProject<Projects.ProtoFast_Auth_Api>("auth")
    .WithOtlpCollectorReference(otel)
    .WithReference(redis).WaitFor(redis)
    .WithReference(sessionsDb).WaitFor(sessionsDb)
    .WithReference(keycloak).WaitFor(keycloak);
```

Aspire injects `ConnectionStrings__redis`, `ConnectionStrings__keycloak`, and the
Postgres connection string for Keycloak automatically. **These exact env var
names are the contract we reproduce by hand in prod compose (§3.2)** — keep them
identical so the service code is environment-agnostic.

> Note: confirm the `Aspire.Hosting.Keycloak` API surface for the Postgres-backed
> dev path; some versions run Keycloak in `start-dev` (H2) by default. If wiring
> to Postgres in dev is awkward, fall back to ephemeral dev Keycloak and accept
> that only prod is Postgres-backed — but D5 prefers parity.

### 2.3 Service code (works in dev **and** prod)
The services read connection strings by name, so the same code path serves both
environments. Likely touch points in
[services/shared/ServiceDefaults](../services/shared/ServiceDefaults),
[services/shared/Database](../services/shared/Database), and
[services/auth](../services/auth):

- Add a Redis client keyed off `ConnectionStrings__redis`
  (e.g. `builder.AddRedisClient("redis")` via `Aspire.StackExchange.Redis`).
- Add the **sessions DB** client keyed off `ConnectionStrings__sessions`
  (Npgsql/EF through `services/shared/Database`); `auth` owns a `sessions` table.
- Add OIDC/Keycloak auth wiring keyed off `ConnectionStrings__keycloak`
  (issuer/JWKS URL).
- Implement **cache-aside** in `auth`: read Redis → on miss read the `sessions`
  DB → repopulate Redis (with TTL) on hit, clear the cookie (401) on miss. Redis
  is the cache; the `sessions` DB is the source of truth.

`auth` and Keycloak share the Postgres **instance** but use **separate
databases** (`sessions` vs `keycloak`) — no shared schema. Keycloak's DB
connection stays internal to Keycloak.

**Acceptance for Phase 1:** `dotnet run` on the AppHost shows postgres, redis,
keycloak healthy in the dashboard; auth can reach Redis and resolve Keycloak's
OIDC discovery document; restarting the AppHost preserves the dev realm.

---

## 3. Phase 2 — Prod compose split

Split the single [deploy/docker-compose.yml](../deploy/docker-compose.yml) into
two files that share the same variable/`*_TAG` conventions:

- `deploy/docker-compose.host-a.yml` — cloudflared, envoy, clients, otel-collector, aspire-dashboard
- `deploy/docker-compose.host-b.yml` — auth, payments, api, keycloak, redis, postgres

### 3.1 Host A changes
- Move the edge services unchanged.
- **Publish otel ports for Host B**: `otel-collector` gets
  `ports: ["4317:4317", "4318:4318"]` (cross-host telemetry ingress).
- Repoint Envoy's backend upstreams from Docker names to **Host B's private IP**
  and the published ports:
  ```yaml
  AUTH_HOST: ${HOST_B_IP}      ; AUTH_PORT: "8080"
  PAYMENTS_HOST: ${HOST_B_IP}  ; PAYMENTS_PORT: "8081"
  API_HOST: ${HOST_B_IP}       ; API_PORT: "8082"
  KEYCLOAK_HOST: ${HOST_B_IP}  ; KEYCLOAK_PORT: "8083"   # new vhost/route
  ```
  `HOST_B_IP` comes from `.env` (seeded by cloud-init / Terraform output).
- Add a Keycloak route/vhost to the Envoy config so login/OIDC endpoints are
  reachable through the edge (new domain or path under an existing client).

### 3.2 Host B changes
Existing `auth`/`payments`/`api` gain published ports and the new connection-string
env (the prod equivalent of the Aspire references in §2.2):

```yaml
  auth:
    image: ${ECR}/protofast-auth:${AUTH_TAG}
    restart: unless-stopped
    ports: ["8080:8080"]
    environment:
      <<: *dotnet-env
      ConnectionStrings__redis: "redis:6379"
      ConnectionStrings__sessions: "Host=postgres;Port=5432;Database=sessions;Username=auth;Password=${SESSIONS_DB_PASSWORD}"
      ConnectionStrings__keycloak: "http://keycloak:8080/realms/protofast"
    # ... existing grpc_health_probe volume + healthcheck
  payments: { ports: ["8081:8080"], ... }
  api:      { ports: ["8082:8080"], ... }
```

New services:

```yaml
  postgres:
    image: postgres:17
    restart: unless-stopped
    environment:
      POSTGRES_DB: keycloak
      POSTGRES_USER: keycloak
      POSTGRES_PASSWORD_FILE: /run/secrets/kc-db-password
    volumes:
      - /mnt/pgdata:/var/lib/postgresql/data            # persistent EBS (§4.2)
      - ./postgres/initdb:/docker-entrypoint-initdb.d:ro  # creates `sessions` DB + auth role (first init only)
    secrets: [kc-db-password]
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U keycloak"]
      interval: 10s; timeout: 3s; retries: 5; start_period: 20s

  keycloak:
    image: quay.io/keycloak/keycloak:26.0
    restart: unless-stopped
    command: start --optimized --import-realm
    environment:
      KC_DB: postgres
      KC_DB_URL: jdbc:postgresql://postgres:5432/keycloak
      KC_DB_USERNAME: keycloak
      KC_DB_PASSWORD_FILE: /run/secrets/kc-db-password
      KC_HOSTNAME: ${KEYCLOAK_DOMAIN}
      KC_PROXY_HEADERS: xforwarded          # behind Envoy
      KC_HTTP_ENABLED: "true"
      KC_HEALTH_ENABLED: "true"             # /health/ready for deploy gate
    volumes:
      - ./keycloak/realms:/opt/keycloak/data/import:ro
    ports: ["8083:8080"]
    depends_on:
      postgres: { condition: service_healthy }

  redis:
    image: redis:7
    restart: unless-stopped
    # In-memory only (D3): no AOF, no volume. Set a memory ceiling + TTL eviction.
    command: redis-server --maxmemory 256mb --maxmemory-policy volatile-ttl

secrets:
  kc-db-password:
    file: /opt/protofast/kc-db-password     # root-only, seeded by cloud-init
  tunnel-token:
    file: /opt/protofast/tunnel-token        # Host A only
```

**Second database (`sessions`).** `POSTGRES_DB` only creates Keycloak's DB, so
`deploy/postgres/initdb/01-sessions.sql` (mounted above) runs on first init to
`CREATE ROLE auth ...` + `CREATE DATABASE sessions OWNER auth`. Init scripts run
**only on an empty data dir** — since the EBS volume persists, this fires once;
adding it to an already-populated volume means running the `CREATE` statements by
hand. `SESSIONS_DB_PASSWORD` is seeded as a secret by cloud-init (alongside
`kc-db-password`) and injected into both the init script and `auth`'s connection
string — keep the two in sync.

New manifest tags (`versions.env`): `KEYCLOAK_TAG`, `POSTGRES_TAG`, `REDIS_TAG`
(pin upstream images so the per-component flow can move them deliberately).

---

## 4. Phase 3 — Terraform

### 4.1 Second instance + per-host roles
Refactor `aws_instance.app` ([compute.tf](../infra/compute.tf)) into two
instances (or `for_each` over a `{ a = {...}, b = {...} }` map):

- `aws_instance.host_a` — edge role; keeps the current IAM (S3 read for clients,
  ECR pull), public IP, IMDS hop limit 2 (clients-host still pulls from S3 here).
  **Sizing: `t4g.small`** (2 GB). A sheds the three .NET services to B, so the
  edge (cloudflared + Envoy + Node SSR + otel + aspire-dashboard) fits a small;
  revisit to medium only if the SSR process or aspire-dashboard telemetry
  retention needs the headroom.
- `aws_instance.host_b` — services role; `instance_type = t4g.medium` (Q4 — and
  the one host that genuinely needs it: 3× .NET + Keycloak JVM + Postgres +
  Redis); ECR pull + the EBS attachment. IMDS hop limit stays 2 only if a
  container on B needs instance creds (Keycloak/Postgres don't; keep 2 only if a
  future service does).
- **Size the hosts independently.** Split `var.instance_type` into
  `var.host_a_instance_type` (default `t4g.small`) and
  `var.host_b_instance_type` (default `t4g.medium`) — their sizing drivers now
  differ (edge/SSR vs. JVM+DB).
- Each renders its **own `user_data`** template (§6.1) — not a shared one — so a
  boot-config edit to one host never force-replaces the other.
- **Assign static private IPs** (`private_ip = var.host_a_private_ip` /
  `var.host_b_private_ip`) so each host's user_data references the peer
  (`HOST_A_IP`/`HOST_B_IP`) via a variable rather than
  `aws_instance.*.private_ip` — the latter is a Terraform cycle (§6.1).

### 4.2 Persistent EBS for Postgres (D2)
```hcl
resource "aws_ebs_volume" "pgdata" {
  availability_zone = aws_instance.host_b.availability_zone
  size              = var.pgdata_volume_gb   # default 20
  type              = "gp3"
  encrypted         = true
  tags              = { Name = "${var.project}-pgdata" }
  lifecycle { prevent_destroy = true }       # outlives instance teardown
}

resource "aws_volume_attachment" "pgdata" {
  device_name = "/dev/sdf"
  volume_id   = aws_ebs_volume.pgdata.id
  instance_id = aws_instance.host_b.id
}
```
Host B `user_data` mounts it **without reformatting an existing filesystem**:
```bash
blkid /dev/sdf >/dev/null 2>&1 || mkfs -t ext4 /dev/sdf
mkdir -p /mnt/pgdata && mount /dev/sdf /mnt/pgdata
grep -q /mnt/pgdata /etc/fstab || echo '/dev/sdf /mnt/pgdata ext4 defaults,nofail 0 2' >> /etc/fstab
```

### 4.3 Security group (cross-host, zero public ingress preserved)
Add to `aws_security_group.instance` ([network.tf](../infra/network.tf)) — still
**no internet-facing ingress**, `self = true` only admits sibling instances:
```hcl
ingress {                       # Host A (Envoy) -> Host B services + Keycloak
  description = "Envoy to backend services/keycloak"
  from_port = 8080; to_port = 8083; protocol = "tcp"; self = true
}
ingress {                       # Host B services -> Host A collector
  description = "Services to otel-collector"
  from_port = 4317; to_port = 4318; protocol = "tcp"; self = true
}
```

### 4.4 Secrets
- `kc-db-password`: generate (e.g. `random_password`), write to
  `/opt/protofast/kc-db-password` (root, 0600) via Host B cloud-init, and pass to
  Postgres + Keycloak as a file secret. Do **not** bake into the image or compose.

---

## 5. Phase 4 — deploy.sh + workflow (two-host targeting)

The current flow assumes **one instance, one compose file, one Docker network**,
and runs health checks by `curl`-ing service names on that network
([deploy.sh](../deploy/deploy.sh)). Two hosts breaks three assumptions; address
each:

### 5.1 Component → host routing
The deploy workflow targets an instance by SSM. Add a host dimension:

| Component | Host |
|-----------|------|
| cloudflared, envoy, clients-host, client-*, otel-collector, aspire-dashboard | A |
| auth, payments, api, keycloak, postgres, redis | B |

Tag instances by role (e.g. `Role = edge|services`) and have the workflow resolve
the SSM target from the component's host. `deploy.sh` runs on **both** hosts,
each with its own `COMPOSE_FILE` (host-a vs host-b) pointed at by `APP_DIR`.

### 5.2 New components in `resolve()`
Extend the `case` in `deploy.sh::resolve()` and the `KIND` set:
```sh
keycloak)  KEY="KEYCLOAK_TAG"; SVC="keycloak"; KIND="stateful" ;;
postgres)  KEY="POSTGRES_TAG"; SVC="postgres"; KIND="stateful" ;;
redis)     KEY="REDIS_TAG";    SVC="redis";    KIND="stateful" ;;
```
Add matching `*_TAG` to `SERVICE_TAGS` (bootstrap) and the Host B bring-up set.

### 5.3 Stateful-aware apply (separate path — Q3)
Stateful components get their **own** `KIND=stateful` branch, kept separate from
the `service` flow. For `KIND=stateful`, **do not** `--force-recreate` blindly and **do not** treat a
failed health check as "roll the tag back and recreate" for Postgres:
- `redis`: ordinary `up -d --no-deps redis`; it's a **cache** (D3), so a restart
  just causes cache misses that repopulate from the `sessions` DB — no logout,
  only a brief latency/DB-load bump. Safe to deploy anytime.
- `keycloak`: `up -d --no-deps keycloak`; health-gate on `/health/ready`.
- `postgres`: image bumps within a major version → `up -d --no-deps postgres`.
  **Major-version bumps are migrations, not deploys** — gate behind a manual
  flag; never auto-rollback a Postgres tag (a downgrade after a catalog upgrade
  corrupts data). Document this in the script comments.

### 5.4 Cross-host / new health checks
- Service checks on Host B can keep using `grpc_health_probe` locally (same host).
- Add `keycloak_ok` (`curl host-b-network → keycloak:8080/health/ready`),
  `postgres_ok` (`pg_isready` via `compose exec`), `redis_ok` (`redis-cli ping`).
- The **envoy** check currently curls the SSR vhost end-to-end; with backends on
  Host B it now exercises the cross-host path — confirm Envoy → `HOST_B_IP:8080…`
  is reachable (SG rule §4.3) as part of the envoy deploy gate.

---

## 6. Phase 5 — Per-host cloud-init + graceful teardown

### 6.1 Split user_data templates
Replace the single
[user_data.sh.tftpl](../infra/templates/user_data.sh.tftpl) with **one template
per role plus a shared install fragment**, so an edit to one host's boot config
re-renders only that host (an edge tweak never force-replaces the live database
on Host B):

- `infra/templates/_common.sh.tftpl` — engine + compose plugin + ECR credential
  helper + grpc_health_probe install; rendered once into `local.common_setup`.
- `infra/templates/user_data.host_a.sh.tftpl` — embeds `${common_setup}`, writes
  `tunnel-token`, seeds `.env` (incl. `HOST_B_IP`), installs the host-a compose
  file, runs `deploy.sh bootstrap`.
- `infra/templates/user_data.host_b.sh.tftpl` — embeds `${common_setup}`, mounts
  the EBS volume (§4.2, format-if-empty), writes `kc-db-password` +
  `SESSIONS_DB_PASSWORD`, seeds `.env` (incl. `HOST_A_IP`), installs the host-b
  compose file, installs the teardown unit (§6.2), runs `deploy.sh bootstrap`.
  Bootstrap order respects `depends_on` (postgres → keycloak; services independent).

`templatefile` inserts `${common_setup}` verbatim (it is not re-parsed), so `$`
and `%{` inside the shared script are safe.

**Break the peer-IP cycle with static private IPs.** Host A's user_data needs
Host B's IP and vice-versa; referencing `aws_instance.host_b.private_ip` from
Host A (and back) is a Terraform **cycle**. Assign fixed addresses instead —
`private_ip = var.host_a_private_ip` / `var.host_b_private_ip` (two free
addresses in the subnet CIDR) — and pass the *variables* into both templates.
No cycle, no boot-time discovery. (A Route53 private hosted zone is the elastic
alternative if you'd rather not pin IPs.)

`push_manifest`/S3 self-heal is unchanged but now **per host**: one shared
`versions.env` in S3, each host's `deploy.sh` acting only on components it owns.

### 6.2 Graceful Host B teardown
Postgres is crash-safe (WAL), so a hard terminate won't corrupt data — but we
drain cleanly anyway to avoid force-detaching a dirty filesystem and to stop
writes mid-transaction. **Required order: stop the writers first (so nothing is
still sending data), then Postgres, then unmount, then detach/destroy:**

```
auth · payments · api · keycloak  →  postgres  →  sync  →  umount /mnt/pgdata  →  detach EBS  →  terminate
```

A `drain` subcommand in [deploy.sh](../deploy/deploy.sh):

```sh
drain() {
  log "draining Host B: writers -> postgres -> unmount"
  compose stop auth payments api keycloak || true   # no new sessions/realm writes
  compose stop postgres                              # fast shutdown (image STOPSIGNAL=SIGINT)
  sync
  umount /mnt/pgdata || log "WARN: /mnt/pgdata busy; ext4 journal covers it"
}
```

Two layers invoke it:

- **systemd `ExecStop` (every shutdown, incl. Terraform terminate).** A oneshot
  unit installed by Host B user_data; `RequiresMountsFor=/mnt/pgdata` guarantees
  it stops Postgres *before* systemd unmounts the volume. AWS gives the OS a
  couple of minutes on terminate — ample for stop + unmount.

  ```ini
  [Unit]
  Description=ProtoFast Host B stack lifecycle
  RequiresMountsFor=/mnt/pgdata
  After=docker.service
  Requires=docker.service
  [Service]
  Type=oneshot
  RemainAfterExit=yes
  ExecStart=/opt/protofast/deploy.sh bootstrap
  ExecStop=/opt/protofast/deploy.sh drain
  TimeoutStopSec=120
  [Install]
  WantedBy=multi-user.target
  ```

- **Explicit pre-apply drain (deliberate replace).** Before a `terraform apply`
  that will replace Host B, quiesce it over SSM first, then apply:

  ```
  aws ssm send-command --document-name AWS-RunShellScript \
    --targets Key=tag:Role,Values=services \
    --parameters 'commands=["/opt/protofast/deploy.sh drain"]'
  terraform apply
  ```

**Postgres signal detail:** `SIGTERM` is Postgres "smart shutdown" (waits for
clients — can hang to the kill timeout). The official `postgres` image already
sets `STOPSIGNAL SIGINT` (fast shutdown); verify it, and add
`stop_grace_period: 60s` to the `postgres` service so Docker won't `SIGKILL` it
early.

### 6.3 Backups (the real safety net)
Clean shutdown protects this volume; backups protect against everything else:
- **Scheduled `pg_dump` of `keycloak` + `sessions` to S3** — the logical restore
  path; this is the durable backup.
- **Optional pre-destroy EBS snapshot** (or AWS Data Lifecycle Manager) for a
  fast block-level rollback.

> If this teardown choreography becomes a maintenance burden, it is exactly what
> RDS handles for you — the §0 escape hatch (Postgres → RDS) restores Host B to
> pure cattle.

---

## 7. Execution order & cutover

1. **Phase 1 (dev Aspire)** — land first; fully testable locally, no infra risk.
2. **Phase 2 (service code)** — Redis client + Keycloak OIDC wiring; verify in dev.
3. **Phase 3 compose split** — author both files; validate with
   `docker compose -f ... config` and a local two-network smoke test.
4. **Phase 4 Terraform** — apply in a staging workspace: second instance, EBS, SG.
   Verify cross-host reachability (`nc HOST_B_IP 8080` from Host A).
5. **Phase 5 deploy.sh/workflow** — wire host routing; dry-run a single-component
   deploy to each host.
6. **Cutover** — stand up Host B alongside the existing box, migrate backends,
   repoint Envoy, then retire the old single-box definition.

### Rollback
- Per-component rollback (existing mechanism) still applies on each host.
- **Postgres is the exception** (§5.3): never auto-rollback its tag.
- The EBS volume (`prevent_destroy`) means a full Terraform rollback of Host B
  does not destroy Keycloak's data; reattach to a rebuilt instance.

---

## 8. Risks & caveats

- **Host B is no longer pure cattle.** Its replacement does an EBS detach/reattach
  of the volume holding **both** Keycloak's realms and all user sessions.
  Mitigated by: split user_data (§6.1) so edge edits don't replace it, the
  graceful drain (§6.2) on every shutdown, and `user_data_replace_on_change =
  false` on Host B to make replacement deliberate. Escape hatch: move Postgres to
  **RDS** for a pure-cattle Host B.
- **Cross-host published ports are hand-wired.** Adding a backend means a new
  published port + SG range + Envoy upstream. This is the cost of plain Compose
  over two hosts (the Swarm-overlay alternative was rejected in D4 to preserve the
  per-component deploy tooling).
- **Postgres is the real session SPOF.** Both Keycloak and the `sessions` DB
  depend on it: if it's down, Redis cache hits still serve but misses fail. Redis
  itself is only a cache (D3) — losing it degrades to Postgres-direct (more load,
  slower), not an outage. Acceptable for now (non-goal: HA).
- **Sizing (Q4).** Host B = `t4g.medium` (4 GB) running five+ workloads incl. a
  JVM — monitor memory headroom; bump the type if Keycloak/Postgres get tight.
- **Dev/prod parity hinges on identical connection-string names** (§2.2 ↔ §3.2).
  If Aspire's injected key for Keycloak differs from what the service reads,
  fix it in `ServiceDefaults`, not by diverging the two environments.

---

## 9. Checklist

- [ ] AppHost references + `AddPostgres` (`keycloak` + `sessions` DBs) / `AddRedis` / `AddKeycloak` wired (Phase 1)
- [ ] Service code: Redis client, `sessions` DB client, Keycloak OIDC, cache-aside in `auth` (Phase 2)
- [ ] `docker-compose.host-a.yml` / `docker-compose.host-b.yml` authored (Phase 3)
- [ ] Postgres `initdb/01-sessions.sql` creates `sessions` DB + `auth` role; `SESSIONS_DB_PASSWORD` secret seeded
- [ ] Envoy upstreams repointed to `HOST_B_IP` + Keycloak vhost added
- [ ] Terraform: `host_a`/`host_b`, `pgdata` EBS, SG rules, `kc-db-password`
- [ ] Split user_data: `_common` + `host_a` + `host_b` templates; static private IPs
- [ ] EBS mount (format-if-empty) in Host B user_data
- [ ] Host B `drain` subcommand + systemd `ExecStop` unit; `stop_grace_period` on postgres
- [ ] Scheduled `pg_dump` (keycloak + sessions) to S3
- [ ] `deploy.sh`: `keycloak`/`postgres`/`redis` components + stateful-aware apply
- [ ] Workflow: component→host SSM routing
- [ ] Cross-host reachability + new health checks verified
- [ ] Committed Keycloak realm import (Q2)
- [ ] Cutover + retire single-box definition
