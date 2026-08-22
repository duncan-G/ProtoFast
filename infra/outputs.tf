output "host_a_instance_id" {
  description = "Host A (edge) EC2 instance id — deploy target for edge components."
  value       = aws_instance.host_a.id
}

output "host_b_instance_id" {
  description = "Host B (services + state) EC2 instance id — deploy target for service/stateful components."
  value       = aws_instance.host_b.id
}

output "host_a_private_ip" {
  description = "Host A private IP (peer address Host B exports telemetry to)."
  value       = local.host_a_private_ip
}

output "host_b_private_ip" {
  description = "Host B private IP (peer address Envoy on Host A dials for backends)."
  value       = local.host_b_private_ip
}

output "instance_tag" {
  description = "Tag the deploy role scopes ssm:SendCommand to (both hosts carry it)."
  value       = "${var.instance_tag_key}=${var.instance_tag_value}"
}

output "role_tags" {
  description = "Per-host Role tag the deploy workflow resolves the SSM target from."
  value       = { host_a = "Role=edge", host_b = "Role=services" }
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

output "ses_smtp_endpoint" {
  description = "SES SMTP submission host for Keycloak (store as Auth_Smtp__Host). Port 587 + STARTTLS."
  value       = var.enable_ses ? local.ses_smtp_host : null
}

output "ses_from_address" {
  description = "From address Keycloak sends as (store as Auth_Smtp__From)."
  value       = var.enable_ses ? local.ses_from_address : null
}

# NOTE: the sender IAM user is NOT an output here — it is a resource of
# infra/identity-center (ses-sender.tf), the only root whose applier can mint IAM
# users. Read its name from that root's ses_smtp_user output, so there is one
# source of truth rather than a convention duplicated across two states.
