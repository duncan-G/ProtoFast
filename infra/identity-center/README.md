# infra/identity-center

AWS Identity Center (SSO) access model. Has its own Terraform state, separate from
the workload `infra/`. It is **never** applied by the CI OIDC roles — those are
capped by `protofast-boundary`, which denies `organizations:*`/`account:*`, so they
can't manage identities. The first apply is run by the **account root** (genesis);
every apply after that is run by a human holding **OrgAdmin**.

This config creates the **groups**, the **permission sets**, and the **account
assignments** that wire them together.

Three groups, three jobs — one permission set each:

## What this creates

### Groups (3)


| Key               | Display name      | Job                          |
| ----------------- | ----------------- | ---------------------------- |
| `org_admins`      | `Org-Admins`      | Identity management + finops |
| `platform_admins` | `Platform-Admins` | Infra + deployments          |
| `developers`      | `Developers`      | Read-only prod debugging     |


### Permission sets (3)


| Permission set    | Session | Policy                                                                        | Grants                                                                                                                                                                                                                             |
| ----------------- | ------- | ----------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **OrgAdmin**      | 4h      | managed `AdministratorAccess`                                                 | Identity management (Identity Center, users, this config) + finops (billing). The standing identity admin once root is locked away.                                                                                               |
| **PlatformAdmin** | 4h      | inline `[platform-admin.json](policies/platform-admin.json)`                  | Infra + deployments: `ec2/ecr/s3/ssm/cloudwatch/logs/kms/iam:*` (its `ecr:*`/`ssm:*` cover image push and deploy-via-SSM). **Denies** `organizations/account/aws-portal`. Capped by the `protofast-boundary` permissions boundary. |
| **Developer**     | 8h      | managed `ViewOnlyAccess` + inline `[developer.json](policies/developer.json)` | Debug prod: read logs, SSM `StartSession` shell, pull images. **Denies** `ssm:SendCommand` and all writes.                                                                                                                       |


### Account assignments

Each group gets its one permission set in the single AWS account (`account_id`):


| Group             | Permission set |
| ----------------- | -------------- |
| `Org-Admins`      | OrgAdmin       |
| `Platform-Admins` | PlatformAdmin  |
| `Developers`      | Developer      |


## Where the groups come from (`identity_source`)

This only affects the three group objects — permission sets and assignments are
created the same way regardless.

- `**builtin`** (default) — Terraform creates the groups directly in Identity
  Center's own directory. Best for a small team with no external identity
  provider.
- `**external`** — your company already manages users/groups in an IdP (Okta,
  Entra, Google) that syncs into Identity Center via SCIM. Create the three
  groups there first; Terraform then just references them by display name (it
  can't create or edit IdP-owned groups). Switch to this when you federate.

## Genesis (one-time, run as account root)

The first apply has a bootstrap problem: creating the first SSO permission set
needs an identity that already holds admin, but no SSO admin exists yet. Rather
than hand-build a throwaway break-glass SSO user, use the one identity every
account already has unconditional admin on — the **account root** — then remove
its keys so root reverts to MFA-protected, console-only break-glass.

1. **Enable Identity Center** in the Organizations / Identity Center console (no
   Terraform resource does this). `aws sso-admin list-instances` must then return
   an instance ARN + identity store id.
2. **`infra/bootstrap` is applied** so the `protofast-boundary` customer-managed
   policy exists (attached to PlatformAdmin via `permissions_boundary_name`).
3. **Mint root access keys** for the account root user.
4. **As root, apply this config** (creates the three groups, permission sets, and
   assignments — cleanly, with no pre-existing group to collide with):
   ```sh
   cd infra/identity-center
   export AWS_ACCESS_KEY_ID=… AWS_SECRET_ACCESS_KEY=…   # root keys

   # Reuses the bucket bootstrap created (distinct state key). Derive the name the
   # same deterministic way — don't hand-type it. (Linux: sha256sum.)
   REPO=$(gh repo view --json nameWithOwner -q .nameWithOwner)
   BUCKET="protofast-tfstate-$(printf '%s' "$REPO" | shasum -a 256 | cut -c1-6)"
   ACCOUNT_ID=$(aws sts get-caller-identity --query Account --output text)

   terraform init -backend-config="bucket=$BUCKET" -backend-config="region=<region>"
   terraform apply \
     -var "account_id=$ACCOUNT_ID" \
     -var "identity_source=builtin"   # or external
   ```
5. **Initialize the first identity:** in the console (or your IdP), create your
   own SSO user and add it to **`Org-Admins`**. This is the only identity root has
   to seed — every other user is created later by OrgAdmin.
6. **Delete the root access keys.** Root now has no standing credentials; nothing
   below root can mint an OrgAdmin.

> Prefer not to touch root at all? Substitute a *temporary IAM admin user* in
> steps 3–6 and delete that user afterward — tidier than root keys and it doesn't
> trip root-key security alarms. Root's only edge is that it already exists.

## Ongoing (run as OrgAdmin)

Once genesis is done and root is locked away, **OrgAdmin is the standing identity
administrator**. A human in `Org-Admins`, signed in via SSO, owns from here on:

- **Users + membership** — create/disable SSO users and decide who is in
  `Org-Admins` / `Platform-Admins` / `Developers`. Membership is managed in the
  console (or, in `external` mode, in the upstream IdP via SCIM); Terraform does
  **not** manage human membership.
- **Permission-set changes** — every subsequent `terraform apply` of this config
  (policy edits, new permission sets, session durations) runs under OrgAdmin, not
  root.

Log in and apply:

```sh
aws configure sso          # set the SSO start URL + region, pick the account
export AWS_PROFILE=<profile>
aws sso login

cd infra/identity-center
REPO=$(gh repo view --json nameWithOwner -q .nameWithOwner)
BUCKET="protofast-tfstate-$(printf '%s' "$REPO" | shasum -a 256 | cut -c1-6)"
ACCOUNT_ID=$(aws sts get-caller-identity --query Account --output text)
terraform init -backend-config="bucket=$BUCKET" -backend-config="region=<region>"
terraform apply \
  -var "account_id=$ACCOUNT_ID" \
  -var "identity_source=builtin"
```

### Variables


| Variable                    | Default              | Notes                                                                                    |
| --------------------------- | -------------------- | ---------------------------------------------------------------------------------------- |
| `account_id`                | —                    | AWS account hosting IC, billing, and workload. **Required.**                             |
| `identity_source`           | `builtin`            | `builtin` (Terraform creates groups) or `external` (groups synced from an IdP via SCIM). |
| `project`                   | `protofast`          | Prefixes group descriptions.                                                             |
| `aws_region`                | `us-east-1`          | Region of the IC instance.                                                               |
| `permissions_boundary_name` | `protofast-boundary` | Boundary attached to PlatformAdmin.                                                      |


### Outputs

`instance_arn`, `permission_set_arns` (by name), `group_ids` (by key).

## Still manual after this

- Enforce MFA org-wide in IC settings; the admin sets already carry shorter
  sessions (4h) than Developer (8h).
- Keep root keyless and MFA-protected — it is the account's last-resort break-glass.
- (Optional) Federate to an upstream IdP with SAML + SCIM and switch
  `identity_source = "external"`. Release authority stays a **GitHub** concern
  (branch protection + the `infra`/`production` Environment reviewers).
</content>
</invoke>
