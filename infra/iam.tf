# Instance profile: SSM core (Session Manager + Run Command, no SSH) and ECR pull
# (so `docker compose pull` needs zero credentials on the box). The permissions
# boundary from bootstrap is attached to satisfy the no-escalation rule.

# The boundary ARN is deterministic (account id + project name), so construct it
# directly rather than looking it up by name. A name-based lookup would force
# iam:ListPolicies, a permission the infra role deliberately does not hold.
locals {
  boundary_arn = "arn:aws:iam::${data.aws_caller_identity.current.account_id}:policy/${var.project}-boundary"
}

data "aws_iam_policy_document" "instance_trust" {
  statement {
    effect  = "Allow"
    actions = ["sts:AssumeRole"]
    principals {
      type        = "Service"
      identifiers = ["ec2.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "instance" {
  name                 = "${var.project}-instance"
  assume_role_policy   = data.aws_iam_policy_document.instance_trust.json
  permissions_boundary = local.boundary_arn
}

resource "aws_iam_role_policy_attachment" "ssm_core" {
  role       = aws_iam_role.instance.name
  policy_arn = "arn:aws:iam::aws:policy/AmazonSSMManagedInstanceCore"
}

data "aws_iam_policy_document" "instance_ecr" {
  statement {
    sid       = "EcrAuth"
    effect    = "Allow"
    actions   = ["ecr:GetAuthorizationToken"]
    resources = ["*"]
  }
  statement {
    sid    = "EcrPull"
    effect = "Allow"
    actions = [
      "ecr:BatchCheckLayerAvailability",
      "ecr:BatchGetImage",
      "ecr:GetDownloadUrlForLayer",
    ]
    resources = [for r in aws_ecr_repository.repo : r.arn]
  }
}

resource "aws_iam_role_policy" "instance_ecr" {
  name   = "${var.project}-instance-ecr"
  role   = aws_iam_role.instance.id
  policy = data.aws_iam_policy_document.instance_ecr.json
}

# Read-only on the assets bucket so the clients-host entrypoint can
# `aws s3 sync` each pinned client's build from clients/<name>/<tag>/ using the
# instance profile (no credentials on the box). Write stays with the deploy role.
data "aws_iam_policy_document" "instance_assets" {
  statement {
    sid       = "AssetsList"
    effect    = "Allow"
    actions   = ["s3:ListBucket"]
    resources = [aws_s3_bucket.assets.arn]
    condition {
      test     = "StringLike"
      variable = "s3:prefix"
      values   = ["clients/*"]
    }
  }
  statement {
    sid       = "AssetsGet"
    effect    = "Allow"
    actions   = ["s3:GetObject"]
    resources = ["${aws_s3_bucket.assets.arn}/clients/*"]
  }
}

resource "aws_iam_role_policy" "instance_assets" {
  name   = "${var.project}-instance-assets"
  role   = aws_iam_role.instance.id
  policy = data.aws_iam_policy_document.instance_assets.json
}

resource "aws_iam_instance_profile" "instance" {
  name = "${var.project}-instance"
  role = aws_iam_role.instance.name
}
