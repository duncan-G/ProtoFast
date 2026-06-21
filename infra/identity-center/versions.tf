# AWS Identity Center & access model. Driven by a HUMAN (never the CI OIDC
# roles), from its own root config with its own state — an identity change can
# never be entangled with an app-infra plan.
#
# First apply: by a human holding OrgAdmin/AdministratorAccess (creating the
# first permission set needs pre-existing SSO admin rights). Subsequent applies
# run under the same OrgAdmin set.

terraform {
  required_version = ">= 1.10"

  backend "s3" {
    key          = "identity-center/terraform.tfstate"
    encrypt      = true
    use_lockfile = true
    # bucket/region passed at init: terraform init -backend-config="bucket=..." -backend-config="region=..."
  }

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.60"
    }
  }
}

provider "aws" {
  region = var.aws_region

  default_tags {
    tags = {
      Project   = var.project
      ManagedBy = "terraform"
      Component = "identity-center"
    }
  }
}
