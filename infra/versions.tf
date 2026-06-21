# Main workload config (deployment-plan §3.1). Run by the infra.yml workflow
# (assuming protofast-infra via OIDC) or by a PlatformAdmin by hand. State lives
# in the S3 bucket created by infra/bootstrap (see backend.tf).

terraform {
  required_version = ">= 1.10"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.60"
    }
    cloudflare = {
      source  = "cloudflare/cloudflare"
      version = "~> 5.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
  }
}

provider "aws" {
  region = var.aws_region

  default_tags {
    tags = {
      Project   = var.project
      ManagedBy = "terraform"
    }
  }
}

# Token comes from CLOUDFLARE_API_TOKEN (zone + tunnel scoped) — set in the
# workflow env from the repo secret.
provider "cloudflare" {}
