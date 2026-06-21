variable "project" {
  description = "Project name; prefixes permission-set names."
  type        = string
  default     = "protofast"
}

variable "aws_region" {
  description = "Region for the Identity Center API calls (the IC instance's region)."
  type        = string
  default     = "us-east-1"
}

variable "account_id" {
  description = "AWS account id that hosts Identity Center, billing, and the workload (single-account setup)."
  type        = string
}

variable "identity_source" {
  description = <<-EOT
    Where the three SSO groups come from:
      "builtin"  — Terraform CREATES the groups in Identity Center's own directory.
      "external" — groups are synced from an IdP via SCIM; Terraform only REFERENCES
                   them by display name (create them in the IdP first).
  EOT
  type        = string
  default     = "builtin"

  validation {
    condition     = contains(["builtin", "external"], var.identity_source)
    error_message = "identity_source must be 'builtin' or 'external'."
  }
}

variable "permissions_boundary_name" {
  description = "Customer-managed permissions boundary (created by infra/bootstrap) attached to the PlatformAdmin set."
  type        = string
  default     = "protofast-boundary"
}
