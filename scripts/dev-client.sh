#!/usr/bin/env bash
# Serve the Angular client for local development.
# Used by .claude/launch.json; also runnable directly: scripts/dev-client.sh
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
client_dir="$repo_root/clients/protofast"
cd "$client_dir"

# Pick up the Node version pinned in clients/protofast/.nvmrc when a version
# manager is available; otherwise fall back to whatever node is on PATH.
nvm_dir="${NVM_DIR:-$HOME/.nvm}"
if [ -s "$nvm_dir/nvm.sh" ]; then
  # shellcheck disable=SC1091
  . "$nvm_dir/nvm.sh"
  nvm use >/dev/null || nvm install
elif command -v fnm >/dev/null 2>&1; then
  eval "$(fnm env)"
  fnm use --install-if-missing >/dev/null
fi

if ! command -v node >/dev/null 2>&1; then
  echo "node not found. Install Node $(cat .nvmrc) (see clients/protofast/.nvmrc)." >&2
  exit 1
fi

node_major="$(node -p 'process.versions.node.split(".")[0]')"
if [ "$node_major" -lt 20 ]; then
  echo "Node $(node -v) is too old for Angular 22. Install Node $(cat .nvmrc) (see clients/protofast/.nvmrc)." >&2
  exit 1
fi

if [ ! -d node_modules ]; then
  echo "node_modules missing. Run 'npm ci' in clients/protofast first." >&2
  exit 1
fi

exec npx ng serve --port "${PORT:-4300}" "$@"
