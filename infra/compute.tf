# The single app server. Cattle: terraform destroy && apply rebuilds it from
# nothing and the next deploy repopulates it. All
# one-time setup is in user_data; everything after boot is driven by deploys.

data "aws_caller_identity" "current" {}

# Latest Amazon Linux 2023 AMI for the instance architecture. AL2023 ships the
# AWS CLI v2 and SSM agent preinstalled (so user_data installs neither) and
# carries a recent amazon-ecr-credential-helper in its repos. Resolved via the
# public SSM parameter AWS keeps current, so there's no hard-coded AMI ID to age.
data "aws_ssm_parameter" "al2023" {
  name = "/aws/service/ami-amazon-linux-latest/al2023-ami-kernel-default-${local.ssm_arch}"
}

locals {
  # Dualstack endpoint (.dkr-ecr.<region>.on.aws), not the IPv4-only
  # .dkr.ecr.<region>.amazonaws.com one: it resolves over both IPv4 and IPv6, so
  # image pulls prefer IPv6 where available. Same underlying registry/repos as the
  # standard endpoint — CI still pushes to the IPv4 endpoint, which is fine.
  ecr_registry = "${data.aws_caller_identity.current.account_id}.dkr-ecr.${var.aws_region}.on.aws"

  # The AL2023 SSM AMI parameter uses arm64/x86_64.
  ssm_arch = var.instance_arch == "arm64" ? "arm64" : "x86_64"

  # Compose release assets use aarch64/x86_64; grpc_health_probe uses arm64/amd64.
  compose_arch = var.instance_arch == "arm64" ? "aarch64" : "x86_64"

  user_data = templatefile("${path.module}/templates/user_data.sh.tftpl", {
    ecr_registry              = local.ecr_registry
    aws_region                = var.aws_region
    admin_domain              = var.admin_domain
    protofast_domain          = var.protofast_domain
    default_client            = "admin"
    tunnel_token              = local.tunnel_token
    compose_plugin_version    = var.compose_plugin_version
    compose_arch              = local.compose_arch
    grpc_health_probe_version = var.grpc_health_probe_version
    grpc_health_probe_arch    = var.instance_arch
  })
}

resource "aws_instance" "app" {
  ami                    = data.aws_ssm_parameter.al2023.value
  instance_type          = var.instance_type
  subnet_id              = aws_default_subnet.default.id
  vpc_security_group_ids = [aws_security_group.instance.id]
  iam_instance_profile   = aws_iam_instance_profile.instance.name

  # Dual-stack egress (no inbound SG rules): a public IPv4 for plain-IPv4
  # destinations (github, etc.) plus a public IPv6, used where supported such as
  # the ECR dualstack endpoint (<acct>.dkr-ecr.<region>.on.aws — see
  # local.ecr_registry). Covers SSM/ECR/tunnel/CloudWatch.
  associate_public_ip_address = true
  ipv6_address_count          = 1

  user_data                   = local.user_data
  user_data_replace_on_change = true

  metadata_options {
    http_tokens   = "required" # IMDSv2 only
    http_endpoint = "enabled"
    # The clients-host entrypoint runs the AWS CLI INSIDE a container and needs
    # instance-profile creds from IMDS. A container sits one network hop behind
    # the host (container -> docker bridge -> host), so the default hop limit of
    # 1 makes IMDS unreachable from any container. Allow the extra hop.
    http_put_response_hop_limit = 2
    http_protocol_ipv6          = "enabled" # IMDS also reachable over IPv6 (dual-stack)
  }

  root_block_device {
    volume_size = var.root_volume_gb
    volume_type = "gp3"
    encrypted   = true
  }

  tags = {
    Name                   = "${var.project}-app"
    (var.instance_tag_key) = var.instance_tag_value
  }
}
