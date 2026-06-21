data "aws_ssoadmin_instances" "this" {}

locals {
  instance_arn      = tolist(data.aws_ssoadmin_instances.this.arns)[0]
  identity_store_id = tolist(data.aws_ssoadmin_instances.this.identity_store_ids)[0]
  use_builtin       = var.identity_source == "builtin"

  # key → SSO group display name. Three groups, three jobs:
  #   org_admins      — identity management + finops
  #   platform_admins — infra + deployments
  #   developers      — read-only prod debugging
  groups = {
    org_admins      = "Org-Admins"
    platform_admins = "Platform-Admins"
    developers      = "Developers"
  }
}

# --- Groups: builtin creates them, external references SCIM-provisioned ones ---
resource "aws_identitystore_group" "g" {
  for_each          = local.use_builtin ? local.groups : {}
  identity_store_id = local.identity_store_id
  display_name      = each.value
  description       = "${var.project} ${each.value}"
}

data "aws_identitystore_group" "g" {
  for_each          = local.use_builtin ? {} : local.groups
  identity_store_id = local.identity_store_id
  alternate_identifier {
    unique_attribute {
      attribute_path  = "DisplayName"
      attribute_value = each.value
    }
  }
}

locals {
  group_ids = local.use_builtin ? {
    for k, r in aws_identitystore_group.g : k => r.group_id
    } : {
    for k, d in data.aws_identitystore_group.g : k => d.group_id
  }
}

# ---------------------------------------------------------------------------
# Permission sets — one per group.
# ---------------------------------------------------------------------------

# OrgAdmin — AdministratorAccess. Covers identity management (Identity Center,
# this config) and finops (billing). Powerful, so kept to a short working session.
resource "aws_ssoadmin_permission_set" "org_admin" {
  name             = "OrgAdmin"
  description      = "Org admin: identity management + finops."
  instance_arn     = local.instance_arn
  session_duration = "PT4H"
}
resource "aws_ssoadmin_managed_policy_attachment" "org_admin" {
  instance_arn       = local.instance_arn
  permission_set_arn = aws_ssoadmin_permission_set.org_admin.arn
  managed_policy_arn = "arn:aws:iam::aws:policy/AdministratorAccess"
}

# PlatformAdmin — custom workload admin + permissions boundary (no escalation).
# Its ecr:* / ssm:* already cover image push and deploy-via-SSM, so this one set
# does both infra and deployments.
resource "aws_ssoadmin_permission_set" "platform_admin" {
  name             = "PlatformAdmin"
  description      = "Owns Terraform infra, networking, app IAM, and deployments (boundary-capped)."
  instance_arn     = local.instance_arn
  session_duration = "PT4H"
}
resource "aws_ssoadmin_permission_set_inline_policy" "platform_admin" {
  instance_arn       = local.instance_arn
  permission_set_arn = aws_ssoadmin_permission_set.platform_admin.arn
  inline_policy      = file("${path.module}/policies/platform-admin.json")
}
resource "aws_ssoadmin_permissions_boundary_attachment" "platform_admin" {
  instance_arn       = local.instance_arn
  permission_set_arn = aws_ssoadmin_permission_set.platform_admin.arn
  permissions_boundary {
    customer_managed_policy_reference {
      name = var.permissions_boundary_name
    }
  }
}

# Developer — ViewOnly + debug shell + log read + image pull. No mutation.
resource "aws_ssoadmin_permission_set" "developer" {
  name             = "Developer"
  description      = "Debug prod: read logs, SSM shell, pull images."
  instance_arn     = local.instance_arn
  session_duration = "PT8H"
}
resource "aws_ssoadmin_managed_policy_attachment" "developer_viewonly" {
  instance_arn       = local.instance_arn
  permission_set_arn = aws_ssoadmin_permission_set.developer.arn
  managed_policy_arn = "arn:aws:iam::aws:policy/job-function/ViewOnlyAccess"
}
resource "aws_ssoadmin_permission_set_inline_policy" "developer" {
  instance_arn       = local.instance_arn
  permission_set_arn = aws_ssoadmin_permission_set.developer.arn
  inline_policy      = file("${path.module}/policies/developer.json")
}

# ---------------------------------------------------------------------------
# Account assignments: group → permission set → account
# ---------------------------------------------------------------------------
locals {
  # key → { group, permission_set_arn }
  assignments = {
    org_admins = {
      group  = "org_admins"
      ps_arn = aws_ssoadmin_permission_set.org_admin.arn
    }
    platform_admins = {
      group  = "platform_admins"
      ps_arn = aws_ssoadmin_permission_set.platform_admin.arn
    }
    developers = {
      group  = "developers"
      ps_arn = aws_ssoadmin_permission_set.developer.arn
    }
  }
}

resource "aws_ssoadmin_account_assignment" "this" {
  for_each = local.assignments

  instance_arn       = local.instance_arn
  permission_set_arn = each.value.ps_arn
  principal_id       = local.group_ids[each.value.group]
  principal_type     = "GROUP"
  target_id          = var.account_id
  target_type        = "AWS_ACCOUNT"
}
