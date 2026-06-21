# S3 bucket holding the MAIN infra/ config's Terraform state. Versioned so a bad
# apply is recoverable; the main config uses native S3 lockfiles (use_lockfile),
# so no DynamoDB table is needed (Terraform >= 1.10).

resource "aws_s3_bucket" "state" {
  bucket = var.state_bucket_name

  # Protected: state is precious. Guard against accidental destruction.
  lifecycle {
    prevent_destroy = true
  }
}

resource "aws_s3_bucket_versioning" "state" {
  bucket = aws_s3_bucket.state.id
  versioning_configuration {
    status = "Enabled"
  }
}

resource "aws_s3_bucket_server_side_encryption_configuration" "state" {
  bucket = aws_s3_bucket.state.id
  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "aws:kms"
    }
    bucket_key_enabled = true
  }
}

resource "aws_s3_bucket_public_access_block" "state" {
  bucket                  = aws_s3_bucket.state.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}
