#!/usr/bin/env bash
# Opens prod's Grafana (ProtoStrap's docs/telemetry.md#reading-telemetry): the services' logs in
# Loki, traces in Tempo and metrics in Prometheus, through an SSM port forward to data-1.
# Developers, Platform Admins and Org Admins.
#
#   AWS_PROFILE=<developer, platform-admin or org-admin> scripts/telemetry.sh [local port]
#
# Then open http://localhost:7482; curl -s http://localhost:7482/protostrap/help describes its
# API for an agent. Env: AWS_PROFILE, AWS_REGION (default us-west-2), PROTOSTRAP_SOURCE (default
# .protostrap, or ../ProtoStrap).
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
source_dir=${PROTOSTRAP_SOURCE:-}
if [[ -z "$source_dir" ]]; then
  for candidate in "$root/.protostrap" "$root/../ProtoStrap"; do
    [[ -f "$candidate/scripts/telemetry.sh" ]] && { source_dir=$candidate; break; }
  done
fi
[[ -n "$source_dir" ]] || { echo "no ProtoStrap checkout: set PROTOSTRAP_SOURCE, or clone it at the spec's version into .protostrap" >&2; exit 1; }

export AWS_REGION=${AWS_REGION:-us-west-2} PROTOSTRAP_APP=protofast
aws sts get-caller-identity --query Arn --output text >/dev/null || { echo "no AWS credentials: aws sso login --profile \$AWS_PROFILE" >&2; exit 1; }
exec bash "$source_dir/scripts/telemetry.sh" "$@"
