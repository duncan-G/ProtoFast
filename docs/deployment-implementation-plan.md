# ProtoFast Deployment — Actionable Implementation Plan

This is the step-by-step companion to [deployment-plan.md](deployment-plan.md).
The architecture, trade-offs, and "why" live there; this document is the
ordered, checkable "how". Every task names the concrete files to create or
edit and a verifiable exit criterion.

Sequencing follows the plan's milestones (§7): **M1 local prod parity →
M2 infra up → M3 pipelines → M4 polish**. Within a milestone, tasks are
ordered by dependency. Do them in order; each is small enough to be one PR.

Legend: 📝 = file to create · ✏️ = file to edit · ✅ = exit criterion.

---

## Phase 0 — Prerequisites (one-time, before any code)

These are account/tooling facts to nail down before M1, because later tasks
hard-code their outputs (domain names, account ids, repo slug).

- **0.1 — Decide the two client domains.** The plan uses
`admin.example.com` / `protofast.example.com` as placeholders. Pick the real
apex domain and the two subdomains now; they thread through Envoy env vars,
Cloudflare tunnel config, and health checks.
✅ Domains written down; the apex is **registered in Cloudflare and the zone
is active/authoritative** in the Cloudflare account (Free plan is fine). This
registration/transfer is a one-time manual step — Cloudflare Registrar has no
Terraform create resource — but everything after it is code.
- **0.2 — Derive the GitHub repo slug from git (do not hard-code it).**
Bootstrap IAM trust policies are scoped to `repo:<owner>/<repo>:`*. Rather than
baking a literal slug into the plan or the Terraform, derive it from the
checkout and pass it as the `github_repo` input variable:
  ```sh
  gh repo view --json nameWithOwner -q .nameWithOwner   # → owner/repo
  ```
  Wire it into the bootstrap apply, e.g.
  `terraform apply -var "github_repo=$(gh repo view --json nameWithOwner -q .nameWithOwner)"`.
  ✅ `gh repo view --json nameWithOwner -q .nameWithOwner` returns the expected
  `owner/repo`; the trust policies interpolate `var.github_repo`, so a fork or
  rename needs no edit.
- **0.3 — Local toolchain.** Install: Docker + compose v2, Terraform ≥ 1.10
(for `use_lockfile` native S3 locking), AWS CLI v2, `gh`, .NET 10 SDK,
Node 24, and the `aspire` CLI (`dotnet tool install -g aspire.cli` matching
SDK 13.4.x).
✅ `terraform version`, `aws --version`, `docker compose version`,
`dotnet --version`, `aspire --version` all succeed.
- **0.4 — AWS Organization + Identity Center (plan §3.4).** This is an
account-structure decision, not code. Minimum to *unblock* M2: a
`protofast-prod` account you can get admin credentials into. The full
group/permission-set model (§3.4) can land in parallel with M3/M4 — track it
as a separate workstream (Phase 5 below) so it doesn't block first deploy.
✅ You can `aws sts get-caller-identity` against the prod account with admin
creds (SSO profile or temporary keys for the very first bootstrap).

---

## Phase 1 (M1) — Local production parity

**Goal:** the full stack runs from a production-style `docker-compose.yml` on a
dev box, in Envoy `publish` mode, with working health checks — *before* any
cloud exists. This de-risks everything downstream.

### 1.1 — Make health checks respond in production

Plan §6.2. Today `MapDefaultEndpoints` only maps `/health` + `/alive` when
`IsDevelopment()` ([Extensions.cs:101](../services/shared/ServiceDefaults/Extensions.cs)),
so a prod deploy has nothing to probe. Add gRPC health (what the deploy script
in §4.1 expects) and keep it on in all environments.

- ✏️ `services/shared/ServiceDefaults/ServiceDefaults.csproj` — add
`Grpc.AspNetCore.HealthChecks`.
- ✏️ `services/shared/ServiceDefaults/Extensions.cs`:
  - in `AddDefaultHealthChecks`, also call `AddGrpcHealthChecks()`.
  - in `MapDefaultEndpoints`, call `app.MapGrpcHealthChecksService()` **outside**
  the `IsDevelopment()` guard so gRPC health serves in prod. (Leave the HTTP
  `/health` + `/alive` mapping dev-only per the security note, or expose
  `/alive` only — gRPC is the prod probe.)
