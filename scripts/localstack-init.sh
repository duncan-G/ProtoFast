#!/usr/bin/env bash
#
# Creates the S3 buckets and SQS queues the stack expects, so `aspire run` comes up on a fresh
# clone with every resource its services are already configured to talk to. Mounted into
# /etc/localstack/init/ready.d, which runs after the emulated services are accepting calls — see
# LocalStackResourceBuilderExtensions.AddLocalStack.
#
set -euo pipefail

# USE_SSL and LOCALSTACK_HOST are set on the container so any URL LocalStack generates carries the
# https:// gateway address the browser and the host-side .NET clients were given (see
# LocalStackResource.GatewayUrl)
unset USE_SSL LOCALSTACK_HOST

# AddLocalStack always sets this; the default is only for running the script by hand.
REGION="${AWS_DEFAULT_REGION:-us-east-1}"

# The browser origins the presigned upload arrives from, set by WithClientOrigins, which derives
# them from the Envoy per-client listener ports. The same variable feeds the bucket rules below and
# LocalStack's own fallback: reading it here rather than asking the AppHost for a second copy is
# what keeps the two from drifting apart.
ORIGINS="${EXTRA_CORS_ALLOWED_ORIGINS:-}"

# Queue shapes, kept together so they are tunable in one place rather than buried in the JSON.
# Fourteen days is SQS's maximum retention and what a dead-letter queue wants — the default four
# would quietly discard the failures the queue exists to preserve.
readonly VISIBILITY_TIMEOUT=900
readonly MESSAGE_RETENTION=1209600
readonly RECEIVE_WAIT=20
readonly MAX_RECEIVE_COUNT=5

log() {
  printf 'localstack-init: %s\n' "$*"
}

die() {
  printf 'localstack-init: %s\n' "$*" >&2
  exit 1
}

# One name per line, with the whitespace around each comma dropped and empty entries removed, so a
# list that picked up a trailing or doubled comma does not turn into a resource named "". The
# trailing newline printf adds is what terminates the last name: without it `read` returns false on
# that final line and the loops below drop the last bucket and the last queue silently.
split_list() {
  printf '%s\n' "${1:-}" | tr ',' '\n' | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//' -e '/^$/d'
}

create_bucket() {
  local bucket="$1" error

  # us-east-1 is the one region S3 takes no location constraint for, and sending one anyway is as
  # much an error as omitting it elsewhere — LocalStack rejects both exactly as S3 does.
  local -a location=()
  if [ "$REGION" != "us-east-1" ]; then
    location=(--create-bucket-configuration "LocationConstraint=${REGION}")
  fi

  # An existing bucket is the expected outcome of a re-run and the only failure worth swallowing;
  # anything else (a gateway that is not really up, a name S3 will not take) has to stop the script
  # rather than let the remaining resources fail one by one for the same underlying reason.
  if ! error="$(awslocal s3api create-bucket \
    --bucket "$bucket" \
    --region "$REGION" \
    ${location+"${location[@]}"} 2>&1)"; then
    case "$error" in
      *BucketAlreadyOwnedByYou*|*BucketAlreadyExists*) ;;
      *) die "could not create bucket ${bucket}: ${error}" ;;
    esac
  fi

  log "bucket ${bucket} ready"
}

put_bucket_cors() {
  local bucket="$1" origins_json error

  origins_json="$(printf '%s' "$ORIGINS" |
    awk -F, '{for (i=1;i<=NF;i++) printf "%s\"%s\"", (i>1 ? ", " : ""), $i}')"

  if ! error="$(awslocal s3api put-bucket-cors --bucket "$bucket" --cors-configuration "{
    \"CORSRules\": [
      {
        \"AllowedMethods\": [\"POST\", \"PUT\", \"GET\", \"HEAD\"],
        \"AllowedOrigins\": [${origins_json}],
        \"AllowedHeaders\": [\"*\"],
        \"ExposeHeaders\": [\"etag\"],
        \"MaxAgeSeconds\": 3600
      }
    ]
  }" 2>&1)"; then
    die "could not set CORS on ${bucket}: ${error}"
  fi

  log "bucket ${bucket} allows ${ORIGINS}"
}

