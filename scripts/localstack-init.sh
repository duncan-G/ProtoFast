#!/bin/bash
#
# Creates the segmentation bucket and queues in LocalStack so `aspire run` comes up with a
# working stack on a fresh clone (plan §22.1). Mounted into /etc/localstack/init/ready.d, which
# runs after the emulated services are accepting calls.
#
# The shapes here mirror infra/segmentation.tf as closely as LocalStack supports: the same queue
# names, the same redrive policy, the same lifecycle intent. Object lock is the one exception —
# LocalStack does not implement it, so Seg_Storage__ObjectLockEnabled is false in development and
# the worker logs that "frozen" is weaker there than in production.
set -euo pipefail

BUCKET="${SEGMENTATION_BUCKET:-protofast-segmentation-dev}"
REGION="${AWS_DEFAULT_REGION:-us-east-1}"
PROJECT="protofast"

echo "localstack-init: creating bucket ${BUCKET} and the segmentation queues in ${REGION}"

awslocal s3api create-bucket --bucket "$BUCKET" --region "$REGION" 2>/dev/null || true

# CORS for the browser's presigned PUT. The dev origins are the Envoy per-client listeners;
# production allows only the ThePlot domain (infra/segmentation.tf).
awslocal s3api put-bucket-cors --bucket "$BUCKET" --cors-configuration '{
  "CORSRules": [
    {
      "AllowedMethods": ["PUT"],
      "AllowedOrigins": ["https://localhost:20000", "https://localhost:20001", "https://localhost:20002"],
      "AllowedHeaders": ["*"],
      "ExposeHeaders": ["etag"],
      "MaxAgeSeconds": 3600
    }
  ]
}'

DLQ_URL="$(awslocal sqs create-queue --queue-name "${PROJECT}-segmentation-dlq" --output text --query QueueUrl)"
DLQ_ARN="$(awslocal sqs get-queue-attributes --queue-url "$DLQ_URL" --attribute-names QueueArn --output text --query 'Attributes.QueueArn')"

REDRIVE="{\"deadLetterTargetArn\":\"${DLQ_ARN}\",\"maxReceiveCount\":\"5\"}"

for queue in runs runs-bulk batch-poll; do
  awslocal sqs create-queue \
    --queue-name "${PROJECT}-segmentation-${queue}" \
    --attributes "{\"VisibilityTimeout\":\"900\",\"MessageRetentionPeriod\":\"1209600\",\"ReceiveMessageWaitTimeSeconds\":\"20\",\"RedrivePolicy\":\"$(echo "$REDRIVE" | sed 's/"/\\"/g')\"}" \
    >/dev/null
  echo "localstack-init: queue ${PROJECT}-segmentation-${queue} ready"
done

echo "localstack-init: done"