- Each service already calls `AddServiceDefaults()` + `MapDefaultEndpoints()`
([auth Program.cs](../services/auth/src/ProtoFast.Auth.Api/Program.cs)), so
no per-service change is needed beyond inheriting the shared default.
✅ `grpc_health_probe -addr=localhost:<port>` returns `SERVING` for auth,
payments, and api when run locally.

### 1.2 — Enable container builds for the three .NET services

Plan §6.4. No Dockerfiles; use the SDK's `PublishContainer`.

- ✏️ For each of
[ProtoFast.Auth.Api.csproj](../services/auth/src/ProtoFast.Auth.Api/ProtoFast.Auth.Api.csproj),
`ProtoFast.Payments.Api.csproj`,
[ProtoFast.Api.csproj](../services/api/src/ProtoFast.Api/ProtoFast.Api.csproj):
add `<ContainerRepository>protofast-auth</ContainerRepository>` (resp.
`-payments`, `-api`), and set `<ContainerFamily>` if a smaller base is wanted
(e.g. `noble-chiseled`). Add `grpc_health_probe` into the image (either a
small `<ContainerExecutableEntrypoint>`-adjacent copy step or bake it via a
`RUN`-equivalent `ContainerImageTags`/custom layer — simplest is to add the
probe binary in the compose healthcheck instead; see 1.4).
- Build locally: `dotnet publish services/auth/src/ProtoFast.Auth.Api -c Release /t:PublishContainer`.
✅ `docker image ls` shows `protofast-auth/payments/api` images that start and
serve gRPC + gRPC-health locally.

### 1.3 — Solve the Envoy internal-TLS wrinkle

Plan §2.2 / §6.3. `entrypoint.sh` requires `ENVOY_TLS_CERT`/`ENVOY_TLS_KEY`
unconditionally (lines 40–41). Use **Option 1**: a baked long-lived self-signed
cert for the in-host hop; `cloudflared` targets `https://envoy:8443` with
`noTLSVerify`.

- ✏️ [proxy/Dockerfile](../proxy/Dockerfile) — generate a self-signed cert at
build time (`openssl req -x509 -newkey ... -days 3650 -nodes -subj "/CN=envoy"`)
to a known path, and default `ENVOY_TLS_CERT`/`ENVOY_TLS_KEY` to it. Keep the
env vars overridable so dev behaviour is unchanged.
- No template changes required (this is the reason Option 1 was chosen).
✅ Envoy starts in `publish` mode with no externally supplied cert and serves
HTTPS on 8443 internally.

### 1.4 — Produce the production compose file

Plan §5, §6.1. Try Aspire generation first (SDK is 13.4.3, which supports it),
fall back to hand-written.

- **Attempt A (preferred):** ✏️ [apphost/Program.cs](../apphost/Program.cs) —
add `builder.AddDockerComposeEnvironment("prod")` and run
`aspire publish -o deploy/`. Inspect the generated `deploy/docker-compose.yml`
against the §5 sketch: publish-mode Envoy env (`ENVOY_MODE=publish`,
`CLIENTS=admin,protofast`, `CLIENT_*_DOMAIN`), the unified `clients` host with
`DEFAULT_CLIENT=admin`, the three services, otel-collector, and a
`cloudflared` service. Every image ref must be `${ECR}/<image>:${TAG}`.
- **Attempt B (fallback):** 📝 `deploy/docker-compose.yml` hand-written to
mirror the AppHost wiring + the §5 sketch. Add `cloudflared` (command
`tunnel run --token-file /run/secrets/tunnel-token`) and the standalone
`aspire-dashboard` (not exposed).
- Add per-service `healthcheck:` blocks using `grpc_health_probe` so
`docker compose ps` reflects real health.
✅ `ECR=local TAG=dev docker compose -f deploy/docker-compose.yml up` brings the
whole stack up locally (use locally-built images tagged `:dev`; skip
`cloudflared` or point it at a dev tunnel).

### 1.5 — Local end-to-end smoke

- Drive Envoy directly with Host headers (no Cloudflare yet):
`curl -k https://localhost:8443 -H 'Host: admin.example.com'` and the
`protofast` host → expect SSR HTML from the unified host.
- gRPC-Web path: hit `/auth/*`, `/payments/*`, `/api/*` through Envoy.
✅ Both client vhosts render via the SSR host and all three gRPC routes
respond — **M1 exit criterion met.**

---

## Phase 2 (M2) — Infrastructure up

