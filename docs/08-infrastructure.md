# 08 · Infrastructure

*What Terraform creates in AWS and Cloudflare, how it is run, and which knobs
matter. Self-contained.*

## Three Terraform roots

| Root | Runs where | Creates |
|---|---|---|
| [`infra/bootstrap`](../../infra/bootstrap) | **locally, once**, with admin credentials | the S3 state bucket, the GitHub OIDC provider, the `protofast-infra` and `protofast-deploy` roles, a permissions boundary, and (optionally) the GitHub repo variables and secrets |
| [`infra/identity-center`](../../infra/identity-center) | locally, once | AWS Identity Center permission sets (platform-admin, developer) and the SES sender IAM user |
| [`infra/`](../../infra) | GitHub Actions (`infra.yml`) | everything the running system needs |

The split exists because the main root runs in CI, and CI needs a state bucket and
an OIDC role that nothing has created yet. Bootstrap breaks that cycle from an
operator's machine and keeps its state on local disk.

## Running the main root

`infra.yml` is manual-dispatch only, with `plan` / `apply` / `destroy`. It assumes
`protofast-infra` via OIDC and runs in the `infra` GitHub Environment — put
required reviewers there to gate applies. Its inputs come from repo settings:

| Repo **variables** | Repo **secrets** |
|---|---|
| `AWS_REGION`, `TFSTATE_BUCKET`, `ASSETS_BUCKET` | `AWS_INFRA_ROLE_ARN`, `AWS_DEPLOY_ROLE_ARN`, `ECR_REGISTRY`, `CLOUDFLARE_API_TOKEN`, `CLOUDFLARE_ACCOUNT_ID`, `CLOUDFLARE_ZONE`, `ADMIN_DOMAIN`, `PROTOFAST_DOMAIN`, `KEYCLOAK_DOMAIN`, optional `TELEMETRY_DOMAIN` + `TELEMETRY_ACCESS_EMAILS` |

"Update the infrastructure" is just editing `infra/*.tf` and running `apply` again.

## What the main root creates

**Network** (`network.tf`) — adopts the account's default VPC/subnet and makes them
dual-stack: public IPv4 plus IPv6 with an egress-only gateway. **No inbound ports.**
Egress carries image pulls, SSM and the Cloudflare tunnel.

**Compute** (`compute.tf`) — two Amazon Linux 2023 instances sharing an instance
profile:

| | Host A (`Role=edge`) | Host B (`Role=services`) |
|---|---|---|
| Default type | `t4g.small` | `t4g.medium` |
| Runs | cloudflared, Envoy, clients host, otel-collector, Aspire dashboard | auth, payments, api, Keycloak, Postgres, Redis |
| Private IP | static, `cidrhost(subnet, 10)` | static, `cidrhost(subnet, 11)` |
| AMI | floats with the latest AL2023 | **pinned** (`ignore_changes = [ami]`) |
| `user_data_replace_on_change` | `true` — pure cattle | `false` — it holds state |

Static IPs are derived from the subnet CIDR rather than read from the peer
instance, which would be a Terraform cycle (each host's boot config needs the
other's address).

Each host renders its own cloud-init from `infra/templates/` (a shared install
fragment plus a per-role template). Boot seeds `/opt/protofast/.env` with the
stable values — `ECR`, `AWS_REGION`, `HOST_ROLE`, the peer IP, `CLIENTS`, the client
domains, `ASSETS_BUCKET` — and then pulls the published manifest, compose file and
`deploy.sh` from S3 and bootstraps the stack.

**Storage** —
`ebs.tf`: a separate encrypted gp3 volume for Postgres, `prevent_destroy = true`,
attached to Host B and pinned to the subnet's AZ so instance replacement only
detaches and reattaches it.
`assets.tf`: the S3 bucket holding built client bundles (`clients/<name>/<tag>/`)
and the published deploy manifest. Fully private; only failed multipart uploads are
lifecycle-expired, because the live client tag may sit unchanged for a long time.
`ecr.tf`: one immutable-tag repository per image, scan-on-push, keep the last 20.

**Secrets** (`secrets.tf`) — the empty `protofast/app` shell only; see
[layer 06](06-secrets.md).

**IAM** (`iam.tf`) — the instance profile (S3 read, ECR pull, Secrets Manager read,
SSM plus a dedicated policy for streaming SSM output to CloudWatch Logs).

**Email** (`ses.tf`) — an SES domain identity on the zone with a custom MAIL FROM
subdomain for SPF/DMARC alignment, the DNS records that prove it, and the outputs
Keycloak's SMTP settings are built from. Toggle with `enable_ses`.

**Cloudflare** (`cloudflare.tf`, `access.tf`, `cdn.tf`) — the zone is referenced,
never created. Terraform owns:

- a Zero Trust **tunnel** and its ingress map: every client hostname and the
  Keycloak hostname → `https://envoy:8443` (`noTLSVerify`, Host header preserved);
  the optional telemetry hostname → the Aspire dashboard;
- proxied **CNAMEs** for each hostname to `<tunnel-id>.cfargotunnel.com`;
- zone settings: Always-Use-HTTPS and Full TLS;
- **cache rules**: bypass for `/auth/`, `/payments/`, `/api/`, `/otlp/` and any
  non-GET; cache static assets *respecting origin Cache-Control* (so Angular's
  hashed bundles get a year and Keycloak's theme files get their own short TTL);
- a **Cloudflare Access** app and email allow-list in front of the telemetry
  hostname, enabled only when both telemetry variables are set.

## The knobs you are most likely to touch

| Variable | Default | Effect |
|---|---|---|
| `host_a_instance_type` / `host_b_instance_type` | `t4g.small` / `t4g.medium` | sizing per role |
| `pgdata_volume_gb` | `20` | Postgres volume size |
| `root_volume_gb` | `30` | image + rollback storage per host |
| `admin_domain`, `protofast_domain`, `keycloak_domain` | — | public hostnames (also feed `.env` and the realm) |
| `telemetry_domain` + `telemetry_access_emails` | empty | both required to expose the Aspire dashboard |
| `enable_ses`, `ses_from_local_part`, `ses_mail_from_subdomain`, `dmarc_rua` | SES on, `no-reply`, `bounce` | outbound mail identity |
| `compose_plugin_version`, `grpc_health_probe_version` | pinned | on-host tooling installed by cloud-init |
| `ecr_repositories` | seven repos | must match the image names in the compose files |

## Safety rails worth remembering

- The pgdata volume has `prevent_destroy`; deleting it is a deliberate manual act.
- Host B's AMI is pinned so an unrelated apply cannot silently replace the box that
  holds the database. Rebuild it deliberately with
  `terraform apply -replace=aws_instance.host_b`, after `deploy.sh drain`.
- Terraform never sees a secret value: the CI role can create and describe the
  secret but is denied its value APIs.
