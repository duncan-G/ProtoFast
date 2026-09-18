# Segmentation feature infrastructure (docs/theplot-segmentation-plan.md §19).
#
# One S3 bucket for run artifacts, uploads and frozen output; three SQS lanes plus a shared DLQ;
# and the IAM to reach them from the existing instance role. Nothing here publishes a port or
# changes the security groups: the worker is reached by nothing — it pulls from SQS and writes to
# S3 and Postgres.

# ---------------------------------------------------------------------------
# S3
# ---------------------------------------------------------------------------

# Deliberately NOT the assets bucket. That one is narrow on purpose — client bundles, the deploy
# manifest and DB backups, with a lifecycle rule that must never age-expire clients/. Run
# artifacts want the opposite: aggressive expiry on intermediates, object lock on frozen output,
# CORS for browser uploads, and a different IAM blast radius (plan §16.2).
resource "aws_s3_bucket" "segmentation" {
  bucket = var.segmentation_bucket

  # Must be set at creation and cannot be turned off afterwards. See the note on the lifecycle
  # rule below before the first apply.
  object_lock_enabled = true

  # Frozen output is a user-visible product, not cattle.
  force_destroy = false
}

resource "aws_s3_bucket_versioning" "segmentation" {
  bucket = aws_s3_bucket.segmentation.id
  versioning_configuration {
    status = "Enabled" # required for object lock
  }
}

