# Prod's platform, in ProtoFast's one account: ProtoStrap's platform module for
# protostrap.yaml. The infra workflow applies it with the Infra role, after the release's
# approval; its inputs come from the prod-infra environment, which infra/bootstrap filled in.
# ProtoStrap's docs/production.md is the checklist before go-live.
terraform {
  required_version = ">= 1.10"

  # The bucket and region are given at init.
  backend "s3" {
    key          = "infra/terraform.tfstate"
    encrypt      = true
    use_lockfile = true
  }

  required_providers {
    aws        = { source = "hashicorp/aws", version = "~> 5.60" }
    cloudflare = { source = "cloudflare/cloudflare", version = "~> 5.0" }
    random     = { source = "hashicorp/random", version = "~> 3.6" }
  }
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

provider "aws" {
  default_tags { tags = { "protostrap:component" = "platform" } }
}
provider "cloudflare" {}

module "protostrap" {
  source = "github.com/duncan-G/protostrap//infra/platform?ref=v0.1.3"

  spec                  = file("${path.module}/../../protostrap.yaml")
  environment           = "prod"
  hosts                 = var.hosts
  email_domains         = var.email_domains
  cloudflare_account_id = var.cloudflare_account_id
  cloudflare_zone       = var.cloudflare_zone
  alert_emails          = var.alert_emails

  # protect_data_vms and force_destroy keep their defaults, true and false: a destroy can't take
  # data-1 or the buckets by accident. vpc_cidr (the default, 10.20.0.0/16) can't change later.
  log_retention_days    = 90
  backup_retention_days = 35
  snapshot_retention    = 14
  # The per-client rate limit is the AppHost's now: AddCloudflareCdn(...).WithRateLimit(...),
  # sized from real traffic before go-live (docs/production.md).
}
