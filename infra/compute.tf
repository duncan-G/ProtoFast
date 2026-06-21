# The single app server. Cattle: terraform destroy && apply rebuilds it from
# nothing and the next deploy repopulates it. All
# one-time setup is in user_data; everything after boot is driven by deploys.

data "aws_caller_identity" "current" {}

# Canonical Ubuntu 24.04 LTS (Noble), matched to the instance architecture.
data "aws_ami" "ubuntu" {
  most_recent = true
  owners      = ["099720109477"] # Canonical

  filter {
    name   = "name"
    values = ["ubuntu/images/hvm-ssd-gp3/ubuntu-noble-24.04-${var.instance_arch}-server-*"]
  }
  filter {
    name   = "virtualization-type"
    values = ["hvm"]
  }
}

locals {
  ecr_registry = "${data.aws_caller_identity.current.account_id}.dkr.ecr.${var.aws_region}.amazonaws.com"

  user_data = templatefile("${path.module}/templates/user_data.sh.tftpl", {
    ecr_registry              = local.ecr_registry
    aws_region                = var.aws_region
    admin_domain              = var.admin_domain
    protofast_domain          = var.protofast_domain
    default_client            = "admin"
    tunnel_token              = local.tunnel_token
    grpc_health_probe_version = var.grpc_health_probe_version
    grpc_health_probe_arch    = var.instance_arch
  })
}

resource "aws_instance" "app" {
  ami                    = data.aws_ami.ubuntu.id
  instance_type          = var.instance_type
  subnet_id              = aws_default_subnet.default.id
  vpc_security_group_ids = [aws_security_group.instance.id]
  iam_instance_profile   = aws_iam_instance_profile.instance.name

  # IPv6-only egress: no billable public IPv4. One public IPv6 whose only route
  # out is the egress-only gateway covers SSM/ECR/tunnel/CloudWatch.
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
