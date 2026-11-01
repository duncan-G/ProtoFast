# infra/people

Identity Center's groups and permission sets. An Org Admin applies it after bootstrap; its
state is `people/terraform.tfstate` in bootstrap's state bucket.

```bash
aws sso login --profile org-admin
```

```bash
export AWS_PROFILE=org-admin
```

```bash
terraform -chdir=infra/people init -reconfigure -backend-config="bucket=$(terraform -chdir=infra/bootstrap output -raw state_bucket)" -backend-config=region=us-west-2
```

```bash
terraform -chdir=infra/people apply
```

Run from the repo root. `terraform.tfvars` is `region = "us-west-2"` and
`accounts = { prod = "<account id>" }`. Without bootstrap's local state, the bucket is
`gh variable get PROTOSTRAP_STATE_BUCKET --env prod`. People are added to the groups in
Identity Center.
