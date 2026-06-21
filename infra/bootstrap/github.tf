# Wire the bootstrap outputs into the GitHub repo so the CI workflows (infra.yml,
# deploy.yml) can consume them: role ARNs + region + ECR registry + state bucket
# as repo VARIABLES, and the Cloudflare API token as the single repo SECRET. This
# is what lets the workflows assume the OIDC roles without any hand-copied config.
# Gated by manage_github_repo so the bootstrap still applies without a GitHub
# token — see the README for the `gh variable set` commands to run by hand then.

locals {
  repo_name     = split("/", var.github_repo)[1]
  ecr_registry  = "${local.account_id}.dkr.ecr.${var.aws_region}.amazonaws.com"
  gh_enabled    = var.manage_github_repo ? 1 : 0
  cf_token_set  = var.manage_github_repo && var.cloudflare_api_token != "" ? 1 : 0

  github_variables = {
    AWS_REGION          = var.aws_region
    AWS_INFRA_ROLE_ARN  = aws_iam_role.infra.arn
    AWS_DEPLOY_ROLE_ARN = aws_iam_role.deploy.arn
    ECR_REGISTRY        = local.ecr_registry
    TFSTATE_BUCKET      = aws_s3_bucket.state.id
  }
}

resource "github_actions_variable" "repo" {
  for_each      = var.manage_github_repo ? local.github_variables : {}
  repository    = local.repo_name
  variable_name = each.key
  value         = each.value
}

resource "github_actions_secret" "cloudflare_api_token" {
  count           = local.cf_token_set
  repository      = local.repo_name
  secret_name     = "CLOUDFLARE_API_TOKEN"
  value           = var.cloudflare_api_token
}