**Goal:** Terraform stands up EC2 + tunnel + DNS; a manual one-off deploy puts
the M1 stack on the instance; a `destroy`→`apply`→deploy cycle restores the
site.

### 2.1 — Bootstrap config (chicken-and-egg)

Plan §3.3. Applied **once, locally, with admin creds**; never in CI.

- 📝 `infra/bootstrap/` (local state, gitignored) creating:
  1. **S3 state bucket** (versioned) for the main config's backend.
  2. **IAM OIDC provider** for `token.actions.githubusercontent.com`.
  3. `**protofast-infra` role** — broad infra perms; trust scoped to
    `repo:${var.github_repo}:environment:infra` (slug from 0.2, never literal).
  4. `**protofast-deploy` role** — ECR push to the six repos + tag-scoped
    `ssm:SendCommand`; trust scoped to
     `repo:${var.github_repo}:ref:refs/heads/main`.
  5. `**protofast-boundary`** permissions boundary (used by PlatformAdmin and
    by infra-created roles to prevent escalation — §3.4).
  6. **GitHub repo config** — role ARNs as repo *variables*,
    `CLOUDFLARE_API_TOKEN` as the only repo *secret* (set via `gh` or console).
- ✏️ `.gitignore` — ignore `infra/bootstrap/*.tfstate*` and `.terraform/`.
✅ `cd infra/bootstrap && terraform apply` succeeds; `gh variable list` /
`gh secret list` show the role ARNs and the Cloudflare token.

### 2.2 — Main Terraform config

Plan §3.1. S3 backend (the bucket from 2.1) with `use_lockfile = true`.

- 📝 `infra/` with provider blocks (AWS + Cloudflare) and:
  - **AWS:** VPC (or default), `t4g.medium` Ubuntu 24.04 EC2, egress-only
  security group (**no ingress**), IAM instance profile (SSM core + ECR
  pull), six ECR repos (envoy, clients-host, auth, payments, api,
  otel-collector), EBS volume, `user_data` cloud-init.
  - `**user_data`:** install Docker + compose plugin, create `/opt/protofast/`,
  write the tunnel token to a root-only file, enable SSM agent. Nothing
  app-specific — the instance is cattle.
  - **Cloudflare** (zone is already authoritative — reference it as a
  `data "cloudflare_zone"`, do not create it):
  `cloudflare_zero_trust_tunnel_cloudflared`, tunnel config (public hostnames
  → `https://envoy:8443`, `noTLSVerify`), DNS CNAME per client domain, zone
  settings (Always HTTPS, TLS mode Full).
  - **No Route 53 / AWS DNS.** DNS lives entirely in Cloudflare; AWS hosts only
  compute (EC2/ECR) and Terraform state.
- Outputs: instance id, tunnel id, hostnames.
✅ `terraform apply` from a clean state creates everything; `aws ssm start-session`
into the instance works; Docker is installed; the tunnel shows healthy in the
Cloudflare dashboard.

### 2.3 — On-instance deploy script

Plan §4.1 / §6.5.

- 📝 `deploy/deploy.sh` implementing the §4.1 sequence: write `TAG=<sha>` to
`/opt/protofast/.env`, `docker compose pull`, `up -d`, health loop (≤90s:
curl Envoy with each `Host:` header + `grpc_health_probe` on the three
services), then `echo SHA > last-good` on success, or restore
`TAG=$(cat last-good)` and re-`up -d` on failure (exit 1).
- The script lives in the repo and is synced to the instance by the deploy job
(2.4 / 3.x) alongside `deploy/docker-compose.yml`.
✅ Running `deploy.sh <sha>` by hand on the instance (via SSM) deploys and
health-checks the stack; a deliberately bad tag triggers rollback to last-good.

### 2.4 — Manual one-off deploy (proves the path before pipelines)

- Push M1 images to ECR by hand (`aws ecr get-login-password | docker login`,
tag, push), sync `deploy/` to `/opt/protofast/`, run `deploy.sh`.
✅ The site is reachable at both client domains through Cloudflare over HTTPS.

### 2.5 — Cattle drill (M2 exit criterion)

Plan §7. `terraform destroy` → `terraform apply` → one deploy → site restored.
✅ A full destroy/rebuild + deploy cycle brings the site back with no manual
instance fiddling. **M2 exit criterion met.**

---

## Phase 3 (M3) — Pipelines

**Goal:** GitHub Actions own both infra lifecycle and app deploys with rollback.

