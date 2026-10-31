# Who signs in to ProtoFast's accounts, and what they may do: ProtoStrap's people module
# (its docs/identity.md#people). People sign in through IAM Identity Center with MFA, for
# credentials that last the session; there are no IAM users and no access keys:
#
#   Org-Admins       OrgAdmin          AdministratorAccess                   every account
#   Platform-Admins  PlatformAdmin     read-only view, operator UI, Grafana  every account
#   Developers       Developer         read-only view, development secrets   dev
#   Developers       ServiceTelemetry  Grafana                               prod
#
# Dev is the local run (aspire run). The dev account holds only development secrets, which
# Developers manage: nothing is bootstrapped or deployed in it. In prod, as in any account not
# in developer_environments, Developers open Grafana to see their services' telemetry, and
# nothing else.
#
# An Org Admin applies it once, from the organization's management account (or Identity
# Center's delegated administrator), after enabling Identity Center in the console, and adds
# people to the groups in Identity Center:
#
#   cp terraform.tfvars.example terraform.tfvars    # Then fill it in.
#   export AWS_PROFILE=...
#   terraform init && terraform apply
#
# Its state stays on that machine (gitignored). It replaces the old identity-center root.
terraform {
  required_version = ">= 1.10"
  required_providers {
    aws = { source = "hashicorp/aws", version = "~> 5.60" }
  }
}

variable "region" {
  description = "Identity Center's region."
  type        = string
  default     = "us-east-1"
}
variable "accounts" {
  description = "Each environment's AWS account: { dev = \"111111111111\", prod = \"222222222222\" }."
  type        = map(string)
}

provider "aws" {
  region = var.region
}

module "people" {
  source = "github.com/duncan-G/protostrap//infra/people?ref=v0.1.0"

  accounts               = var.accounts
  developer_environments = ["dev"]
}

output "permission_sets" { value = module.people.permission_sets }
output "groups" { value = module.people.groups }
