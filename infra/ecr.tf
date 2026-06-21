# One ECR repository per image (deployment-plan §3.1). The instance profile pulls
# from these with zero credentials on the box; the deploy role (bootstrap) pushes.

resource "aws_ecr_repository" "repo" {
  for_each             = toset(var.ecr_repositories)
  name                 = each.value
  image_tag_mutability = "IMMUTABLE"
  force_delete         = true # repos are cattle; images are re-pushable from CI

  image_scanning_configuration {
    scan_on_push = true
  }
}

# Keep the registry bounded — the instance keeps the rollback tag sets locally,
# so ECR only needs recent history.
resource "aws_ecr_lifecycle_policy" "repo" {
  for_each   = aws_ecr_repository.repo
  repository = each.value.name

  policy = jsonencode({
    rules = [{
      rulePriority = 1
      description  = "Keep last 20 images"
      selection = {
        tagStatus   = "any"
        countType   = "imageCountMoreThan"
        countNumber = 20
      }
      action = { type = "expire" }
    }]
  })
}
