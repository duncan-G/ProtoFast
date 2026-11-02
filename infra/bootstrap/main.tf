# Bootstraps ProtoFast's one account, where prod runs: the state bucket, GitHub's OIDC
# provider, the Infra and Deploy roles and their boundaries, and the prod and prod-infra GitHub
# environments. An Org Admin applies it once, from their own machine, with the account's admin
# credentials:
#
#   cp terraform.tfvars.example terraform.tfvars    # Then fill it in.
#   export AWS_PROFILE=... GITHUB_TOKEN=$(gh auth token)
#   terraform init && terraform apply
#
# Its state stays on that machine (gitignored); everything here can be imported again.
terraform {
  required_version = ">= 1.10"
  required_providers {
    aws    = { source = "hashicorp/aws", version = "~> 5.60" }
    github = { source = "integrations/github", version = "~> 6.2" }
    tls    = { source = "hashicorp/tls", version = "~> 4.0" }
  }
}

variable "region" {
  type    = string
  default = "us-west-2"
}
variable "environment" {
  type    = string
  default = "prod"
}
variable "github_repository" {
  description = "owner/repo: gh repo view --json nameWithOwner -q .nameWithOwner"
  type        = string
}
variable "cloudflare_account_id" { type = string }
variable "cloudflare_zone" { type = string }
variable "hosts" {
  description = "The hostname in this account of each of the spec's hosts: protofast, theplot, admin and auth."
  type        = map(string)
}
variable "email_domains" {
  description = "The domains this account sends mail from: the protofast host's, which auth and Keycloak send from."
  type        = list(string)
}
variable "alert_emails" {
  type    = list(string)
  default = []
}
variable "create_github_oidc_provider" {
  description = "False if the account already has GitHub's OIDC provider."
  type        = bool
  default     = true
}

provider "aws" {
  region = var.region
  default_tags { tags = { "protostrap:component" = "bootstrap" } }
}
provider "github" {
  owner = split("/", var.github_repository)[0]
}

module "bootstrap" {
  # The same version as the spec's protostrap: and the workflows' @v0.1.8.
  source = "github.com/duncan-G/protostrap//infra/bootstrap?ref=v0.1.8"

  app                         = "protofast" # the spec's app; the secret is protofast/<environment>
  environment                 = var.environment
  github_repository           = var.github_repository
  create_github_oidc_provider = var.create_github_oidc_provider

  # The platform root's inputs in this account, as TF_VAR_* variables of prod-infra.
  infra_variables = {
    cloudflare_account_id = var.cloudflare_account_id
    cloudflare_zone       = var.cloudflare_zone
    hosts                 = jsonencode(var.hosts)
    email_domains         = jsonencode(var.email_domains)
    alert_emails          = jsonencode(var.alert_emails)
  }
}

output "state_bucket" { value = module.bootstrap.state_bucket }
output "infra_role_arn" { value = module.bootstrap.infra_role_arn }
output "deploy_role_arn" { value = module.bootstrap.deploy_role_arn }
