# Bootstrap — applied ONCE, LOCALLY, with admin credentials; never in CI.
# It solves the chicken-and-egg: the main infra/ config (one directory up) runs
# in GitHub Actions, which needs an S3 state backend and GitHub-OIDC roles to
# authenticate — but nothing has created those yet. This config creates them
# using whatever admin credentials the operator has on their laptop.
#
# State is LOCAL and gitignored (see repo .gitignore). All resources here are
# trivially re-importable if that local state is ever lost.

terraform {
  required_version = ">= 1.10"

  # NOTE: deliberately a LOCAL backend (default). Do not add an S3 backend here —
  # this config is what creates that bucket.

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.60"
    }
    github = {
      source  = "integrations/github"
      version = "~> 6.2"
    }
    tls = {
      source  = "hashicorp/tls"
      version = "~> 4.0"
    }
  }
}

provider "aws" {
  region = var.aws_region

  default_tags {
    tags = {
      Project   = var.project
      ManagedBy = "terraform"
      Component = "bootstrap"
    }
  }
}

# Authenticated via the GITHUB_TOKEN env var or `gh auth token`. Only used when
# var.manage_github_repo is true (writes the role ARNs + Cloudflare token to the
# repo). Set owner from the derived github_repo slug.
provider "github" {
  owner = split("/", var.github_repo)[0]
}
