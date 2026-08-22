# infra/identity-center

AWS Identity Center (SSO) access model. Has its own Terraform state, separate from the workload `infra/`. It is **never** applied by the CI OIDC roles — those are capped by `protofast-boundary`, which denies `organizations:`*/*`account:`, so they can't manage identities. The first apply is run by the same **account root** (genesis) identity that applied
`[../bootstrap/README.md](../bootstrap/README.md)`; every apply after that is run by a human holding **OrgAdmin**.

This config creates the **groups**, the **permission sets**, and the **account assignments** that wire them together. People are not in Terraform: add them in the Identity Center console.

Three groups, three jobs — one permission set each:


| Group             | Permission set  | Session | Policy                                                                        | Job                                                                                                                      |
| ----------------- | --------------- | ------- | ----------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------ |
| `Org-Admins`      | `OrgAdmin`      | 4h      | managed `AdministratorAccess`                                                 | Identity management + finops. Standing admin once root is locked.                                                        |
| `Platform-Admins` | `PlatformAdmin` | 4h      | inline `[platform-admin.json](policies/platform-admin.json)`                  | Infra + deployments (`ecr`/`ssm` cover push and deploy). Boundary-capped, denies `organizations`/`account`/`aws-portal`. |
| `Developers`      | `Developer`     | 8h      | managed `ViewOnlyAccess` + inline `[developer.json](policies/developer.json)` | Debug prod: read logs, SSM `StartSession`, pull images. No writes.                                                       |



| Variable                    | Default              | Notes                                                                   |
| --------------------------- | -------------------- | ----------------------------------------------------------------------- |
| `project`                   | `protofast`          | Prefixes group descriptions.                                            |
| `aws_region`                | `us-west-2`          | Region of the IC instance.                                              |
| `permissions_boundary_name` | `protofast-boundary` | Boundary attached to `PlatformAdmin`; created by `infra/bootstrap`.     |
| `ses_sender_zone`           | `""`                 | Domain the SES sender may send from. Empty omits the sender. See below. |
| `ses_from_local_part`       | `no-reply`           | Local part of the sender's only allowed From address.                   |
| `ses_region`                | `""`                 | Region of the SES identity; falls back to `aws_region`.                 |


Outputs: `instance_arn`, `permission_set_arns`, `group_ids`, `ses_smtp_user`, `ses_from_address`.

## SES sender

`[ses-sender.tf](ses-sender.tf)` creates one service account — `protofast-ses-smtp`, the IAM user behind Keycloak's SMTP credential — plus an inline policy letting it send *only* as `<ses_from_local_part>@<ses_sender_zone>`. It carries `protofast-boundary`, so the key can never reach past that ceiling.

It sits in this root purely because of who applies it. `protofast-boundary` denies `iam:CreateUser` to both the CI infra role and `PlatformAdmin`, so OrgAdmin is the only identity in the account that can create it. Nothing about it is an SSO concern.

Set `ses_sender_zone` to the same apex domain as `infra/`'s `cloudflare_zone` (and keep `ses_from_local_part` / `ses_region` in step with that root's `ses_from_local_part` / `aws_region` — they are duplicated across two states, and a mismatch surfaces only as SES refusing to send). There is no ordering constraint: IAM does not check that the SES identity in the policy exists, so this can be applied before `infra/` ever creates it.

The **access key** is deliberately not managed here — `aws_iam_access_key` would write the secret into this root's state. Mint it out of band per [../README.md](../README.md) section 4.2.

## State bucket

State lives in the bucket `infra/bootstrap` created, under the
`identity-center/terraform.tfstate` key. It is not a variable: a Terraform
`backend` block cannot reference variables or locals, so the name is passed to
`terraform init` instead. Derive it the same way bootstrap names it —
`<project>-tfstate-<first 9 hex of sha256(owner/repo)>`:

```sh
SHA=$(command -v sha256sum || echo "shasum -a 256")
REPO=$(gh repo view --json nameWithOwner -q .nameWithOwner)
BUCKET="protofast-tfstate-$(printf '%s' "$REPO" | $SHA | cut -c1-9)"
```

`gh variable get TFSTATE_BUCKET` returns the same name when bootstrap ran with
`manage_github_repo = true`.

## Genesis (one-time)

Creating the first permission set needs an identity that already holds admin, and no SSO admin exists yet. Create that identity, enable Identity Center, and apply bootstrap first — see `[../bootstrap/README.md](../bootstrap/README.md)`
("Genesis identity"). Then continue here with those keys still exported.

### 1. Fill terraform.tfvars and apply

Copy `[terraform.tfvars.example](terraform.tfvars.example)` to `terraform.tfvars` (gitignored) and set `aws_region`. Init needs the state bucket name passed on the command line (see [State bucket](#state-bucket)); apply reads the rest from tfvars.

```sh
cd "$(git rev-parse --show-toplevel)/infra/identity-center"
cp -n terraform.tfvars.example terraform.tfvars   # then edit aws_region

SHA=$(command -v sha256sum || echo "shasum -a 256")
REPO=$(gh repo view --json nameWithOwner -q .nameWithOwner)
BUCKET="protofast-tfstate-$(printf '%s' "$REPO" | $SHA | cut -c1-9)"

terraform init \
  -backend-config="bucket=$BUCKET" \
  -backend-config="region=$AWS_DEFAULT_REGION"
terraform apply
```



### 2. Create yourself as OrgAdmin

Identity Center cannot set a password via API. In the console: **Users** →**Add user** (your email), add them to **Org-Admins**, then **Reset password**.

### 3. Sign in as OrgAdmin, then delete genesis

Swap the genesis keys for SSO and delete the genesis identity — that deletion revokes the shell that created it, so it has to run under the new credentials.

```sh
unset AWS_ACCESS_KEY_ID AWS_SECRET_ACCESS_KEY
aws configure sso --profile protofast-orgadmin   # start URL from the IC console
export AWS_PROFILE=protofast-orgadmin

for K in $(aws iam list-access-keys --user-name protofast-genesis \
             --query 'AccessKeyMetadata[].AccessKeyId' --output text); do
  aws iam delete-access-key --user-name protofast-genesis --access-key-id "$K"
done
aws iam detach-user-policy --user-name protofast-genesis \
  --policy-arn arn:aws:iam::aws:policy/AdministratorAccess
aws iam delete-user --user-name protofast-genesis
```

If you used root keys instead, delete them in IAM → Security credentials.

## Ongoing (as OrgAdmin)

OrgAdmin is the standing identity administrator and runs every later apply. Granting or revoking access is a console change to group membership, not a Terraform edit. New users still need **Reset password** to get in.

Only the credentials differ from step 1 — SSO instead of static keys. SSO can't be used for the first run because the profile below depends on the permission set and assignment that the first apply creates.

```sh
export AWS_PROFILE=protofast-orgadmin AWS_DEFAULT_REGION=us-west-2
aws sso login
terraform apply
```

