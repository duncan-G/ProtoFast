# Default VPC + a default subnet, IPv6-enabled. The instance has NO inbound ports
# (deployment-plan §1) and runs IPv6-only for internet egress: no public IPv4
# (which AWS now bills per-hour), just a public IPv6 address whose only route out
# is the egress-only internet gateway. cloudflared dials out to Cloudflare; image
# pulls, SSM and CloudWatch go out over IPv6 (dualstack endpoints: ECR via its
# .dkr-ecr.<region>.on.aws endpoint, the CLI/SDKs via AWS_USE_DUALSTACK_ENDPOINT,
# the SSM agent via its own UseDualStackEndpoint config — see user_data).
# Nothing dials in.
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

  # Public IPv6 on launch; IPv4 stays private (free) and has no internet route.
  ipv6_cidr_block                 = cidrsubnet(aws_default_vpc.default.ipv6_cidr_block, 8, 0)
  assign_ipv6_address_on_creation = true
  map_public_ip_on_launch         = false
}

# IPv6 outbound-only gateway (free, unlike a NAT gateway). The matching ::/0 route
# is what makes egress work; without it the instance can reach nothing.
resource "aws_egress_only_internet_gateway" "this" {
  vpc_id = aws_default_vpc.default.id
}

# Default subnets use the VPC main route table. Add the IPv6 default route there;
# the existing IPv4 0.0.0.0/0 -> IGW route is left untouched (unused without a
# public IPv4).
resource "aws_route" "ipv6_egress" {
  route_table_id              = aws_default_vpc.default.main_route_table_id
  destination_ipv6_cidr_block = "::/0"
  egress_only_gateway_id      = aws_egress_only_internet_gateway.this.id
}

resource "aws_security_group" "instance" {
  name        = "${var.project}-app"
  description = "ProtoFast app server - egress only, zero ingress."
  vpc_id      = aws_default_vpc.default.id

  # No ingress rules at all: admin access is via SSM Session Manager, public
  # traffic arrives through the outbound Cloudflare tunnel only.

  egress {
    description      = "All outbound IPv6 (Cloudflare tunnel, ECR/SSM/CloudWatch over the egress-only gateway)."
    from_port        = 0
    to_port          = 0
    protocol         = "-1"
    ipv6_cidr_blocks = ["::/0"]
  }

  tags = { Name = "${var.project}-app" }
}