### 3.1 — Deploy workflow

Plan §4, §4.1.

- 📝 `.github/workflows/deploy.yml` — trigger on push to `main` +
`workflow_dispatch` (with an optional `sha` input for redeploy/pinned-tag).
Jobs:
  1. **CI:** `dotnet test`, `ng test` (or `npm test` per client).
  2. **Build + tag with git SHA:** envoy ([proxy/Dockerfile](../proxy/Dockerfile)),
    clients-host ([clients/host/Dockerfile](../clients/host/Dockerfile),
     **repo-root context**), the three services via
     `dotnet publish /t:PublishContainer`, otel-collector.
  3. **Push to ECR** (assume `protofast-deploy` via OIDC — no stored keys).
  4. **Deploy:** `aws ssm send-command` to run `deploy.sh <sha>`; sync
    `deploy/docker-compose.yml` + `deploy.sh` first. On success record SHA; on
     failure the script rolls back and the run fails loudly.
  ✅ A push to `main` builds, pushes, deploys, and health-checks; an injected
  failure rolls back and reports red.

### 3.2 — Infra workflow

Plan §3.2.

- 📝 `.github/workflows/infra.yml` — `workflow_dispatch` with `action` input
(`plan` / `apply` / `destroy`). Assume `protofast-infra` via OIDC,
`terraform init` (S3 backend), `plan`; `apply`/`destroy` gated behind a
GitHub **Environment `infra`** with required reviewers.
✅ `plan` posts a diff and stops; `apply`/`destroy` require approval and
converge; "update" is just editing `infra/*.tf` and re-running `apply`.

---

## Phase 4 (M4) — Polish

Plan §6.8, §5, §4.1.

- **4.1 — Cloudflare Access for the dashboard.** Tunnel hostname
`telemetry.<domain> → aspire-dashboard:18888` behind a CF Access policy
(SSO/email). Or skip the hostname and document the SSM port-forward
alternative.
✅ Dashboard reachable only after edge auth.
- **4.2 — CDN cache rules.** Cache Angular hashed bundles aggressively; bypass
SSR HTML and gRPC-Web paths. Add as Cloudflare rules in `infra/`.
✅ Static assets show `cf-cache-status: HIT`; HTML/gRPC bypass.
- **4.3 — Image pruning.** Keep last N image tag sets on the instance (so
rollback stays local); prune beyond N in `deploy.sh` or a cron.
✅ Disk stays bounded; rollback to last-good still works offline.
- **4.4 — Repeat the destroy/rebuild drill** end-to-end through the pipelines
(not by hand).
✅ Pipeline-driven destroy→apply→deploy restores the site.

---

## Phase 5 (parallel) — AWS Identity Center & access model

Plan §3.4. Independent of M1–M4; can proceed in parallel once Phase 0.4 exists.
This runs in the **management account**, by a **human** (never the CI OIDC
roles), from its own root config with its own state.

### 5.0 — Decide the identity source (determines what's `resource` vs `data`)

Plan §3.4, "can Terraform create the SSO groups?". The whole phase forks here:

- **Branch A — built-in Identity Center directory** (recommended for v1 / small
team). Terraform creates the groups too (`aws_identitystore_group`).
- **Branch B — external IdP + SCIM** (Entra/Okta/Google). Groups are
SCIM-provisioned and **read-only**; Terraform references them with
`data.aws_identitystore_group` and a human creates them upstream first.

Permission sets and account assignments are Terraform-created in **both**
branches — only the six group objects differ.
✅ Identity source chosen; Branch A or B recorded in `infra/identity-center/README`.

### 5.1 — Enable Identity Center (manual, one-time)

No Terraform resource enables IC. Enable it in the Organizations / Identity
Center console for the org; pick the identity source from 5.0.
✅ `aws sso-admin list-instances` returns an instance ARN + identity store id.

### 5.2 — Permission-set policies in the repo

Plan §3.4 "Permission set contents". These are needed regardless of branch.

- 📝 `infra/identity-center/policies/` — one JSON per custom set:
`platform-admin.json`, `deployer.json`, `developer.json`, plus the
`protofast-boundary.json` boundary, and
📝 `identity-center-admin.json` — the least-privilege policy for the **human
who runs this very config** (`sso:`*, `identitystore:*`,
`organizations:Describe*/List*`, `iam:CreateServiceLinkedRole`,
`ds:DescribeDirectories`; drop `identitystore:Create*` in Branch B). See plan
§3.4 for the full document.
- AWS-managed sets (`OrgAdmin`→`AdministratorAccess`, `Billing`,
`SecurityAudit`) need no JSON — they're managed-policy attachments.
✅ Every group in the §3.4 table has either a checked-in policy JSON or a named
managed policy.

