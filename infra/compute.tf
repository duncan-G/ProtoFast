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
  # .dkr.ecr.<region>.amazonaws.com one: the instance is IPv6-only, so image
  # pulls must resolve over IPv6. Same underlying registry/repos as the standard
  # endpoint — CI still pushes to the IPv4 endpoint, which is fine.
  ecr_registry = "${data.aws_caller_identity.current.account_id}.dkr-ecr.${var.aws_region}.on.aws"

  # The AL2023 SSM AMI parameter uses arm64/x86_64. The Compose plugin and
  # grpc_health_probe are no longer fetched on the box (the deploy pipeline ships
  # them via ECR — see deploy/deploy.sh), so no compose/probe arch mapping here.
  ssm_arch = var.instance_arch == "arm64" ? "arm64" : "x86_64"

  user_data = templatefile("${path.module}/templates/user_data.sh.tftpl", {
    ecr_registry     = local.ecr_registry
    aws_region       = var.aws_region
    admin_domain     = var.admin_domain
    protofast_domain = var.protofast_domain
    default_client   = "admin"
    tunnel_token     = local.tunnel_token
  })
}

resource "aws_instance" "app" {
  ami                    = data.aws_ssm_parameter.al2023.value
  instance_type          = var.instance_type
  subnet_id              = aws_default_subnet.default.id
  vpc_security_group_ids = [aws_security_group.instance.id]
  iam_instance_profile   = aws_iam_instance_profile.instance.name

  # IPv6-only egress: no billable public IPv4. One public IPv6 whose only route
  # out is the egress-only gateway covers SSM/ECR/tunnel/CloudWatch. ECR is
  # reached over its dualstack endpoint (<acct>.dkr-ecr.<region>.on.aws) — see
  # local.ecr_registry.
  associate_public_ip_address = false
  ipv6_address_count          = 1

  user_data                   = local.user_data
  user_data_replace_on_change = true

  metadata_options {
    http_tokens        = "required" # IMDSv2 only
    http_endpoint      = "enabled"
    http_protocol_ipv6 = "enabled" # IMDS reachable on the IPv6-only box
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
