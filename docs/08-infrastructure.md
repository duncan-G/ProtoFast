# 08 · Infrastructure

*Which Terraform roots exist, what they create in AWS and Cloudflare, how they are run, and
which knobs matter. Self-contained.*

## Three kinds of root

ProtoFast's Terraform is a few small roots, each calling one of ProtoStrap's modules at
`?ref=v0.1.0` ([ProtoStrap's infra](https://github.com/duncan-G/protostrap/blob/v0.1.0/infra/README.md)).
Everything about the application comes from its spec; the roots hold only the account's own
settings. Prod is the one deployed environment: dev is the local run, and its account holds only
development secrets, so nothing is bootstrapped or deployed in it.

| Root                                            | Module      | Applied by                                         | Creates                                       |
| ----------------------------------------------- | ----------- | -------------------------------------------------- | --------------------------------------------- |
| [`infra/people`](../infra/people/main.tf)       | `people`    | an Org Admin, once per organization, from the management account (or Identity Center's delegated administrator) | Identity Center groups and permission sets |
| [`infra/bootstrap/prod`](../infra/bootstrap/prod/main.tf) | `bootstrap` | an Org Admin, once, with prod's admin credentials | the state bucket, GitHub's OIDC provider, the Infra and Deploy roles and their boundaries, and the account's two GitHub environments |
| [`infra/prod`](../infra/prod/main.tf) | `platform` | the infra workflow, and `deploy.yml` when a release changes the infrastructure, as the Infra role | every AWS and Cloudflare resource the spec needs |

The split exists because the platform root runs in CI, and CI needs a state bucket and roles
that nothing has created yet. Bootstrap makes them from an Org Admin's machine; its state
stays there, gitignored, and everything in it can be imported again.

## People

`infra/people` gives three groups their permission sets
([people](https://github.com/duncan-G/protostrap/blob/v0.1.0/docs/identity.md#people)). People
sign in through IAM Identity Center with MFA; there are no IAM users and no access keys.

| Group             | Can                                                        | Where       |
| ----------------- | ---------------------------------------------------------- | ----------- |
| `Org-Admins`      | everything (AdministratorAccess): bootstrap, secrets       | every account |
| `Platform-Admins` | view read-only, open the operator UI and Grafana           | every account |
| `Developers`      | view read-only, manage development secrets (`Developer`)   | dev (`developer_environments = ["dev"]`) |
| `Developers`      | open Grafana, for their services' telemetry, and nothing else (`ServiceTelemetry`) | prod, and any account not in `developer_environments` |

Its inputs are Identity Center's region and `accounts`, dev's and prod's AWS account ids.
People are added to the groups in Identity Center.

## Bootstrap

Prod's bootstrap root takes `terraform.tfvars` (copy `terraform.tfvars.example`):

| Variable                      | Meaning                                                          |
| ----------------------------- | ---------------------------------------------------------------- |
| `github_repository`           | `owner/repo`, for the OIDC trust and the GitHub environments     |
| `region`                      | default `us-east-1`                                              |
| `cloudflare_account_id`, `cloudflare_zone` | the zone the hosts and mail records go in (`protofast.dev`) |
| `hosts`                       | each of the spec's hosts' hostname in this account               |
| `email_domains`               | the domains this account sends mail from                          |
| `alert_emails`                | who gets operator alerts (the example names one; each must confirm SNS's email) |
| `create_github_oidc_provider` | false if the account already has GitHub's provider              |

The module is told `app = "protofast"`, so the secret is `protofast/<environment>`. It writes
`AWS_ROLE_ARN`, `AWS_REGION`, the bucket and registry names to the `prod` and `prod-infra`
GitHub environments, and the platform root's inputs to `prod-infra` as `TF_VAR_*`. To change
a host or an email domain, change `terraform.tfvars` and apply bootstrap again.

```bash
cp infra/bootstrap/prod/terraform.tfvars.example infra/bootstrap/prod/terraform.tfvars   # then fill it in
GITHUB_TOKEN=$(gh auth token) terraform -chdir=infra/bootstrap/prod init
GITHUB_TOKEN=$(gh auth token) terraform -chdir=infra/bootstrap/prod apply
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
`platform/terraform.tfstate`, given at init by the workflow.

From the spec the module creates
([infra workflow](https://github.com/duncan-G/protostrap/blob/v0.1.0/docs/protostrap.md#infra-workflow)):

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
- **ProtoStrap's own:** the deploy, files, backups and telemetry buckets, and the SSM documents
  for deploys, the operator UI and Grafana.

`infra/prod` sets what differs from the module's defaults:

| Setting                 | Prod                                         |
| ----------------------- | -------------------------------------------- |
| `protect_data_vms`      | `true` (default): data-1 can't be terminated by API |
| `force_destroy`         | `false` (default)                            |
| `log_retention_days`    | 90                                           |
| `backup_retention_days` | 35                                           |
| `snapshot_retention`    | 14                                           |
| `client_rate_limit`     | 0 for now: size it from real traffic before go-live (requests per client per 10 s) |

It doesn't set `zones`, `vpc_cidr` (it can't change later), `email_dmarc` (so no DMARC record
is published) or `operator_hosts`.

## Running the platform root

| Workflow           | Runs                                                                    |
| ------------------ | ----------------------------------------------------------------------- |
| `infra.yml`        | by hand with `action=plan`, `apply` or `destroy`; `apply` on a push to `main` touching `infra/prod/**` |
| `deploy.yml`       | its `infra` job applies the root when a release's plan says the infrastructure changed |

Both run in the `prod-infra` GitHub environment with the Infra role, and need
`CLOUDFLARE_API_TOKEN` there.

```bash
gh secret set CLOUDFLARE_API_TOKEN --env prod-infra
gh workflow run infra.yml -f action=plan
gh workflow run infra.yml -f action=apply
```

The first time, in order: `infra/people` (once for the organization), bootstrap, the
Cloudflare token, the platform's plan and apply, then the secret
([layer 06](06-secrets.md#filling-an-environments-secret)), and only then the first deploy.
The GitHub settings in [layer 07](07-deployment.md#github-settings) come first.

## The knobs you are most likely to touch

| Knob                                                       | Where                              | Effect                                   |
| ---------------------------------------------------------- | ---------------------------------- | ---------------------------------------- |
| VM types, pool sizes, spare VMs, replica counts, a component's request, `stop`, scaling, routes, volume size | `apphost/Program.cs` | prod, through the spec         |
| Hostnames, email domains, alert emails, the Cloudflare zone | `infra/bootstrap/prod/terraform.tfvars`, then bootstrap again | the platform root's `TF_VAR_*` |
| Retention, destroy protection, the per-client rate limit   | `infra/prod/main.tf`               | prod                                     |
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
