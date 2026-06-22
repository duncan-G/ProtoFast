output "state_bucket" {
  description = "S3 bucket for the main infra/ config's Terraform state. Put this in infra/backend.tf."
  value       = aws_s3_bucket.state.id
}

output "aws_region" {
  description = "Region for the state bucket and workload."
  value       = var.aws_region
}

output "ecr_registry" {
  description = "ECR registry host for image pushes."
  value       = local.ecr_registry
}

output "assets_bucket" {
  description = "S3 bucket for per-client SSR builds (set as repo variable ASSETS_BUCKET; created by infra/assets.tf)."
  value       = "${var.project}-assets-${local.account_id}"
}

output "infra_role_arn" {
  description = "ARN the infra workflow assumes (set as repo variable AWS_INFRA_ROLE_ARN)."
  value       = aws_iam_role.infra.arn
}

output "deploy_role_arn" {
  description = "ARN the deploy workflow assumes (set as repo variable AWS_DEPLOY_ROLE_ARN)."
  value       = aws_iam_role.deploy.arn
}

output "permissions_boundary_arn" {
  description = "Permissions boundary to attach to infra-created roles and the PlatformAdmin set."
  value       = aws_iam_policy.boundary.arn
}

output "oidc_provider_arn" {
  description = "GitHub Actions OIDC provider ARN."
  value       = aws_iam_openid_connect_provider.github_actions.arn
}
