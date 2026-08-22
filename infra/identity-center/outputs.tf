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

output "ses_smtp_user" {
  description = "SES sender IAM user (ses-sender.tf). Null when ses_sender_zone is empty. Its access key is minted out of band — infra/README.md section 4.2."
  value       = local.ses_sender_enabled ? aws_iam_user.ses_smtp[0].name : null
}

output "ses_from_address" {
  description = "The only From address the SES sender's policy permits (store as Auth_Smtp__From)."
  value       = local.ses_sender_enabled ? local.ses_from_address : null
}
