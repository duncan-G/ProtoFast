# Publishes the values GitHub Actions needs onto this repo: region, state
# bucket, and assets bucket as variables; OIDC role ARNs, ECR registry, and
# Cloudflare inputs as secrets. `manage_github_repo = false` skips all of this.

locals {
  repo_name    = split("/", var.github_repo)[1]
  ecr_registry = "${local.account_id}.dkr.ecr.${var.aws_region}.amazonaws.com"

  github_variables = {
    AWS_REGION     = var.aws_region
    TFSTATE_BUCKET = aws_s3_bucket.state.id
    ASSETS_BUCKET  = local.assets_bucket_name
  }

  github_secrets = merge(
    {
      AWS_INFRA_ROLE_ARN  = aws_iam_role.infra.arn
      AWS_DEPLOY_ROLE_ARN = aws_iam_role.deploy.arn
      ECR_REGISTRY        = local.ecr_registry
    },
    var.cloudflare_api_token != "" ? { CLOUDFLARE_API_TOKEN = var.cloudflare_api_token } : {},
    var.cloudflare_account_id != "" ? { CLOUDFLARE_ACCOUNT_ID = var.cloudflare_account_id } : {},
    var.cloudflare_zone != "" ? { CLOUDFLARE_ZONE = var.cloudflare_zone } : {},
    var.admin_domain != "" ? { ADMIN_DOMAIN = var.admin_domain } : {},
    var.protofast_domain != "" ? { PROTOFAST_DOMAIN = var.protofast_domain } : {},
    var.keycloak_domain != "" ? { KEYCLOAK_DOMAIN = var.keycloak_domain } : {},
    var.telemetry_domain != "" ? { TELEMETRY_DOMAIN = var.telemetry_domain } : {},
    length(var.telemetry_access_emails) > 0 ? {
      TELEMETRY_ACCESS_EMAILS = jsonencode(var.telemetry_access_emails)
    } : {},
  )
}

resource "github_actions_variable" "repo" {
  for_each      = var.manage_github_repo ? local.github_variables : {}
  repository    = local.repo_name
  variable_name = each.key
  value         = each.value
}

resource "github_actions_secret" "repo" {
  for_each    = var.manage_github_repo ? local.github_secrets : {}
  repository  = local.repo_name
  secret_name = each.key
  value       = each.value
}
