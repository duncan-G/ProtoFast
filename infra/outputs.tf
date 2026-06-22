output "instance_id" {
  description = "EC2 instance id (deploy target for ssm:SendCommand)."
  value       = aws_instance.app.id
}

output "instance_tag" {
  description = "Tag the deploy role scopes ssm:SendCommand to."
  value       = "${var.instance_tag_key}=${var.instance_tag_value}"
}

output "tunnel_id" {
  description = "Cloudflare tunnel id."
  value       = cloudflare_zero_trust_tunnel_cloudflared.this.id
}

output "ecr_registry" {
  description = "ECR registry host."
  value       = local.ecr_registry
}

output "assets_bucket" {
  description = "S3 bucket holding per-client SSR builds (clients/<name>/<tag>/)."
  value       = aws_s3_bucket.assets.id
}

output "hostnames" {
  description = "Public hostnames served through the tunnel."
  value       = values(local.tunnel_hostnames)
}
