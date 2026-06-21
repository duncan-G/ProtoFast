# protofast-boundary — an IAM permissions boundary: a ceiling on the maximum
# permissions a role can ever have, independent of its attached policies. It caps
# both the human PlatformAdmin SSO permission set AND any role the infra plane
# creates, so neither can escalate privilege via IAM (e.g. grant itself more than
# this boundary allows). The main infra/ config attaches this boundary to every
# role it creates (e.g. the EC2 instance role), and the RequireBoundaryOnCreatedRoles
# statement below forces that.

data "aws_iam_policy_document" "boundary" {
  # The workload's legitimate surface — the ceiling, not a grant.
  statement {
    sid    = "WorkloadServices"
    effect = "Allow"
    actions = [
      "ec2:*",
      "ecr:*",
      "ecr-public:*",
      "s3:*",
      "ssm:*",
      "ssmmessages:*",
      "ec2messages:*",
      "cloudwatch:*",
      "logs:*",
      "kms:*",
      "sts:AssumeRole",
      "sts:GetCallerIdentity",
      "iam:Get*",
      "iam:List*",
      "iam:CreateRole",
      "iam:DeleteRole",
      "iam:TagRole",
      "iam:UntagRole",
      "iam:PassRole",
      "iam:AttachRolePolicy",
      "iam:DetachRolePolicy",
      "iam:PutRolePolicy",
      "iam:DeleteRolePolicy",
      "iam:CreateInstanceProfile",
      "iam:DeleteInstanceProfile",
      "iam:TagInstanceProfile",
      "iam:UntagInstanceProfile",
      "iam:AddRoleToInstanceProfile",
      "iam:RemoveRoleFromInstanceProfile",
      "iam:CreateServiceLinkedRole",
    ]
    resources = ["*"]
  }

  # Block managing the AWS Organization (organizations:*), account-level settings
  # (account:*), and the billing console (aws-portal:*). Anything bounded by this
  # policy — CI roles and the PlatformAdmin people — works only inside this single
  # workload account. Org structure and billing are managed by separate admins in
  # the Organization's management account, so nothing here should ever touch them.
  statement {
    sid       = "DenyOrgAndAccount"
    effect    = "Deny"
    actions   = ["organizations:*", "account:*", "aws-portal:*"]
    resources = ["*"]
  }

  # Cannot mint IAM users or static keys — CI/humans authenticate via OIDC/SSO.
  statement {
    sid    = "DenyIamUsersAndKeys"
    effect = "Deny"
    actions = [
      "iam:CreateUser",
      "iam:CreateAccessKey",
      "iam:CreateLoginProfile",
      "iam:UpdateAccessKey",
    ]
    resources = ["*"]
  }

  # Cannot tamper with the OIDC provider or this boundary itself (escalation paths).
  statement {
    sid    = "DenyEscalationPrimitives"
    effect = "Deny"
    actions = [
      "iam:CreatePolicyVersion",
      "iam:DeletePolicyVersion",
      "iam:SetDefaultPolicyVersion",
      "iam:DeletePolicy",
    ]
    resources = [
      "arn:aws:iam::${data.aws_caller_identity.current.account_id}:policy/${var.project}-boundary",
    ]
  }

  statement {
    sid    = "DenyOidcProviderTamper"
    effect = "Deny"
    actions = [
      "iam:UpdateOpenIDConnectProviderThumbprint",
      "iam:DeleteOpenIDConnectProvider",
      "iam:AddClientIDToOpenIDConnectProvider",
    ]
    resources = ["*"]
  }

  # Any role this plane creates MUST carry this same boundary (no boundary-free
  # children that could escalate).
  statement {
    sid       = "RequireBoundaryOnCreatedRoles"
    effect    = "Deny"
    actions   = ["iam:CreateRole", "iam:PutRolePermissionsBoundary"]
    resources = ["*"]
    condition {
      test     = "StringNotEquals"
      variable = "iam:PermissionsBoundary"
      values   = ["arn:aws:iam::${data.aws_caller_identity.current.account_id}:policy/${var.project}-boundary"]
    }
  }
}

resource "aws_iam_policy" "boundary" {
  name        = "${var.project}-boundary"
  description = "ProtoFast permissions boundary — caps the infra plane; prevents IAM/org escalation."
  policy      = data.aws_iam_policy_document.boundary.json
}
