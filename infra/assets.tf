# S3 assets bucket for client SSR builds (docs/independent-deployment-plan.md §7).
# Each client workflow uploads its built output to clients/<name>/<tag>/; the
# clients-host entrypoint pulls the pinned set on every (re)start using the EC2
# instance profile. No public access — assets are server-side only (the unified
# SSR host serves them to browsers; the bucket is never a website origin).
#
# Name is deterministic (project + account id) so bootstrap can grant the deploy
# role write access and publish the ASSETS_BUCKET repo variable without depending
# on this stack's outputs — mirroring how the ECR registry host is derived.

resource "aws_s3_bucket" "assets" {
  bucket        = "${var.project}-assets-${data.aws_caller_identity.current.account_id}"
  force_destroy = true # cattle: client tags are re-uploadable from CI
}

resource "aws_s3_bucket_public_access_block" "assets" {
  bucket                  = aws_s3_bucket.assets.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_server_side_encryption_configuration" "assets" {
  bucket = aws_s3_bucket.assets.id
  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

# Only reap failed multipart uploads. Superseded client tags are pruned by CI
# (beyond KEEP_RELEASES) on each deploy; we must NOT age-expire clients/ here —
# the pinned tag currently in versions.env may sit unchanged for a long time and
# the host re-pulls it on every restart, so a time-based rule could delete the
# live release out from under a reboot.
resource "aws_s3_bucket_lifecycle_configuration" "assets" {
  bucket = aws_s3_bucket.assets.id
  rule {
    id     = "abort-incomplete-multipart"
    status = "Enabled"
    filter {} # whole bucket
    abort_incomplete_multipart_upload {
      days_after_initiation = 7
    }
  }
}