# A FIFO queue's name must end in .fifo and so must its dead-letter queue's, so the suffix goes
# before the extension rather than after it: orders.fifo pairs with orders-dlq.fifo, not with
# orders.fifo-dlq, which SQS would reject outright.
dlq_name_for() {
  case "$1" in
    *.fifo) printf '%s-dlq.fifo' "${1%.fifo}" ;;
    *)      printf '%s-dlq' "$1" ;;
  esac
}

# SQS rejects a .fifo name unless the queue is declared FIFO, so the attribute is inferred from the
# name the AppHost gave rather than asked of the caller a second time.
fifo_attributes_for() {
  case "$1" in
    *.fifo) printf '"FifoQueue":"true",' ;;
  esac
}

# Prints the queue's ARN, creating it if it is not already there. Used for the dead-letter queue,
# whose ARN the redrive policy on the main queue has to name.
create_queue_returning_arn() {
  local queue="$1" url arn

  if ! url="$(awslocal sqs create-queue \
    --queue-name "$queue" \
    --attributes "{$(fifo_attributes_for "$queue")\"MessageRetentionPeriod\":\"${MESSAGE_RETENTION}\"}" \
    --output text --query QueueUrl 2>&1)"; then
    die "could not create queue ${queue}: ${url}"
  fi

  if ! arn="$(awslocal sqs get-queue-attributes \
    --queue-url "$url" \
    --attribute-names QueueArn \
    --output text --query 'Attributes.QueueArn' 2>&1)"; then
    die "could not read the ARN of ${queue}: ${arn}"
  fi

  printf '%s' "$arn"
}

create_queue() {
  local queue="$1" dlq dlq_arn redrive error

  # A name that is already a dead-letter queue is created as an ordinary queue with no redrive of
  # its own: giving it one would mean a foo-dlq-dlq nothing asked for, and a failure that moves one
  # more hop away from where anyone is looking for it.
  case "$queue" in
    *-dlq|*-dlq.fifo)
      create_queue_returning_arn "$queue" >/dev/null
      log "queue ${queue} ready (dead-letter queue; no redrive of its own)"
      return
      ;;
  esac

  dlq="$(dlq_name_for "$queue")"
  dlq_arn="$(create_queue_returning_arn "$dlq")"
  log "queue ${dlq} ready"

  # The redrive policy is a JSON document nested inside the JSON of the attribute map, so its
  # quotes have to survive one round of escaping to reach SQS intact.
  redrive="{\"deadLetterTargetArn\":\"${dlq_arn}\",\"maxReceiveCount\":\"${MAX_RECEIVE_COUNT}\"}"

  if ! error="$(awslocal sqs create-queue \
    --queue-name "$queue" \
    --attributes "{$(fifo_attributes_for "$queue")\
\"VisibilityTimeout\":\"${VISIBILITY_TIMEOUT}\",\
\"MessageRetentionPeriod\":\"${MESSAGE_RETENTION}\",\
\"ReceiveMessageWaitTimeSeconds\":\"${RECEIVE_WAIT}\",\
\"RedrivePolicy\":\"$(printf '%s' "$redrive" | sed 's/"/\\"/g')\"}" \
    --output text --query QueueUrl 2>&1)"; then
    die "could not create queue ${queue}: ${error}"
  fi

  log "queue ${queue} ready, dead-lettering to ${dlq} after ${MAX_RECEIVE_COUNT} receives"
}

buckets=()
while IFS= read -r name; do
  buckets+=("$name")
done < <(split_list "${BUCKET_NAMES:-}")

queues=()
while IFS= read -r name; do
  queues+=("$name")
done < <(split_list "${QUEUE_NAMES:-}")

if [ ${#buckets[@]} -eq 0 ] && [ ${#queues[@]} -eq 0 ]; then
  log "neither BUCKET_NAMES nor QUEUE_NAMES is set; nothing to create"
  exit 0
fi

log "creating ${#buckets[@]} bucket(s) and ${#queues[@]} queue(s) with dead-letter queues in ${REGION}"

for bucket in ${buckets[@]+"${buckets[@]}"}; do
  create_bucket "$bucket"

  # No origins means no client is served over a browser origin yet. Leaving the bucket without a
  # CORS configuration is the right state for that: LocalStack then falls back to its own
  # permissive handling instead of a rule list that allows nobody.
  if [ -n "$ORIGINS" ]; then
    put_bucket_cors "$bucket"
  fi
done

for queue in ${queues[@]+"${queues[@]}"}; do
  create_queue "$queue"
done

log "done"
