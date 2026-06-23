locals {
  account_id = data.aws_caller_identity.current.account_id
  oidc_arn   = aws_iam_openid_connect_provider.github_actions.arn

  ecr_repo_arns = [
    for name in var.ecr_repositories :
    "arn:aws:ecr:${var.aws_region}:${local.account_id}:repository/${name}"
  ]

  state_bucket_arn = aws_s3_bucket.state.arn

  # Deterministic assets-bucket name (matches infra/assets.tf), so the deploy
  # role can be granted write without depending on the main stack's outputs.
  assets_bucket_arn = "arn:aws:s3:::${var.project}-assets-${local.account_id}"
}

# ---------------------------------------------------------------------------
# protofast-infra — broad infra lifecycle (EC2, VPC, ECR-create, IAM-with-boundary).
# Assumed by the infra.yml workflow. The trust policy's `sub` condition below
# requires the `infra` GitHub Environment, so only runs that pass that
# Environment's approval gate can assume this role. No route53 actions — DNS is
# managed entirely in Cloudflare, not AWS.
# ---------------------------------------------------------------------------
data "aws_iam_policy_document" "infra_trust" {
  statement {
    effect  = "Allow"
    actions = ["sts:AssumeRoleWithWebIdentity"]
    principals {
      type        = "Federated"
      identifiers = [local.oidc_arn]
    }
    condition {
      test     = "StringEquals"
      variable = "token.actions.githubusercontent.com:aud"
      values   = ["sts.amazonaws.com"]
    }
    condition {
      test     = "StringEquals"
      variable = "token.actions.githubusercontent.com:sub"
      values   = ["repo:${var.github_repo}:environment:infra"]
    }
  }
}

resource "aws_iam_role" "infra" {
  name                 = "${var.project}-infra"
  assume_role_policy   = data.aws_iam_policy_document.infra_trust.json
  permissions_boundary = aws_iam_policy.boundary.arn
}

data "aws_iam_policy_document" "infra" {
  statement {
    sid    = "ComputeAndRegistry"
    effect = "Allow"
    actions = [
      "ec2:*",
      "ecr:*",
      "ssm:*",
      "ssmmessages:*",
      "ec2messages:*",
      "cloudwatch:*",
      "logs:*",
      "kms:*",
    ]
    resources = ["*"]
  }

  statement {
    sid    = "StateBucket"
    effect = "Allow"
    actions = [
      "s3:ListBucket",
      "s3:GetObject",
      "s3:PutObject",
      "s3:DeleteObject",
    ]
    resources = [local.state_bucket_arn, "${local.state_bucket_arn}/*"]
  }

  # Full lifecycle of the assets bucket (infra/assets.tf): create/destroy plus its
  # public-access-block, encryption, and lifecycle sub-resources. Terraform reads
  # many bucket attributes on every refresh and force_destroy empties objects, so
  # s3:* is scoped to this one bucket rather than enumerating a brittle action list.
  statement {
    sid       = "AssetsBucket"
    effect    = "Allow"
    actions   = ["s3:*"]
    resources = [local.assets_bucket_arn, "${local.assets_bucket_arn}/*"]
  }

  # The single application secret (infra/secrets.tf). The infra plane owns only the
  # empty SHELL's lifecycle — create/describe/tag/delete — never its value. Terraform
  # manages no version, and the values are written out-of-band (console /
  # scripts/populate-secrets.sh), so the CI role needs nothing on the value plane.
  # The DenyAppSecretValues statement below makes that boundary explicit and
  # tamper-proof. Scoped to this project's secret namespace; the trailing wildcard
  # also covers the 6-char random suffix AWS appends to the secret ARN.
  statement {
    sid    = "AppSecretShell"
    effect = "Allow"
    actions = [
      "secretsmanager:CreateSecret",
      "secretsmanager:DescribeSecret",
      "secretsmanager:TagResource",
      "secretsmanager:UntagResource",
      "secretsmanager:GetResourcePolicy",
      "secretsmanager:DeleteSecret",
      "secretsmanager:RestoreSecret",
    ]
    resources = ["arn:aws:secretsmanager:${var.aws_region}:${local.account_id}:secret:${var.project}/*"]
  }

  # Hard wall: the GitHub Actions infra role can NEVER read or write secret values.
  # An explicit Deny overrides any Allow (now or added later by mistake), so the CI
  # plane is structurally barred from the value APIs. UpdateSecret is denied too
  # because that API can also set SecretString — it's the one metadata path that
  # could otherwise smuggle a value write. The cost is small and intended: changing
  # the shell's description via Terraform would require an operator apply, but
  # description is static and recovery_window_in_days is only consumed at delete
  # time, so a normal create / no-op apply never calls UpdateSecret.
  statement {
    sid    = "DenyAppSecretValues"
    effect = "Deny"
    actions = [
      "secretsmanager:GetSecretValue",
      "secretsmanager:PutSecretValue",
      "secretsmanager:UpdateSecret",
    ]
    resources = ["arn:aws:secretsmanager:${var.aws_region}:${local.account_id}:secret:${var.project}/*"]
  }

  # IAM for app roles (e.g. the EC2 instance profile). CreateRole is constrained
  # by the boundary (RequireBoundaryOnCreatedRoles) to prevent escalation.
  statement {
    sid    = "ManageAppRoles"
    effect = "Allow"
    actions = [
      "iam:GetRole",
      "iam:GetRolePolicy",
      "iam:ListRolePolicies",
      "iam:ListAttachedRolePolicies",
      "iam:ListInstanceProfilesForRole",
      "iam:CreateRole",
      "iam:DeleteRole",
      "iam:TagRole",
      "iam:UntagRole",
      "iam:PassRole",
      "iam:AttachRolePolicy",
      "iam:DetachRolePolicy",
      "iam:PutRolePolicy",
      "iam:DeleteRolePolicy",
      "iam:PutRolePermissionsBoundary",
      "iam:CreateInstanceProfile",
      "iam:DeleteInstanceProfile",
      "iam:GetInstanceProfile",
      "iam:TagInstanceProfile",
      "iam:UntagInstanceProfile",
      "iam:AddRoleToInstanceProfile",
      "iam:RemoveRoleFromInstanceProfile",
      "iam:CreateServiceLinkedRole",
    ]
    resources = ["*"]
  }
}

