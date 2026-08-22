# Bootstrap

One-time, **local**, **admin-credential** Terraform that breaks a
chicken-and-egg problem: the main `infra/` config runs in GitHub Actions, which
needs an S3 state backend and GitHub-OIDC IAM roles to authenticate — but
nothing has created those yet. This config creates them from an operator's
device using admin credentials. It never runs in GitHub Actions and keeps its
state on local disk (gitignored).

The "admin credentials" are the **account root** (or a temporary IAM admin
user). This is the only identity that can run the first applies; Identity
Center's OrgAdmin does not exist yet. Keep this session open through
`[../identity-center/README.md](../identity-center/README.md)` — keys are
deleted only after that first apply creates OrgAdmin.

It creates:

1. **S3 state bucket** (versioned, encrypted) for the main `infra/` backend.
2. **GitHub Actions OIDC provider** in IAM.
3. `protofast-infra` **role** — broad infra lifecycle; trust scoped to the
  `infra` GitHub Environment.
4. `protofast-deploy` **role** — ECR push + tag-scoped `ssm:SendCommand`; trust
  scoped to `refs/heads/main`.
5. `protofast-boundary` permissions boundary (prevents IAM/org escalation).
6. (optional) **GitHub repo variables and secrets** (OIDC role ARNs, ECR,
  assets/state bucket names, Cloudflare inputs for `infra.yml`).

State and assets bucket names are `${project}-tfstate|assets-<9-hex sha256(github_repo)>`, so they are globally unique yet deterministic for this
repo.

## Genesis identity (one-time)

Creating the first IAM roles needs an identity that already holds admin, and
no SSO admin exists yet. So this apply — and the first identity-center apply
after it — runs as root (or a temporary IAM admin), which is then deleted.

### 1. Console, as root, in the management account

1. AWS Organizations exists with feature set **All**.
2. **IAM Identity Center → Enable** in `us-west-2` (there is no Terraform
  resource for this; identity-center needs it next).
3. **IAM → Users →** create `protofast-genesis`, attach `AdministratorAccess`,
  create an access key.



### 2. Export the genesis keys (the only step you edit)

```sh
export AWS_ACCESS_KEY_ID=...
export AWS_SECRET_ACCESS_KEY=...
export AWS_DEFAULT_REGION=us-west-2
```



### 3. Fill terraform.tfvars and apply

Copy `[terraform.tfvars.example](terraform.tfvars.example)` to `terraform.tfvars`
(gitignored) and set `github_repo` / `aws_region`. Cloudflare fields are
optional here; empty ones are skipped (set those GitHub secrets later, before
the first `infra/` CI run). See Cloudflare section below on what's needed for Cloudflare API token.

```sh
cd "$(git rev-parse --show-toplevel)/infra/bootstrap"
cp -n terraform.tfvars.example terraform.tfvars   # then edit github_repo
export GITHUB_TOKEN="${GITHUB_TOKEN:-$(gh auth token)}"

terraform init
terraform apply
```

After this succeeds, continue in the **same shell** with  
`[../identity-center/README.md](../identity-center/README.md)` ("Genesis") —  
do not delete the genesis identity until OrgAdmin can sign in.

## Cloudflare

Infra in GitHub Actions talks to Cloudflare (tunnel, DNS, zone settings, Access
apps). Bootstrap does **not** call Cloudflare — it only stores the values as
repo **secrets** (when `manage_github_repo=true` and the tfvars fields are
non-empty) for `infra.yml` to consume as `TF_VAR_*`.

Create a **Custom Token** (My Profile → API Tokens → Create Custom Token); the
canned templates don't fit. Minimum permissions for what `infra/` manages:


| Scope       | Permission                | Level | Needed for                                               |
| ----------- | ------------------------- | ----- | -------------------------------------------------------- |
| **Account** | Cloudflare Tunnel         | Edit  | the `cloudflared` tunnel + its ingress config            |
| **Account** | Access: Apps and Policies | Edit  | the telemetry Access app/policy — *only if telemetry on* |
| **Zone**    | DNS                       | Edit  | the proxied CNAME records                                |
| **Zone**    | Zone Settings             | Edit  | `always_use_https` + `ssl=full`                          |
| **Zone**    | Cache Rules               | Edit  | the `cloudflare_ruleset` cache rules (`cdn.tf`)          |
| **Zone**    | Zone                      | Read  | the `data "cloudflare_zone"` lookup                      |


Note: **Cache Rules** is its own permission, *not* part of **Zone Settings** —
the cache config lives in the rulesets engine (`cloudflare_ruleset` in `cdn.tf`),
so without it `terraform apply` 403s on the `rulesets` endpoint.

Resource scoping: **Account Resources → Include → your account**, and **Zone**
**Resources → Include → your zone**. The zone is referenced, never created, so no
zone-create or account-membership write scopes are needed — keep it least
privilege. If telemetry is off you may drop the Access permission. Optionally set
a TTL / IP filter; unlike the root keys, this token lives on as the CI secret.

Set these in `terraform.tfvars` (bootstrap writes the secrets) or by hand:

```sh
gh secret set CLOUDFLARE_API_TOKEN
gh secret set CLOUDFLARE_ACCOUNT_ID
gh secret set CLOUDFLARE_ZONE
gh secret set ADMIN_DOMAIN
gh secret set PROTOFAST_DOMAIN
gh secret set KEYCLOAK_DOMAIN
# optional — both required to enable telemetry
gh secret set TELEMETRY_DOMAIN
gh secret set TELEMETRY_ACCESS_EMAILS   # JSON array, e.g. ["you@example.com"]
```

