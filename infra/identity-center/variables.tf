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

# --- SES sender (service account, ses-sender.tf) ------------------------------
# These three MUST agree with infra/'s cloudflare_zone, ses_from_local_part, and
# aws_region. They are duplicated rather than shared because this root has its own
# state and no dependency on infra/. If they drift, the send policy's
# ses:FromAddress condition stops matching and Keycloak mail fails with an SES
# authorization error that does not obviously point back here.

variable "ses_sender_zone" {
  description = "Domain the SES sender may send from; must equal infra/'s cloudflare_zone (e.g. example.com). Empty omits the sender user entirely."
  type        = string
  default     = ""
}

variable "ses_from_local_part" {
  description = "Local part of the only From address the sender is allowed to use; must equal infra/'s ses_from_local_part."
  type        = string
  default     = "no-reply"
}

variable "ses_region" {
  description = "Region the SES domain identity lives in (infra/'s aws_region). Empty falls back to var.aws_region, which is the Identity Center instance's region and only coincidentally the same."
  type        = string
  default     = ""
}

