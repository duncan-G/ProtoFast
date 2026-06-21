# Instance profile: SSM core (Session Manager + Run Command, no SSH) and ECR pull
# (so `docker compose pull` needs zero credentials on the box). The permissions
# boundary from bootstrap is attached to satisfy the no-escalation rule.

data "aws_iam_policy" "boundary" {
  name = "${var.project}-boundary"
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
  permissions_boundary = data.aws_iam_policy.boundary.arn
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

resource "aws_iam_instance_profile" "instance" {
  name = "${var.project}-instance"
  role = aws_iam_role.instance.name
}
