# Two app servers (two-instance restructure, docs/two-instance-restructure-plan.md):
#   Host A — edge: cloudflared, Envoy, the unified SSR clients host, otel-collector,
#            Aspire Dashboard. Pure cattle.
#   Host B — services + state: the three gRPC services, Keycloak, Postgres (on the
#            persistent pgdata EBS volume), Redis. NOT pure cattle — its data lives
#            on the EBS volume, reattached on replacement (ebs.tf, §8 risk).
#
# Each host renders its OWN user_data template (a shared _common fragment + a
# per-role template) so a boot-config edit to one host never force-replaces the
# other (§6.1). Both share the instance profile (S3 read + ECR pull).

data "aws_caller_identity" "current" {}

# Latest Amazon Linux 2023 AMI for the instance architecture (both hosts share an
# arch — var.instance_arch). Resolved via the public SSM parameter AWS keeps
# current, so there's no hard-coded AMI ID to age.
data "aws_ssm_parameter" "al2023" {
  name = "/aws/service/ami-amazon-linux-latest/al2023-ami-kernel-default-${local.ssm_arch}"
}

locals {
  # Dualstack endpoint (.dkr-ecr.<region>.on.aws): resolves over IPv4 + IPv6, so
  # image pulls prefer IPv6 where available.
  ecr_registry = "${data.aws_caller_identity.current.account_id}.dkr-ecr.${var.aws_region}.on.aws"

  # The IPv4-only endpoint CI pushes to and the deploy workflow passes as ${ECR}.
  # Both endpoints must be registered with the credential helper (exact-hostname keyed).
  ecr_registry_ipv4 = "${data.aws_caller_identity.current.account_id}.dkr.ecr.${var.aws_region}.amazonaws.com"

  # The AL2023 SSM AMI parameter uses arm64/x86_64.
  ssm_arch = var.instance_arch == "arm64" ? "arm64" : "x86_64"

  # Compose release assets use aarch64/x86_64; grpc_health_probe uses arm64/amd64.
  compose_arch = var.instance_arch == "arm64" ? "aarch64" : "x86_64"

  # Static private IPs, derived from the default subnet CIDR so they are in-range
  # by construction and reference the SUBNET (not the peer instance) — no cycle.
  host_a_private_ip = cidrhost(aws_default_subnet.default.cidr_block, var.host_a_ip_offset)
  host_b_private_ip = cidrhost(aws_default_subnet.default.cidr_block, var.host_b_ip_offset)

  # Shared install fragment (engine + compose plugin + ECR credential helper +
  # grpc_health_probe), rendered once and embedded verbatim into both per-host
  # templates. templatefile does NOT re-parse the inserted value, so $ / %{ inside
  # the common script are safe.
  common_setup = templatefile("${path.module}/templates/_common.sh.tftpl", {
    ecr_registry              = local.ecr_registry
    ecr_registry_ipv4         = local.ecr_registry_ipv4
    compose_plugin_version    = var.compose_plugin_version
    compose_arch              = local.compose_arch
    grpc_health_probe_version = var.grpc_health_probe_version
    grpc_health_probe_arch    = var.instance_arch
  })

  user_data_host_a = templatefile("${path.module}/templates/user_data.host_a.sh.tftpl", {
    common_setup     = local.common_setup
    ecr_registry     = local.ecr_registry
    aws_region       = var.aws_region
    admin_domain     = var.admin_domain
    protofast_domain = var.protofast_domain
    keycloak_domain  = var.keycloak_domain
    default_client   = "protofast"
    clients          = "admin,protofast"
    assets_bucket    = aws_s3_bucket.assets.bucket
    tunnel_token     = local.tunnel_token
    host_b_ip        = local.host_b_private_ip
  })

  user_data_host_b = templatefile("${path.module}/templates/user_data.host_b.sh.tftpl", {
    common_setup         = local.common_setup
    ecr_registry         = local.ecr_registry
    aws_region           = var.aws_region
    keycloak_domain      = var.keycloak_domain
    assets_bucket        = aws_s3_bucket.assets.bucket
    host_a_ip            = local.host_a_private_ip
    kc_db_password   = random_password.kc_db.result
    auth_db_password = random_password.auth_db.result
  })
}

# --- Host A — edge ------------------------------------------------------------
resource "aws_instance" "host_a" {
  ami                    = data.aws_ssm_parameter.al2023.value
  instance_type          = var.host_a_instance_type
  subnet_id              = aws_default_subnet.default.id
  private_ip             = local.host_a_private_ip
  vpc_security_group_ids = [aws_security_group.instance.id]
  iam_instance_profile   = aws_iam_instance_profile.instance.name

  associate_public_ip_address = true
  ipv6_address_count          = 1

  user_data                   = local.user_data_host_a
  user_data_replace_on_change = true

  metadata_options {
    http_tokens   = "required" # IMDSv2 only
    http_endpoint = "enabled"
    # The clients-host entrypoint runs the AWS CLI inside a container (S3 pull),
    # one network hop behind the host, so IMDS needs hop limit 2 here.
    http_put_response_hop_limit = 2
    http_protocol_ipv6          = "enabled"
  }

  root_block_device {
    volume_size = var.root_volume_gb
    volume_type = "gp3"
    encrypted   = true
  }

  tags = {
    Name                   = "${var.project}-host-a"
    (var.instance_tag_key) = var.instance_tag_value
    Role                   = "edge"
  }
}

# --- Host B — services + state ------------------------------------------------
resource "aws_instance" "host_b" {
  ami                    = data.aws_ssm_parameter.al2023.value
  instance_type          = var.host_b_instance_type
  subnet_id              = aws_default_subnet.default.id
  private_ip             = local.host_b_private_ip
  vpc_security_group_ids = [aws_security_group.instance.id]
  iam_instance_profile   = aws_iam_instance_profile.instance.name

  associate_public_ip_address = true
  ipv6_address_count          = 1

  user_data = local.user_data_host_b
  # Replacement is DELIBERATE here: Host B holds the Keycloak + auth data on
  # the pgdata EBS volume. A boot-config edit must not silently rebuild it (§8) —
  # change this to true only for an intentional rebuild and drain first (§6.2).
  user_data_replace_on_change = false

  metadata_options {
    http_tokens   = "required" # IMDSv2 only
    http_endpoint = "enabled"
    # No container on Host B pulls from S3, so the host-only default (hop limit 1)
    # is enough; raise to 2 only if a future B container needs instance creds (§4.1).
    http_put_response_hop_limit = 1
    http_protocol_ipv6          = "enabled"
  }

  root_block_device {
    volume_size = var.root_volume_gb
    volume_type = "gp3"
    encrypted   = true
  }

  tags = {
    Name                   = "${var.project}-host-b"
    (var.instance_tag_key) = var.instance_tag_value
    Role                   = "services"
  }
}
