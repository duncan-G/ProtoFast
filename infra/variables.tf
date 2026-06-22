variable "project" {
  description = "Project name; prefixes resource names and tags."
  type        = string
  default     = "protofast"
}

variable "aws_region" {
  description = "AWS region for the workload."
  type        = string
  default     = "us-east-1"
}

# --- Cloudflare / DNS ---------------------------------------------------------

variable "cloudflare_account_id" {
  description = "Cloudflare account ID that owns the zone and the tunnel."
  type        = string
}

variable "cloudflare_zone" {
  description = "Apex domain, already registered + authoritative in Cloudflare (e.g. example.com)."
  type        = string
}

variable "admin_domain" {
  description = "Hostname for the admin client (e.g. admin.example.com)."
  type        = string
}

variable "protofast_domain" {
  description = "Hostname for the protofast client (e.g. protofast.example.com)."
  type        = string
}

variable "telemetry_domain" {
  description = "Hostname for the Aspire Dashboard, gated by Cloudflare Access (e.g. telemetry.example.com)."
  type        = string
  default     = ""
}

variable "telemetry_access_emails" {
  description = "Emails allowed through the Cloudflare Access policy for the dashboard. Empty disables the telemetry hostname + Access app."
  type        = list(string)
  default     = []
}

# --- Compute ------------------------------------------------------------------

variable "instance_type" {
  description = "EC2 instance type (ARM, cheap; images are multi-arch-able)."
  type        = string
  default     = "t4g.medium"
}

variable "instance_arch" {
  description = "CPU architecture of the AMI / instance (arm64 for t4g, amd64 for t3)."
  type        = string
  default     = "arm64"
}

variable "root_volume_gb" {
  description = "Root EBS volume size in GB (holds Docker images + the last N rollback tag sets)."
  type        = number
  default     = 30
}

variable "instance_tag_key" {
  description = "Tag key the deploy role's ssm:SendCommand is scoped to. Must match bootstrap."
  type        = string
  default     = "protofast:role"
}

variable "instance_tag_value" {
  description = "Tag value identifying the prod instance. Must match bootstrap."
  type        = string
  default     = "app-server"
}

# NOTE: the Docker Compose plugin and grpc_health_probe versions live in the CI
# deploy workflow (.github/workflows/deploy.yml), which builds them into the
# protofast-tools image. They are no longer fetched on the instance, so there's
# no terraform var for them here.

variable "ecr_repositories" {
  description = "ECR repositories to create (must match the compose image names)."
  type        = list(string)
  default = [
    "protofast-envoy",
    "protofast-clients-host",
    "protofast-auth",
    "protofast-payments",
    "protofast-api",
    "protofast-otel-collector",
    # Not a service image: a tiny bundle of the on-host binaries (docker compose
    # plugin + grpc_health_probe). The instance is IPv6-only and github.com is
    # IPv4-only, so these are delivered over the ECR dualstack endpoint instead of
    # curled from github. CI builds it; deploy.sh extracts from it (deploy.sh).
    "protofast-tools",
  ]
}