resource "aws_s3_bucket_public_access_block" "segmentation" {
  bucket                  = aws_s3_bucket.segmentation.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_server_side_encryption_configuration" "segmentation" {
  bucket = aws_s3_bucket.segmentation.id
  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

# Browser uploads go straight to S3 with a presigned POST (plan §18.3, ingest plan §7), so the
# bucket needs CORS for exactly the client origins — never "*", which would let any page replay a
# leaked URL from a browser context. The localhost entries are the Envoy per-client listeners in
# development.
#
# POST is the upload verb: only a POST carries a signed policy document, and only a policy can
# carry the content-length-range condition that makes the 10 MiB limit S3's to enforce rather than
# the browser's to respect. PUT stays listed until nothing mints one any more, then can be dropped.
#
# allowed_headers is unchanged: a multipart POST sends its fields in the body, and content-type is
# already listed for the part the browser generates a boundary for.
resource "aws_s3_bucket_cors_configuration" "segmentation" {
  bucket = aws_s3_bucket.segmentation.id

  cors_rule {
    allowed_methods = ["POST", "PUT"]
    allowed_origins = [
      "https://${var.theplot_domain}",
      "https://localhost:20002",
    ]
    allowed_headers = ["content-type", "content-md5", "x-amz-*"]
    expose_headers  = ["etag"]
    max_age_seconds = 3600
  }
}

# NOTE ON OBJECT LOCK AND EXPIRY.
#
# The frozen artifact is written twice: under runs/{runId}/09_frozen.json with a GOVERNANCE
# retention, and under frozen/{documentId}/{treeHash}.json without one. The pointer copy is the
# read path; the locked copy is what makes "frozen" a storage-level fact.
#
# That means the expire-run-artifacts rule below cannot actually delete 09_frozen.json while its
# retention holds, so a run prefix lingers past its 30 days with one object in it. The alternative
# — writing the frozen artifact only under frozen/ — would let run prefixes expire cleanly but
# would drop the storage-level guarantee for the copy that belongs to the run.
#
# Decide this BEFORE the first apply: object lock cannot be turned off afterwards, only worked
# around with a new bucket (plan §19.2).
resource "aws_s3_bucket_lifecycle_configuration" "segmentation" {
  bucket = aws_s3_bucket.segmentation.id

  # Presigned uploads are copied under the run prefix at phase 0, so the original is scratch.
  rule {
    id     = "expire-uploads"
    status = "Enabled"
    filter {
      prefix = "uploads/"
    }
    expiration {
      days = 7
    }
  }

  # Intermediate phase artifacts are debugging material once a run publishes. Frozen output also
  # lives under frozen/ (and carries object-lock retention besides), so this rule cannot lose it.
  rule {
    id     = "expire-run-artifacts"
    status = "Enabled"
    filter {
      prefix = "runs/"
    }
    expiration {
      days = var.segmentation_artifact_retention_days
    }
    noncurrent_version_expiration {
      noncurrent_days = 7
    }
  }

  rule {
    id     = "abort-incomplete-multipart"
    status = "Enabled"
    filter {}
    abort_incomplete_multipart_upload {
      days_after_initiation = 7
    }
  }
}

# ---------------------------------------------------------------------------
# SQS
# ---------------------------------------------------------------------------

# One DLQ for every lane: a run that fails five receives is a bug or a poisoned document, and both
# want the same triage queue. A message here is an alert, not a dashboard curiosity — each one is
# a run a user is still waiting on (plan §25.3).
resource "aws_sqs_queue" "segmentation_dlq" {
  name                      = "${var.project}-segmentation-dlq"
  message_retention_seconds = 1209600 # 14 days
  sqs_managed_sse_enabled   = true
}

locals {
  segmentation_redrive = jsonencode({
    deadLetterTargetArn = aws_sqs_queue.segmentation_dlq.arn
    maxReceiveCount     = 5
  })
}

# Realtime lane: interactive runs from ThePlot.
resource "aws_sqs_queue" "segmentation_runs" {
  name                       = "${var.project}-segmentation-runs"
  visibility_timeout_seconds = var.segmentation_visibility_timeout_seconds
  message_retention_seconds  = 1209600
  receive_wait_time_seconds  = 20 # long polling; cuts empty receives to near zero
  sqs_managed_sse_enabled    = true
  redrive_policy             = local.segmentation_redrive
}

# Bulk lane: Priority=Bulk runs, served by provider batch APIs. A separate queue so a
# ten-thousand-document import cannot starve interactive runs.
resource "aws_sqs_queue" "segmentation_runs_bulk" {
  name                       = "${var.project}-segmentation-runs-bulk"
  visibility_timeout_seconds = var.segmentation_visibility_timeout_seconds
  message_retention_seconds  = 1209600
  receive_wait_time_seconds  = 20
  sqs_managed_sse_enabled    = true
  redrive_policy             = local.segmentation_redrive
}

# Deferred work: provider-batch polls and human-review resumptions. Messages carry DelaySeconds,
# so a pending batch or an undecided review consumes no worker time until it is worth looking at
# again (plan §14.8).
resource "aws_sqs_queue" "segmentation_batch_poll" {
  name                       = "${var.project}-segmentation-batch-poll"
  visibility_timeout_seconds = 300
  message_retention_seconds  = 1209600
  receive_wait_time_seconds  = 20
  sqs_managed_sse_enabled    = true
  redrive_policy             = local.segmentation_redrive
}

# ---------------------------------------------------------------------------
# IAM
# ---------------------------------------------------------------------------

# api, the worker and the conversion sidecar all run on Host B and share the existing instance
# profile, so this is one more inline policy on the same role. The permissions boundary from
# infra/bootstrap already applies to it; nothing here grants IAM or secret-value APIs, so it stays
# inside the boundary.
#
# The converter needs no statement of its own: its GetObject and PutObject are under uploads/,
# which SegmentationObjects already covers (that statement carries no prefix condition), and the
# ListBucket condition below already lists uploads/*.
data "aws_iam_policy_document" "instance_segmentation" {
  statement {
    sid       = "SegmentationBucketList"
    effect    = "Allow"
    actions   = ["s3:ListBucket"]
    resources = [aws_s3_bucket.segmentation.arn]

    # Scoped to the prefixes ArtifactKeys mints. A key built anywhere else is denied here, which
    # is the second half of the guarantee that GetArtifact cannot presign outside a caller's run.
    condition {
      test     = "StringLike"
      variable = "s3:prefix"
      values   = ["uploads/*", "runs/*", "frozen/*", "eval/*"]
    }
  }

  statement {
    sid    = "SegmentationObjects"
    effect = "Allow"
    actions = [
      "s3:GetObject",
      "s3:PutObject",
      "s3:DeleteObject",       # scratch cleanup only; locked objects reject it
      "s3:PutObjectRetention", # the freeze (plan §9.11)
      "s3:GetObjectRetention",
    ]
    resources = ["${aws_s3_bucket.segmentation.arn}/*"]

    # Deliberately absent: s3:BypassGovernanceRetention. The worker writes the lock and cannot
    # unwrite it, which is what makes frozen output tamper-evident against the worker itself
    # (plan §24.1).
  }

  statement {
    sid    = "SegmentationQueues"
    effect = "Allow"
    actions = [
      "sqs:SendMessage",    # api submits
      "sqs:ReceiveMessage", # the worker consumes
      "sqs:DeleteMessage",
      "sqs:ChangeMessageVisibility", # the heartbeat (plan §13.4)
      "sqs:GetQueueAttributes",
      "sqs:GetQueueUrl",
    ]
    resources = [
      aws_sqs_queue.segmentation_runs.arn,
      aws_sqs_queue.segmentation_runs_bulk.arn,
      aws_sqs_queue.segmentation_batch_poll.arn,
      aws_sqs_queue.segmentation_dlq.arn, # redrive inspection from the box
    ]
  }
}

resource "aws_iam_role_policy" "instance_segmentation" {
  name   = "${var.project}-instance-segmentation"
  role   = aws_iam_role.instance.id
  policy = data.aws_iam_policy_document.instance_segmentation.json
}
