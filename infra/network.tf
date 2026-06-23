# Default VPC + a default subnet, dual-stack (IPv4 + IPv6). The instance has NO
# inbound ports (deployment-plan §1) — egress only. It gets both a public IPv4
# (so plain-IPv4 endpoints like github.com work without any extras) and a public
# IPv6 (used where supported, e.g. the ECR dualstack endpoint). cloudflared dials
# out to Cloudflare; image pulls, SSM and CloudWatch go out over the internet
# gateway (IPv4) or the egress-only gateway (IPv6). Nothing dials in.
#
# These are `aws_default_*` resources, not data sources: Terraform adopts the
# account's existing default VPC/subnet so it can switch IPv6 on. Destroy is a
# no-op for them — the real default VPC is never deleted.

resource "aws_default_vpc" "default" {
  # Amazon-provided /56; carve a /64 for the subnet below.
  assign_generated_ipv6_cidr_block = true
}

data "aws_availability_zones" "available" {
  state = "available"
}

resource "aws_default_subnet" "default" {
  availability_zone = data.aws_availability_zones.available.names[0]

  # Public IPv4 (the instance also sets associate_public_ip_address) plus a
  # public IPv6 on launch — dual-stack.
  ipv6_cidr_block                 = cidrsubnet(aws_default_vpc.default.ipv6_cidr_block, 8, 0)
  assign_ipv6_address_on_creation = true
  map_public_ip_on_launch         = true
}

# IPv6 outbound-only gateway (free, unlike a NAT gateway). IPv4 egress uses the
# default VPC's existing internet gateway + 0.0.0.0/0 route.
resource "aws_egress_only_internet_gateway" "this" {
  vpc_id = aws_default_vpc.default.id
}

# Default subnets use the VPC main route table, which already has the IPv4
# 0.0.0.0/0 -> internet gateway route. Add the matching IPv6 default route so
# IPv6 egress works; without it the instance can reach nothing over IPv6.
resource "aws_route" "ipv6_egress" {
  route_table_id              = aws_default_vpc.default.main_route_table_id
  destination_ipv6_cidr_block = "::/0"
  egress_only_gateway_id      = aws_egress_only_internet_gateway.this.id
}

resource "aws_security_group" "instance" {
  name        = "${var.project}-app"
  description = "ProtoFast app server - egress only, zero ingress."
  vpc_id      = aws_default_vpc.default.id

  # No INTERNET-facing ingress: admin access is via SSM Session Manager, public
  # traffic arrives through the outbound Cloudflare tunnel only. The two cross-host
  # rules below use self = true, which admits ONLY sibling instances in this same
  # security group over private IPs — no NAT, no overlay, still zero public ingress
  # (two-instance restructure §4.3).
  ingress {
    description = "Host A Envoy to Host B services and Keycloak (private, self only)"
    from_port   = 8080
    to_port     = 8083
    protocol    = "tcp"
    self        = true
  }

  ingress {
    description = "Host B services to Host A otel-collector (private, self only)"
    from_port   = 4317
    to_port     = 4318
    protocol    = "tcp"
    self        = true
  }

  egress {
    description = "All outbound IPv4 (Cloudflare tunnel, ECR/SSM/CloudWatch, github, etc.)."
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  egress {
    description      = "All outbound IPv6 (ECR dualstack endpoint and other IPv6 destinations)."
    from_port        = 0
    to_port          = 0
    protocol         = "-1"
    ipv6_cidr_blocks = ["::/0"]
  }

  tags = { Name = "${var.project}-app" }
}
