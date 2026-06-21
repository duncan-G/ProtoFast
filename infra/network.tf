# Use the account's default VPC + a public subnet. The instance has NO inbound
# ports (deployment-plan §1) — the security group is egress-only. cloudflared
# dials out to Cloudflare, image pulls and SSM go out via the internet gateway;
# nothing dials in.

data "aws_vpc" "default" {
  default = true
}

data "aws_subnets" "default" {
  filter {
    name   = "vpc-id"
    values = [data.aws_vpc.default.id]
  }
  filter {
    name   = "default-for-az"
    values = ["true"]
  }
}

resource "aws_security_group" "instance" {
  name        = "${var.project}-app"
  description = "ProtoFast app server - egress only, zero ingress."
  vpc_id      = data.aws_vpc.default.id

  # No ingress rules at all: admin access is via SSM Session Manager, public
  # traffic arrives through the outbound Cloudflare tunnel only.

  egress {
    description = "All outbound (Cloudflare tunnel, ECR/SSM/CloudWatch over the internet gateway)."
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = { Name = "${var.project}-app" }
}
