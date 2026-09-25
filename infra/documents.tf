# S3 bucket for user documents (ThePlot imports). The browser uploads straight to
# it with a presigned POST the api signs under the instance role (services/shared/
# Storage/S3PresignedUrlStore.cs); the api then HEADs the object before recording
# the document. Private: every read and write is either the instance role itself or
# a policy it signed. Named by infra/bootstrap (the infra role's grant is scoped to
# that exact name) and passed from the DOCUMENTS_BUCKET repo variable.

resource "aws_s3_bucket" "documents" {
  bucket = var.documents_bucket
  # Unlike the assets bucket this holds user data that CI cannot re-upload, so a
  # destroy must fail on a non-empty bucket rather than silently empty it.
  force_destroy = false
}

resource "aws_s3_bucket_public_access_block" "documents" {
  bucket                  = aws_s3_bucket.documents.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_ownership_controls" "documents" {
  bucket = aws_s3_bucket.documents.id
  rule {
    object_ownership = "BucketOwnerEnforced"
  }
}

# SSE-S3 rather than KMS: a browser POST is authorised by the signed policy alone,
# and a KMS key would add kms:GenerateDataKey to the instance role for no gain.
resource "aws_s3_bucket_server_side_encryption_configuration" "documents" {
  bucket = aws_s3_bucket.documents.id
  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

# The presigned POST comes from the theplot origin, so S3 must answer its CORS
# preflight. Mirrors the LocalStack rules in scripts/localstack-init.sh (dev), but
# narrowed to what production uses: the POST upload itself, plus GET/HEAD for
# presigned downloads. No credentials — the signed form fields carry the auth.
resource "aws_s3_bucket_cors_configuration" "documents" {
  bucket = aws_s3_bucket.documents.id
  cors_rule {
    allowed_methods = ["POST", "GET", "HEAD"]
    allowed_origins = ["https://${local.theplot_domain}"]
    allowed_headers = ["*"]
    expose_headers  = ["ETag"]
    max_age_seconds = 3600
  }
}

# Only reap failed multipart uploads. Documents are user data with a database row
# pointing at each one, so nothing is age-expired here.
resource "aws_s3_bucket_lifecycle_configuration" "documents" {
  bucket = aws_s3_bucket.documents.id
  rule {
    id     = "abort-incomplete-multipart"
    status = "Enabled"
    filter {} # whole bucket
    abort_incomplete_multipart_upload {
      days_after_initiation = 7
    }
  }
}

# TLS only. A Deny-only policy is not "public", so block_public_policy allows it.
data "aws_iam_policy_document" "documents_bucket" {
  statement {
    sid     = "DenyInsecureTransport"
    effect  = "Deny"
    actions = ["s3:*"]
    resources = [
      aws_s3_bucket.documents.arn,
      "${aws_s3_bucket.documents.arn}/*",
    ]
    principals {
      type        = "*"
      identifiers = ["*"]
    }
    condition {
      test     = "Bool"
      variable = "aws:SecureTransport"
      values   = ["false"]
    }
  }
}

resource "aws_s3_bucket_policy" "documents" {
  bucket = aws_s3_bucket.documents.id
  policy = data.aws_iam_policy_document.documents_bucket.json

  # S3 rejects a bucket policy while the public-access block is still being
  # applied; ordering them avoids a spurious AccessDenied on first create.
  depends_on = [aws_s3_bucket_public_access_block.documents]
}
