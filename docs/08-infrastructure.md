# 08 · Infrastructure

*Which Terraform roots exist, what they create in AWS and Cloudflare, how they are run, and
which knobs matter. Self-contained.*

## Three kinds of root

ProtoFast's Terraform is a few small roots, each calling one of ProtoStrap's modules at
`?ref=v0.1.10` ([ProtoStrap's infra](https://github.com/duncan-G/protostrap/blob/v0.1.10/infra/README.md)).
Everything about the application comes from its spec; the roots hold only the account's own
settings. Prod is the one deployed environment, in ProtoFast's one account, which also holds
Identity Center and dev's secret, `protofast/dev`. Dev is the local run: nothing is
bootstrapped or deployed for it.

| Root                                            | Module      | Applied by                                         | Creates                                       |
| ----------------------------------------------- | ----------- | -------------------------------------------------- | --------------------------------------------- |
| [`infra/bootstrap`](../infra/bootstrap/main.tf) | `bootstrap` | an Org Admin, once, with the account's admin credentials | the state bucket, the deploy bucket, GitHub's OIDC provider, the Infra and Deploy roles and their boundaries, and the account's two GitHub environments |
| [`infra/people`](../infra/people/main.tf)       | `people`    | an Org Admin, once, after bootstrap, with the account's admin credentials (it is its organization's management account), after enabling Identity Center in the console | Identity Center groups and permission sets |
| [`infra/prod`](../infra/prod/main.tf) | `platform` | the infra workflow, and `deploy.yml` when a release changes the infrastructure, as the Infra role | every AWS and Cloudflare resource the spec needs |

The split exists because the platform root runs in CI, and CI needs a state bucket and roles
that nothing has created yet. The deploy bucket is bootstrap's for the same reason: the infra
workflow puts the `protostrap` binary in it before the platform's first apply makes a VM, so
every VM boots with its agent. Bootstrap makes them from an Org Admin's machine; its state
stays there, gitignored, and everything in it can be imported again.

## People

`infra/people` gives three groups their permission sets
([people](https://github.com/duncan-G/protostrap/blob/v0.1.10/docs/identity.md#people)). People
sign in through IAM Identity Center with MFA; there are no IAM users and no access keys.

| Group             | Can                                                        | Where       |
| ----------------- | ---------------------------------------------------------- | ----------- |
| `Org-Admins`      | everything (AdministratorAccess): bootstrap, secrets       | every account |
| `Platform-Admins` | view read-only, open the operator UI and Grafana           | every account |
| `Developers`      | view read-only, manage `protofast/dev`, open Grafana for their services' telemetry | every account |

Developers can't read `protofast/prod`, which the platform tags, or open the operator UI. The
root's inputs are Identity Center's region and `accounts`, the one account's id as `prod`.
Identity Center must be enabled in the console first: the apply fails without it. People are
added to the groups in Identity Center. Its state is `people/terraform.tfstate` in bootstrap's
state bucket, which the Infra role's boundary keeps CI out of; the bucket is given at init:

```bash
terraform -chdir=infra/people init -backend-config="bucket=$(terraform -chdir=infra/bootstrap output -raw state_bucket)" -backend-config=region=us-west-2
terraform -chdir=infra/people apply
```

## Bootstrap

The account's bootstrap root takes `terraform.tfvars` (copy `terraform.tfvars.example`):

| Variable                      | Meaning                                                          |
| ----------------------------- | ---------------------------------------------------------------- |
| `github_repository`           | `owner/repo`, for the OIDC trust and the GitHub environments     |
| `region`                      | default `us-west-2`                                              |
| `cloudflare_account_id`, `cloudflare_zone` | the zone the hosts and mail records go in (`protofast.dev`) |
| `hosts`                       | each of the spec's hosts' hostname in this account               |
| `email_domains`               | the domains this account sends mail from                          |
| `alert_emails`                | who gets operator alerts (the example names one; each must confirm SNS's email) |
| `create_github_oidc_provider` | false if the account already has GitHub's provider              |

The module is told `app = "protofast"`, so the secret is `protofast/<environment>`. It writes
`AWS_ROLE_ARN`, `AWS_REGION`, the bucket and registry names to the `prod` and `prod-infra`
GitHub environments, and the platform root's inputs to `prod-infra` as `TF_VAR_*`. GitHub
shows those in uppercase (`TF_VAR_CLOUDFLARE_ZONE`); the infra workflow lowercases them again,
so the root's variables stay lowercase. To change a host or an email domain, change
`terraform.tfvars` and apply bootstrap again.

```bash
cp infra/bootstrap/terraform.tfvars.example infra/bootstrap/terraform.tfvars   # then fill it in
GITHUB_TOKEN=$(gh auth token) terraform -chdir=infra/bootstrap init
GITHUB_TOKEN=$(gh auth token) terraform -chdir=infra/bootstrap apply
```

## Hosts and mail

| Host        | Prod                    |
| ----------- | ----------------------- |
| `protofast` | `protofast.dev`         |
| `theplot`   | `theplot.protofast.dev` |
| `admin`     | `admin.protofast.dev`   |
| `auth`      | `auth.protofast.dev`    |
| Email from  | `protofast.dev`         |

- Prod sends from the protofast host's domain. `email_domains` replaces the spec's
  `email.domains` (`protofast.dev`, from `AddProtoStrapEmail`) in the account.
- The spec's `hosts:` show the prod names the AppHost gives; the account's `hosts` variable is
  what each replica's `HOST_<NAME>` holds.

## The platform root

`infra/prod` calls the platform module with the spec, `protostrap.yaml`, `environment = "prod"`,
and the inputs bootstrap wrote. State is in the bootstrap's bucket, key
`infra/terraform.tfstate` in the root's backend block; the workflow gives the bucket and
region at init.

From the spec the module creates
([infra workflow](https://github.com/duncan-G/protostrap/blob/v0.1.10/docs/protostrap.md#infra-workflow)):

- **Network:** a VPC (`10.20.0.0/16` by default, in one zone) and its subnets. Only the
  ingress VMs have public IPs, and nothing is admitted from the internet: the tunnel is an
  outbound connection.
- **VMs:** an ingress VM group of one per zone; the general pool's Auto Scaling groups; the
  compute pool's Spot groups with an On-Demand fallback; data-1, a plain instance
  with a fixed private IP and automatic recovery. The scheduler owns how many run.
- **Volumes:** PostgreSQL's (20 GiB) and Redis's (1 GiB) on data-1, each outliving its VM, and
  telemetry's.
- **Per component:** an ECR repository, a role built from its `uses` (the bucket, the queues it
  sends to, `email`), and a log group.
- **Queues:** `document-upload` and `workflow-outcomes` (FIFO), each with a dead-letter queue.
- **Bucket:** `documents`, with browser uploads, versions kept 7 days and object lock.
- **Email:** an SES identity with DKIM and a `bounce.` MAIL FROM domain per email domain, and
  their DNS records in Cloudflare.
- **The secret:** `protofast/<environment>`, empty.
- **Cloudflare:** the tunnel, and a DNS record per host pointing at it.
- **ProtoStrap's own:** the files, backups and telemetry buckets, the deploy bucket's
  lifecycle rules (bootstrap made the bucket), and the SSM documents for deploys, the operator
  UI and Grafana.

`infra/prod` sets these; only the log and snapshot retentions differ from the module's
defaults:

| Setting                 | Prod                                         |
| ----------------------- | -------------------------------------------- |
| `protect_data_vms`      | `true` (default): data-1 can't be terminated by API |
| `force_destroy`         | `false` (default)                            |
| `log_retention_days`    | 90 (default 30)                              |
| `backup_retention_days` | 35 (default)                                 |
| `snapshot_retention`    | 14 (default 7)                               |

It doesn't set `zones`, `vpc_cidr` (it can't change later), `email_dmarc` (so no DMARC record
is published) or `operator_hosts`.

## Running the platform root

| Workflow           | Runs                                                                    |
| ------------------ | ----------------------------------------------------------------------- |
| `infra.yml`        | by hand with `action=plan`, `apply` or `destroy`; `apply` on a push to `main` touching `infra/prod/**` |
| `deploy.yml`       | its `infra` job applies the root when a release's plan says the infrastructure changed |

Both run in the `prod-infra` GitHub environment with the Infra role, and need
`CLOUDFLARE_API_TOKEN` there. For the spec's `cdn` the token needs Zone WAF Edit and Cache
Rules Edit on the zone, beside what the tunnel and DNS records take.

```bash
gh secret set CLOUDFLARE_API_TOKEN --env prod-infra
gh workflow run infra.yml -f action=plan
gh workflow run infra.yml -f action=apply
```

The first time, in order: bootstrap, `infra/people` (once for the organization), the
Cloudflare token, the GitHub settings in [layer 07](07-deployment.md#github-settings), the
merge that brings the workflows to `main` (reject the runs it starts), the platform's plan and
apply, the alert emails' SNS confirmations, then the secret
([layer 06](06-secrets.md#filling-an-environments-secret)), and only then the first deploy.
The [README](../README.md#deploying) has the steps.

## The knobs you are most likely to touch

| Knob                                                       | Where                              | Effect                                   |
| ---------------------------------------------------------- | ---------------------------------- | ---------------------------------------- |
| VM types, pool sizes, spare VMs, replica counts, a component's request, `stop`, scaling, routes, volume size | `apphost/Program.cs` | prod, through the spec         |
| Hostnames, email domains, alert emails, the Cloudflare zone | `infra/bootstrap/terraform.tfvars`, then bootstrap again | the platform root's `TF_VAR_*` |
| Cloudflare's WAF and cache rules, its rate limit           | `AddCloudflareCdn` in `apphost/Program.cs` | prod, through the spec          |
| Retention, destroy protection                              | `infra/prod/main.tf`               | prod                                     |
| What the Infra role or a component role may ever do        | bootstrap's `extra_infra_actions`, `extra_role_actions` (an Org Admin) | the boundaries |

## Safety rails worth remembering

- Terraform never deletes a stateful component's volume: a plan that would fails.
- Prod's data-1 can't be terminated by API, and prod's buckets and repositories can't be
  destroyed while they hold anything.
- Terraform never sees a secret value: the secret is created empty.
- Bootstrap's boundaries cap what the Infra and component roles can do, whatever a root asks.
- Put required reviewers on `prod-infra`; without them, a push touching `infra/prod/**`
  applies with no approval.

## Known gaps

- **Unverified.** No root has been applied yet; `terraform validate` in CI is all that has
  run.
