variable "project" {
  description = "Project name; prefixes resource names."
  type        = string
  default     = "protofast"
}

variable "aws_region" {
  description = "AWS region for the state bucket and workload."
  type        = string
  default     = "us-east-1"
}

variable "github_repo" {
  description = "owner/repo for the GitHub OIDC trust policies and the name-suffix hash. Set in terraform.tfvars."
  type        = string

  validation {
    condition     = can(regex("^[^/]+/[^/]+$", var.github_repo))
    error_message = "github_repo must be in 'owner/repo' form."
  }
}

variable "ecr_repositories" {
  description = "ECR repository names the deploy role may push to."
  type        = list(string)
  default = [
    "protofast-envoy",
    "protofast-clients-host",
    "protofast-auth",
    "protofast-auth-migrations",
    "protofast-payments",
    "protofast-api",
    "protofast-otel-collector",
  ]
}

variable "instance_tag_key" {
  description = "Tag key used to scope the deploy role's ssm:SendCommand to the prod instance."
  type        = string
  default     = "protofast:role"
}

variable "instance_tag_value" {
  description = "Tag value identifying the prod instance for ssm:SendCommand scoping."
  type        = string
  default     = "app-server"
}

variable "manage_github_repo" {
  description = "Whether Terraform writes repo variables and secrets (OIDC role ARNs, ECR, Cloudflare). Needs GITHUB_TOKEN/gh auth."
  type        = bool
  default     = true
}

variable "cloudflare_api_token" {
  description = "Cloudflare API token (zone + tunnel scoped) stored as the CLOUDFLARE_API_TOKEN repo secret. Leave empty to skip and set it by hand."
  type        = string
  default     = ""
}

variable "cloudflare_account_id" {
  description = "Cloudflare account ID; stored as the CLOUDFLARE_ACCOUNT_ID repo secret for infra.yml."
  type        = string
  default     = ""
}

variable "cloudflare_zone" {
  description = "Apex domain in Cloudflare; stored as the CLOUDFLARE_ZONE repo secret for infra.yml."
  type        = string
  default     = ""
}

variable "admin_domain" {
  description = "Admin hostname; stored as the ADMIN_DOMAIN repo secret for infra.yml."
  type        = string
  default     = ""
}

variable "protofast_domain" {
  description = "ProtoFast hostname; stored as the PROTOFAST_DOMAIN repo secret for infra.yml."
  type        = string
  default     = ""
}

variable "keycloak_domain" {
  description = "Keycloak hostname (e.g. auth.example.com); stored as the KEYCLOAK_DOMAIN repo secret for infra.yml."
  type        = string
  default     = ""
}

variable "telemetry_domain" {
  description = "Optional Aspire Dashboard hostname; stored as TELEMETRY_DOMAIN when non-empty."
  type        = string
  default     = ""
}

variable "telemetry_access_emails" {
  description = "Optional emails for the telemetry Access policy; stored as TELEMETRY_ACCESS_EMAILS JSON when non-empty."
  type        = list(string)
  default     = []
}
