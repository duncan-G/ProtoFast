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
  description = <<-EOT
    owner/repo for the GitHub OIDC trust policies. DO NOT hard-code — derive it
    from git at apply time so a fork or rename needs no edit:
      terraform apply -var "github_repo=$(gh repo view --json nameWithOwner -q .nameWithOwner)"
  EOT
  type        = string

  validation {
    condition     = can(regex("^[^/]+/[^/]+$", var.github_repo))
    error_message = "github_repo must be in 'owner/repo' form."
  }
}

variable "state_bucket_name" {
  description = "Globally-unique S3 bucket name for the main infra/ Terraform state."
  type        = string
  default     = "protofast-tfstate"
}

variable "ecr_repositories" {
  description = "ECR repository names the deploy role may push to."
  type        = list(string)
  default = [
    "protofast-envoy",
    "protofast-clients-host",
    "protofast-auth",
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
  description = "Whether Terraform writes the role ARNs (repo variables) and Cloudflare token (repo secret) to GitHub. Needs GITHUB_TOKEN/gh auth."
  type        = bool
  default     = true
}

variable "cloudflare_api_token" {
  description = "Cloudflare API token (zone + tunnel scoped) stored as the CLOUDFLARE_API_TOKEN repo secret. Leave empty to skip and set it by hand."
  type        = string
  default     = ""
  sensitive   = true
}