resource "aws_iam_role_policy" "infra" {
  name   = "${var.project}-infra"
  role   = aws_iam_role.infra.id
  policy = data.aws_iam_policy_document.infra.json
}

# ---------------------------------------------------------------------------
# protofast-deploy — narrow: ECR push to the configured repos + tag-scoped
# ssm:SendCommand to the prod instance. Assumed by the deploy.yml workflow on
# push/dispatch; the trust policy's `sub` condition below scopes it to the main
# branch (refs/heads/main), so feature branches and PRs cannot deploy.
# ---------------------------------------------------------------------------
data "aws_iam_policy_document" "deploy_trust" {
  statement {
    effect  = "Allow"
    actions = ["sts:AssumeRoleWithWebIdentity"]
    principals {
      type        = "Federated"
      identifiers = [local.oidc_arn]
    }
    condition {
      test     = "StringEquals"
      variable = "token.actions.githubusercontent.com:aud"
      values   = ["sts.amazonaws.com"]
    }
    condition {
      test     = "StringEquals"
      variable = "token.actions.githubusercontent.com:sub"
      values   = ["repo:${var.github_repo}:ref:refs/heads/main"]
    }
  }
}

resource "aws_iam_role" "deploy" {
  name                 = "${var.project}-deploy"
  assume_role_policy   = data.aws_iam_policy_document.deploy_trust.json
  permissions_boundary = aws_iam_policy.boundary.arn
}

data "aws_iam_policy_document" "deploy" {
  statement {
    sid       = "EcrAuth"
    effect    = "Allow"
    actions   = ["ecr:GetAuthorizationToken"]
    resources = ["*"]
  }

  statement {
    sid    = "EcrPush"
    effect = "Allow"
    actions = [
      "ecr:BatchCheckLayerAvailability",
      "ecr:BatchGetImage",
      "ecr:DescribeImages",
      "ecr:GetDownloadUrlForLayer",
      "ecr:InitiateLayerUpload",
      "ecr:UploadLayerPart",
      "ecr:CompleteLayerUpload",
      "ecr:PutImage",
    ]
    resources = local.ecr_repo_arns
  }

  # Deploy is delivered via SSM Run Command, scoped to the prod instance tag plus
  # the managed shell document (no SSH, no inbound ports).
  statement {
    sid       = "SsmSendToTaggedInstance"
    effect    = "Allow"
    actions   = ["ssm:SendCommand"]
    resources = ["arn:aws:ec2:${var.aws_region}:${local.account_id}:instance/*"]
    condition {
      test     = "StringEquals"
      variable = "ssm:resourceTag/${var.instance_tag_key}"
      values   = [var.instance_tag_value]
    }
  }

  statement {
    sid       = "SsmSendDocument"
    effect    = "Allow"
    actions   = ["ssm:SendCommand"]
    resources = ["arn:aws:ssm:${var.aws_region}::document/AWS-RunShellScript"]
  }

  statement {
    sid    = "SsmTrackAndSession"
    effect = "Allow"
    actions = [
      "ssm:GetCommandInvocation",
      "ssm:ListCommands",
      "ssm:ListCommandInvocations",
      "ssm:DescribeInstanceInformation",
      "ssm:StartSession",
    ]
    resources = ["*"]
  }

  statement {
    sid       = "DescribeInstances"
    effect    = "Allow"
    actions   = ["ec2:DescribeInstances"]
    resources = ["*"]
  }

  # Publish client SSR builds to the assets bucket under clients/<name>/<tag>/,
  # and prune superseded tags beyond KEEP_RELEASES. Also publish the deploy
  # artifacts (docker-compose.host-a.yml, docker-compose.host-b.yml, deploy.sh)
  # under deploy/ so a replaced instance can self-bootstrap from cloud-init.
  # Scoped to those two prefixes.
  statement {
    sid       = "AssetsList"
    effect    = "Allow"
    actions   = ["s3:ListBucket"]
    resources = [local.assets_bucket_arn]
    condition {
      test     = "StringLike"
      variable = "s3:prefix"
      values   = ["clients/*", "deploy/*"]
    }
  }
  statement {
    sid    = "AssetsWrite"
    effect = "Allow"
    actions = [
      "s3:GetObject",
      "s3:PutObject",
      "s3:DeleteObject",
    ]
    resources = [
      "${local.assets_bucket_arn}/clients/*",
      "${local.assets_bucket_arn}/deploy/*",
    ]
  }

  statement {
    sid       = "ReadLogs"
    effect    = "Allow"
    actions   = ["logs:GetLogEvents", "logs:FilterLogEvents", "logs:DescribeLogGroups", "logs:DescribeLogStreams"]
    resources = ["*"]
  }

  statement {
    sid       = "ReadState"
    effect    = "Allow"
    actions   = ["s3:GetObject", "s3:ListBucket"]
    resources = [local.state_bucket_arn, "${local.state_bucket_arn}/*"]
  }
}

resource "aws_iam_role_policy" "deploy" {
  name   = "${var.project}-deploy"
  role   = aws_iam_role.deploy.id
  policy = data.aws_iam_policy_document.deploy.json
}
