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

variable "keycloak_domain" {
  description = "Hostname for Keycloak's login/OIDC endpoints, reachable through the tunnel (e.g. auth.example.com). Empty disables the Keycloak tunnel route."
  type        = string
  default     = ""
}

# --- Compute ------------------------------------------------------------------
# Two instances now (two-instance restructure, docs/two-instance-restructure-plan.md):
# Host A = edge (cloudflared/Envoy/SSR/otel), Host B = services + stateful tier
# (3× .NET + Keycloak JVM + Postgres + Redis). Their sizing drivers differ, so the
# type is per-host (Q4 / §4.1).

variable "host_a_instance_type" {
  description = "Host A (edge) instance type. Sheds the .NET services to B, so a small fits (t4g.small = 2 GB)."
  type        = string
  default     = "t4g.small"
}

variable "host_b_instance_type" {
  description = "Host B (services + state) instance type. Runs 3× .NET + Keycloak JVM + Postgres + Redis (t4g.medium = 4 GB; Q4)."
  type        = string
  default     = "t4g.medium"
}

# Static private IPs break the peer-IP cycle (§6.1): each host's user_data needs
# the other's IP, so referencing aws_instance.*.private_ip would be a Terraform
# cycle. Derive both from the default subnet CIDR via cidrhost(offset) — in-range
# by construction, deterministic, no cycle. AWS reserves the first 4 host
# addresses, so keep offsets >= 4.
variable "host_a_ip_offset" {
  description = "Host A private-IP offset within the default subnet CIDR (cidrhost). Must be >= 4 (AWS reserves the first four)."
  type        = number
  default     = 10
}

variable "host_b_ip_offset" {
  description = "Host B private-IP offset within the default subnet CIDR (cidrhost). Must be >= 4 and != host_a_ip_offset."
  type        = number
  default     = 11
}

variable "pgdata_volume_gb" {
  description = "Size of the persistent EBS volume holding Postgres data (Keycloak + auth DBs). Outlives instance replacement (D2)."
  type        = number
  default     = 20
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

# On-host binaries fetched from github by cloud-init (user_data). The instance is
# dual-stack, so github.com (IPv4) is reachable directly — no extras needed.
variable "compose_plugin_version" {
  description = "Docker Compose v2 plugin version installed on the instance (no leading v)."
  type        = string
  default     = "2.39.4"
}

variable "grpc_health_probe_version" {
  description = "grpc_health_probe version installed on the instance (no leading v)."
  type        = string
  default     = "0.4.34"
}

variable "ecr_repositories" {
  description = "ECR repositories to create (must match the compose image names)."
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