### 5.3 — `infra/identity-center/` Terraform (separate root config + state)

- 📝 `infra/identity-center/` with `data.aws_ssoadmin_instances` and:
  - `aws_ssoadmin_permission_set` × 6 (+ session durations: 1h admin/deploy,
  4h platform, 8h developer/billing/security) with
  `aws_ssoadmin_managed_policy_attachment` /
  `aws_ssoadmin_permission_set_inline_policy` /
  `aws_ssoadmin_permissions_boundary_attachment` (PlatformAdmin) wiring the 5.2
  policies in;
  - the `IdentityCenterAdmin` permission set from `identity-center-admin.json`;
  - **Branch A:** `aws_identitystore_group` × 6;
  **Branch B:** `data.aws_identitystore_group` × 6 (by display name);
  - `aws_ssoadmin_account_assignment` wiring group → set → account
  (`Org-Admins`/`Billing`/`SecurityAudit` → Management;
  `Platform-Admins`/`Deploy-BreakGlass`/`Developers` → `protofast-prod`).
- First `apply` is run by a human holding `OrgAdmin`/AdministratorAccess
(chicken-and-egg); subsequent applies can use `IdentityCenterAdmin`.
✅ `terraform apply` creates the sets + assignments; an SSO login shows each
group landing in the right account with the right session length. Branch A: the
groups exist in Terraform state; Branch B: the SCIM groups resolve via `data`.

### 5.4 — MFA + short sessions + delegated admin (manual settings)

Enforce MFA org-wide in IC settings; register `protofast-prod` (or a `security`
account) as delegated admin so day-to-day SSO management leaves the management
account untouched.
✅ MFA required at sign-in; admin sets carry the shortest sessions; delegated
admin can manage assignments without the management-account console.

### 5.5 — (Optional) Federate to upstream IdP with SCIM

Plan §3.4 "tying GitHub identities to AWS groups". SAML + SCIM the same IdP
group into both an Identity Center group and a GitHub team. This is the trigger
to move from Branch A to Branch B (groups become `data`). Release authority
stays a **GitHub** concern (branch protection + Environment reviewers).
✅ One IdP membership change governs both AWS access and GitHub release rights.

✅ **Phase exit:** a human can reproduce by hand exactly what the OIDC pipeline
roles do — no more — and no single group is admin over both org and workload.

---

## Tracking checklist

```
Phase 0  Prereqs
  [ ] 0.1 domains   [ ] 0.2 repo slug   [ ] 0.3 toolchain   [ ] 0.4 prod account
Phase 1 (M1) Local prod parity
  [ ] 1.1 prod health checks   [ ] 1.2 service container builds
  [ ] 1.3 Envoy internal TLS   [ ] 1.4 prod compose file
  [ ] 1.5 local e2e smoke ............................. M1 ✅
Phase 2 (M2) Infra up
  [ ] 2.1 bootstrap   [ ] 2.2 main terraform   [ ] 2.3 deploy.sh
  [ ] 2.4 manual deploy   [ ] 2.5 cattle drill ........ M2 ✅
Phase 3 (M3) Pipelines
  [ ] 3.1 deploy.yml   [ ] 3.2 infra.yml ............... M3 ✅
Phase 4 (M4) Polish
  [ ] 4.1 CF Access   [ ] 4.2 cache rules   [ ] 4.3 pruning
  [ ] 4.4 pipeline destroy/rebuild drill .............. M4 ✅
Phase 5 (parallel) Identity Center & access model
  [ ] 5.0 identity source (Branch A/B)   [ ] 5.1 enable IC
  [ ] 5.2 permission-set policy JSON (+ IdentityCenterAdmin)
  [ ] 5.3 infra/identity-center terraform   [ ] 5.4 MFA + delegated admin
  [ ] 5.5 (optional) IdP/SCIM federation
```

## Deferred (plan §8 — do not block on these)

Durable telemetry (swap Aspire Dashboard later, otel-collector stays the
interface) · stateful data + backward-compatible migrations · instance resizing
via `terraform apply`.