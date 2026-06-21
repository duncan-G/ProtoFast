# infra/bootstrap

One-time, **local**, **admin-credential** Terraform that breaks a
chicken-and-egg problem: the main `infra/` config runs in GitHub Actions, which
needs an S3 state backend and GitHub-OIDC IAM roles to authenticate — but
nothing has created those yet. This config creates them from an operator's
device using admin credentials. It never runs in GitHub Actions and keeps its
state on local disk (gitignored).

The "admin credentials" here are the same genesis identity that runs the first
`infra/identity-center` apply — the **account root** (or a temporary IAM admin
user), whose keys are deleted once genesis is done. See
[`../identity-center/README.md`](../identity-center/README.md) ("Genesis") for the
full one-time sequence; run this config in that same root session.

It creates:

1. **S3 state bucket** (versioned, encrypted) for the main `infra/` backend.
2. **GitHub Actions OIDC provider** in IAM.
3. **`protofast-infra` role** — broad infra lifecycle; trust scoped to the
   `infra` GitHub Environment.
4. **`protofast-deploy` role** — ECR push + tag-scoped `ssm:SendCommand`; trust
   scoped to `refs/heads/main`.
5. **`protofast-boundary`** permissions boundary (prevents IAM/org escalation).
6. (optional) **GitHub repo variables + the `CLOUDFLARE_API_TOKEN` secret**.

## Apply

```sh
cd infra/bootstrap
terraform init

REPO=$(gh repo view --json nameWithOwner -q .nameWithOwner)
terraform apply \
  -var "github_repo=$REPO" \
  -var "state_bucket_name=protofast-tfstate-$(printf '%s' "$REPO" | shasum -a 256 | cut -c1-6)" \
  -var "cloudflare_api_token=<zone+tunnel-scoped-token>"
```

The state bucket name is suffixed with the first 6 hex of `sha256(owner/repo)` so
it's globally unique yet **deterministic** — the same repo always yields the same
bucket, so re-runs and `infra/backend.tf` stay stable. `printf '%s'` (not `echo`)
avoids hashing a trailing newline. On Linux use `sha256sum` in place of
`shasum -a 256`.

`github_repo` is **derived from git**, never hard-coded, so a fork/rename needs
no edit. If you don't want Terraform touching GitHub, pass
`-var manage_github_repo=false` and set the repo variables/secret by hand:

```sh
gh variable set AWS_REGION          --body "$(terraform output -raw aws_region)"
gh variable set AWS_INFRA_ROLE_ARN  --body "$(terraform output -raw infra_role_arn)"
gh variable set AWS_DEPLOY_ROLE_ARN --body "$(terraform output -raw deploy_role_arn)"
gh variable set ECR_REGISTRY        --body "$(terraform output -raw ecr_registry)"
gh secret   set CLOUDFLARE_API_TOKEN
```

## Cloudflare API token

Bootstrap does **not** call Cloudflare — it only stores the token as the
`CLOUDFLARE_API_TOKEN` repo secret (and only when `manage_github_repo=true` and
the token is non-empty). The token is actually consumed by the **main `infra/`
apply in CI**, which manages the tunnel, DNS, zone settings, and Access apps. So
it must exist before the first `infra/` run, not before bootstrap — you can apply
bootstrap without it and set the secret later.

Create a **Custom Token** (My Profile → API Tokens → Create Custom Token); the
canned templates don't fit. Minimum permissions for what `infra/` manages:

| Scope       | Permission                | Level | Needed for                                              |
| ----------- | ------------------------- | ----- | ------------------------------------------------------- |
| **Account** | Cloudflare Tunnel         | Edit  | the `cloudflared` tunnel + its ingress config           |
| **Account** | Access: Apps and Policies | Edit  | the telemetry Access app/policy — *only if telemetry on* |
| **Zone**    | DNS                       | Edit  | the proxied CNAME records                               |
| **Zone**    | Zone Settings             | Edit  | `always_use_https` + `ssl=full`                         |
| **Zone**    | Cache Rules               | Edit  | the `cloudflare_ruleset` cache rules (`cdn.tf`)         |
| **Zone**    | Zone                      | Read  | the `data "cloudflare_zone"` lookup                     |

Note: **Cache Rules** is its own permission, *not* part of **Zone Settings** —
the cache config lives in the rulesets engine (`cloudflare_ruleset` in `cdn.tf`),
so without it `terraform apply` 403s on the `rulesets` endpoint.

Resource scoping: **Account Resources → Include → your account**, and **Zone
Resources → Include → your zone**. The zone is referenced, never created, so no
zone-create or account-membership write scopes are needed — keep it least
privilege. If telemetry is off you may drop the Access permission. Optionally set
a TTL / IP filter; unlike the root keys, this token lives on as the CI secret.

## After apply

Put the state bucket name into `infra/backend.tf` (the `bucket` value), then
`cd ../ && terraform init`. The name is deterministic, so always derive it the
**same way** rather than copying a remembered value — this keeps every future
operator (and a from-scratch rebuild) on the identical bucket:

```sh
REPO=$(gh repo view --json nameWithOwner -q .nameWithOwner)
echo "protofast-tfstate-$(printf '%s' "$REPO" | shasum -a 256 | cut -c1-6)"
# paste the result into infra/backend.tf as the `bucket` value
```

> Recompute it; don't hand-edit the suffix. Same `owner/repo` → same first-6
> `sha256` → same bucket, so the workload state never points at the wrong place.
> (Linux: `sha256sum` in place of `shasum -a 256`.)

If local state is ever lost, every resource here is trivially re-importable
(`terraform import`) — they are stable, named singletons.
