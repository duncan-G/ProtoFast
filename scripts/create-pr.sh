#!/usr/bin/env bash
# Create a GitHub pull request for the current branch.
#
# Title is derived from the branch name: path, hyphen, underscore, and dot
# separators are replaced with spaces and each word is capitalized.
#
#   feature/add-oauth-login  →  Feature Add Oauth Login
#   fix_broken_health.check  →  Fix Broken Health Check
#
# Usage:
#   scripts/create-pr.sh
#   scripts/create-pr.sh "Optional PR description"
#   scripts/create-pr.sh -d "Optional PR description"
#   scripts/create-pr.sh --description "Optional PR description"
#
# The branch is pushed to origin if it has no upstream. Requires `gh` (GitHub CLI)
# authenticated against this repo.

set -euo pipefail

readonly SCRIPT_NAME="${0##*/}"

log() {
  printf '[%s] %s\n' "$SCRIPT_NAME" "$*"
}

die() {
  printf '[%s] error: %s\n' "$SCRIPT_NAME" "$*" >&2
  exit 1
}

usage() {
  cat <<EOF
Usage: $SCRIPT_NAME [-d DESCRIPTION] [DESCRIPTION]

Create a GitHub pull request whose title is the current branch name with
separators removed (replaced by spaces, words capitalized).

Options:
  -d, --description TEXT   PR body (optional; may also be given as a positional arg)
  -h, --help               Show this help
EOF
}

title_from_branch() {
  local branch=$1
  local spaced word result=""

  spaced=$(printf '%s' "$branch" | tr '/_.-' '    ')
  spaced=$(printf '%s' "$spaced" | awk '{$1=$1; print}')

  [[ -n "$spaced" ]] || die "branch name '${branch}' produced an empty title"

  for word in $spaced; do
    result+="${word^} "
  done
  printf '%s' "${result% }"
}

DESCRIPTION=""
POSITIONAL=()

while [[ $# -gt 0 ]]; do
  case "$1" in
    -h | --help)
      usage
      exit 0
      ;;
    -d | --description)
      [[ $# -ge 2 ]] || die "option $1 requires a value"
      DESCRIPTION=$2
      shift 2
      ;;
    --description=*)
      DESCRIPTION=${1#--description=}
      shift
      ;;
    --)
      shift
      POSITIONAL+=("$@")
      break
      ;;
    -*)
      die "unknown option: $1 (try --help)"
      ;;
    *)
      POSITIONAL+=("$1")
      shift
      ;;
  esac
done

if [[ ${#POSITIONAL[@]} -gt 0 ]]; then
  [[ -z "$DESCRIPTION" ]] || die "description given more than once"
  [[ ${#POSITIONAL[@]} -eq 1 ]] || die "too many arguments (try --help)"
  DESCRIPTION=${POSITIONAL[0]}
fi

command -v git >/dev/null 2>&1 || die "git is required"
command -v gh >/dev/null 2>&1 || die "GitHub CLI (gh) is required — install it from https://cli.github.com/"

git rev-parse --is-inside-work-tree >/dev/null 2>&1 || die "not inside a git repository"

gh auth status >/dev/null 2>&1 || die "gh is not authenticated; run: gh auth login"

branch=$(git branch --show-current)
[[ -n "$branch" ]] || die "detached HEAD; check out a branch first"

default_branch=$(gh repo view --json defaultBranchRef --jq .defaultBranchRef.name 2>/dev/null || true)
default_branch=${default_branch:-main}

[[ "$branch" != "$default_branch" ]] || die "refusing to open a PR from the default branch (${default_branch})"

title=$(title_from_branch "$branch")

if git rev-parse --abbrev-ref --symbolic-full-name '@{upstream}' >/dev/null 2>&1; then
  log "pushing ${branch} to its upstream"
  git push
else
  log "pushing ${branch} and setting upstream to origin"
  git push -u origin HEAD
fi

log "creating PR: ${title}"

gh_args=(pr create --title "$title" --base "$default_branch" --head "$branch")
if [[ -n "$DESCRIPTION" ]]; then
  gh_args+=(--body "$DESCRIPTION")
else
  gh_args+=(--body "")
fi

url=$(gh "${gh_args[@]}")
log "$url"
printf '%s\n' "$url"
