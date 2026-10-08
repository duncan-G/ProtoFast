# SQS queues for document import. Mirrors the LocalStack shapes in scripts/localstack-init.sh
# (dev): each queue dead-letters to a <name>-dlq after five receives, and every queue keeps
# messages for fourteen days, SQS's maximum, so a failure is still there when someone looks.
# Names match the services' appsettings.json, so production sets no queue env vars.
#
# Unlike the buckets, these need no bootstrap-chosen name: a queue name is unique per account
# and region only. The infra role's grant is scoped to the ${var.project}-* prefix instead
# (infra/bootstrap/roles.tf).

locals {
  queue_retention_seconds = 1209600
  queue_max_receive_count = 5

  # Only the queue's default; consumers set their own on each receive and renew it while they
  # work (SqsQueueOptions, MessageLease), so a long import is not delivered twice.
  queue_visibility_timeout_seconds = 900
  queue_receive_wait_seconds       = 20
}

# Completed uploads, from the api to the document-import worker.
resource "aws_sqs_queue" "document_import_dlq" {
  name                      = "${var.project}-document-upload-dlq"
  message_retention_seconds = local.queue_retention_seconds
  sqs_managed_sse_enabled   = true
}

resource "aws_sqs_queue" "document_import" {
  name                       = "${var.project}-document-upload"
  message_retention_seconds  = local.queue_retention_seconds
  visibility_timeout_seconds = local.queue_visibility_timeout_seconds
  receive_wait_time_seconds  = local.queue_receive_wait_seconds
  sqs_managed_sse_enabled    = true

  redrive_policy = jsonencode({
    deadLetterTargetArn = aws_sqs_queue.document_import_dlq.arn
    maxReceiveCount     = local.queue_max_receive_count
  })
}

resource "aws_sqs_queue_redrive_allow_policy" "document_import_dlq" {
  queue_url = aws_sqs_queue.document_import_dlq.id
  redrive_allow_policy = jsonencode({
    redrivePermission = "byQueue"
    sourceQueueArns   = [aws_sqs_queue.document_import.arn]
  })
}

# Workflow outcomes, grouped by document family so each family's policy has one writer at a
# time. The sender sets its own deduplication id, so content-based deduplication stays off.
resource "aws_sqs_queue" "workflow_outcomes_dlq" {
  name                      = "${var.project}-workflow-outcomes-dlq.fifo"
  fifo_queue                = true
  message_retention_seconds = local.queue_retention_seconds
  sqs_managed_sse_enabled   = true
}

resource "aws_sqs_queue" "workflow_outcomes" {
  name                       = "${var.project}-workflow-outcomes.fifo"
  fifo_queue                 = true
  message_retention_seconds  = local.queue_retention_seconds
  visibility_timeout_seconds = local.queue_visibility_timeout_seconds
  receive_wait_time_seconds  = local.queue_receive_wait_seconds
  sqs_managed_sse_enabled    = true

  redrive_policy = jsonencode({
    deadLetterTargetArn = aws_sqs_queue.workflow_outcomes_dlq.arn
    maxReceiveCount     = local.queue_max_receive_count
  })
}

resource "aws_sqs_queue_redrive_allow_policy" "workflow_outcomes_dlq" {
  queue_url = aws_sqs_queue.workflow_outcomes_dlq.id
  redrive_allow_policy = jsonencode({
    redrivePermission = "byQueue"
    sourceQueueArns   = [aws_sqs_queue.workflow_outcomes.arn]
  })
}
