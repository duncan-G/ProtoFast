# Who signs in to ProtoFast's account, and what they may do: ProtoStrap's people module
# (its docs/identity.md#people). People sign in through IAM Identity Center with MFA, for
# credentials that last the session; there are no IAM users and no access keys:
#
#   Org-Admins       OrgAdmin       AdministratorAccess                     every account
#   Platform-Admins  PlatformAdmin  read-only view, operator UI, Grafana    every account
#   Developers       Developer      read-only view, protofast/dev, Grafana  every account
#
# ProtoFast has one account. It holds Identity Center (it is its organization's management
# account), prod, and dev's secret, protofast/dev, beside prod's. Dev is the local run
# (aspire run): nothing is bootstrapped or deployed for it. Developers manage protofast/dev
# and open Grafana to see their services' telemetry in prod; they can't read protofast/prod
# or open the operator UI.
#
# An Org Admin applies it once, after bootstrap, from their own machine with that account's
# admin credentials, after enabling Identity Center in the console, and adds people to the
# groups in Identity Center:
#
#   cp terraform.tfvars.example terraform.tfvars    # Then fill it in.
#   export AWS_PROFILE=...
#   terraform init -backend-config="bucket=$(terraform -chdir=../bootstrap output -raw state_bucket)" -backend-config=region=us-west-2
#   terraform apply
#
# Its state is people/ in bootstrap's state bucket, which the Infra role can't read or write.
# It replaces the old identity-center root.
terraform {
  required_version = ">= 1.10"

  # The bucket and region are given at init.
  backend "s3" {
    key          = "people/terraform.tfstate"
    encrypt      = true
    use_lockfile = true
  }

  required_providers {
    aws = { source = "hashicorp/aws", version = "~> 5.60" }
  }
}

variable "region" {
  description = "Identity Center's region."
  type        = string
  default     = "us-west-2"
}
variable "accounts" {
  description = "ProtoFast's one AWS account, by deployed environment: { prod = \"111111111111\" }. Dev, the local run, has no account: its secret is in this one."
  type        = map(string)
}

provider "aws" {
  region = var.region
}

module "people" {
  source = "github.com/duncan-G/protostrap//infra/people?ref=v0.1.12"

  accounts = var.accounts
}

output "permission_sets" { value = module.people.permission_sets }
output "groups" { value = module.people.groups }
