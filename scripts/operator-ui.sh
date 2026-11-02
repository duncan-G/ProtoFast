#!/usr/bin/env bash
# Opens ProtoStrap's operator UI for prod (its docs/protostrap.md#operator-ui): the scheduler's
# state and the infra log, through an SSM port forward to data-1. Platform Admins and Org Admins.
#
#   AWS_PROFILE=<platform-admin or org-admin> scripts/operator-ui.sh [--admin] [local port]
#
# Then open http://localhost:7480 (7481 with --admin). Env: AWS_PROFILE, AWS_REGION (default
# us-west-2), PROTOSTRAP_SOURCE (default .protostrap, or ../ProtoStrap).
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
source_dir=${PROTOSTRAP_SOURCE:-}
if [[ -z "$source_dir" ]]; then
  for candidate in "$root/.protostrap" "$root/../ProtoStrap"; do
    [[ -f "$candidate/scripts/operator-ui.sh" ]] && { source_dir=$candidate; break; }
  done
fi
[[ -n "$source_dir" ]] || { echo "no ProtoStrap checkout: set PROTOSTRAP_SOURCE, or clone it at the spec's version into .protostrap" >&2; exit 1; }

export AWS_REGION=${AWS_REGION:-us-west-2} PROTOSTRAP_APP=protofast
aws sts get-caller-identity --query Arn --output text >/dev/null || { echo "no AWS credentials: aws sso login --profile \$AWS_PROFILE" >&2; exit 1; }
exec bash "$source_dir/scripts/operator-ui.sh" "$@"
