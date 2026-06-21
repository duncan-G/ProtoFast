output "instance_arn" {
  description = "Identity Center instance ARN."
  value       = local.instance_arn
}

output "permission_set_arns" {
  description = "Created permission set ARNs by name."
  value = {
    OrgAdmin      = aws_ssoadmin_permission_set.org_admin.arn
    PlatformAdmin = aws_ssoadmin_permission_set.platform_admin.arn
    Developer     = aws_ssoadmin_permission_set.developer.arn
  }
}

output "group_ids" {
  description = "SSO group ids (created when identity_source=builtin, referenced when external)."
  value       = local.group_ids
}
