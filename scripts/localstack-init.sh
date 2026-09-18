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

# USE_SSL and LOCALSTACK_HOST are set on the container so the browser's presigned PUT and the
# host-side .NET clients get an https:// gateway URL (see
# LocalStackResourceBuilderExtensions.GatewayUrl). awslocal builds its --endpoint-url from those
# two env vars directly — it does not consult AWS_ENDPOINT_URL at all — so left alone, every
# awslocal call below would also go out over TLS to the public localhost.localstack.cloud
# hostname: a DNS lookup and a cert this script has no reason to depend on when it is only ever
# talking to the LocalStack instance it is running inside of. Unsetting them here (not exporting
# an endpoint override, which awslocal ignores) makes awslocal fall back to its own default,
# plain http://localhost:4566.
unset USE_SSL LOCALSTACK_HOST

BUCKET="${SEGMENTATION_BUCKET:-protofast-segmentation-dev}"
REGION="${AWS_DEFAULT_REGION:-us-west-2}"
PROJECT="protofast"

# us-east-1 is the one region S3 takes no location constraint for, and sending one anyway is as
# much an error as omitting it elsewhere — LocalStack rejects both exactly as S3 does.
CREATE_BUCKET_ARGS=()
if [ "$REGION" != "us-east-1" ]; then
  CREATE_BUCKET_ARGS=(--create-bucket-configuration "LocationConstraint=${REGION}")
fi

echo "localstack-init: creating bucket ${BUCKET} and the segmentation queues in ${REGION}"

# BucketAlreadyOwnedByYou is the only failure worth ignoring here; anything else (a gateway that
# is not actually up, a malformed name) has to stop the script rather than let the CORS and queue
# steps below fail one by one against a bucket that does not exist.
if ! create_error="$(awslocal s3api create-bucket --bucket "$BUCKET" --region "$REGION" \
  ${CREATE_BUCKET_ARGS+"${CREATE_BUCKET_ARGS[@]}"} 2>&1)"; then
  case "$create_error" in
    *BucketAlreadyOwnedByYou*|*BucketAlreadyExists*) ;;
    *) echo "localstack-init: could not create ${BUCKET}: ${create_error}" >&2; exit 1 ;;
  esac
fi

# CORS for the browser's presigned POST and GET. The origins come from the AppHost, which derives
# them from the Envoy per-client listener ports (WithClientOrigins) — a rule that does not list
# the origin the page was served from fails the preflight with a 403 and shows up in the browser
# as a bare CORS error. Production allows only the ThePlot domain (infra/segmentation.tf).
#
# POST is the upload verb since the ingest plan: only a POST carries a signed policy, and only a
# policy can carry the content-length-range condition that makes the 10 MiB limit S3's to enforce.
# PUT stays listed until nothing mints one any more.
#
# GET is here and not in the Terraform rules because only dev reads artifacts cross-origin: in
# production the browser downloads them through the api origin.
ORIGINS="${SEGMENTATION_CORS_ORIGINS:-https://localhost:20000,https://localhost:20001,https://localhost:20002}"
ORIGINS_JSON="$(printf '%s' "$ORIGINS" | awk -F, '{for (i=1;i<=NF;i++) printf "%s\"%s\"", (i>1 ? ", " : ""), $i}')"

echo "localstack-init: allowing browser origins ${ORIGINS}"

awslocal s3api put-bucket-cors --bucket "$BUCKET" --cors-configuration "{
  \"CORSRules\": [
    {
      \"AllowedMethods\": [\"POST\", \"PUT\", \"GET\", \"HEAD\"],
      \"AllowedOrigins\": [${ORIGINS_JSON}],
      \"AllowedHeaders\": [\"*\"],
      \"ExposeHeaders\": [\"etag\"],
      \"MaxAgeSeconds\": 3600
    }
  ]
}"

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
