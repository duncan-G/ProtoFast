# The SES SMTP sender for Keycloak's transactional mail (infra/ses.tf owns the
# sending identity itself). The credential is a long-lived IAM access key, which
# needs an IAM user — exactly what the permissions boundary bans (see
# infra/bootstrap/boundary.tf "DenyIamUsersAndKeys"). That deny caps the CI/infra
# plane AND the PlatformAdmin people, so the user cannot live in infra/. It lives
# here instead: OrgAdmin applies this root and carries AdministratorAccess with no
# boundary attachment, so it is the one identity in the account that can mint it.
#
# Only the user and its send policy are managed here. The ACCESS KEY is
# deliberately NOT: aws_iam_access_key writes the secret into state, and no secret
# value may ever land in Terraform state (infra/secrets.tf). Minting the key,
# deriving the SMTP password, and storing both in the app secret is out of band —
# see infra/README.md section 4.2.
#
# Ordering: none. IAM does not validate that the resource ARN in a policy refers
# to an existing resource, so this applies cleanly before infra/ses.tf has ever
# created the domain identity. Sending, of course, only works once that identity
# is verified.

locals {
  ses_sender_enabled = var.ses_sender_zone != ""
  ses_sender_region  = var.ses_region != "" ? var.ses_region : var.aws_region
  ses_from_address   = "${var.ses_from_local_part}@${var.ses_sender_zone}"

  boundary_arn     = "arn:aws:iam::${data.aws_caller_identity.current.account_id}:policy/${var.permissions_boundary_name}"
  ses_identity_arn = "arn:aws:ses:${local.ses_sender_region}:${data.aws_caller_identity.current.account_id}:identity/${var.ses_sender_zone}"
}

# The boundary applies to the user too: even holding this key, nothing outside the
# boundary's ceiling is reachable.
resource "aws_iam_user" "ses_smtp" {
  count                = local.ses_sender_enabled ? 1 : 0
  name                 = "${var.project}-ses-smtp"
  permissions_boundary = local.boundary_arn
}

# Send only, and only as the one From address — a leaked key cannot spoof other
# addresses in the domain.
resource "aws_iam_user_policy" "ses_smtp" {
  count = local.ses_sender_enabled ? 1 : 0
  name  = "ses-send"
  user  = aws_iam_user.ses_smtp[0].name

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect   = "Allow"
      Action   = ["ses:SendEmail", "ses:SendRawEmail"]
      Resource = local.ses_identity_arn
      Condition = {
        StringEquals = { "ses:FromAddress" = local.ses_from_address }
      }
    }]
  })
}
