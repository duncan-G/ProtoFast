#!/usr/bin/env bash
# Idempotent dev dependency setup for Ubuntu only.
# Run as your normal login user (not root); the script will use sudo for apt/docker where needed.
#
# Installs if missing: Python 3, Node.js (LTS via NodeSource), @angular/cli, uv (uvx),
# Docker Engine, Aspire CLI (user-local under ~/.aspire/bin), Angular agent skills
# for Cursor (via `npx skills add angular/skills`, global scope).
#
# Prompts for confirmation before any installs and after completion (interactive tty only).
# Non-interactive: pre-install auto-proceeds with a log line; post-install pause is skipped.
# Optional: SETUP_DEV_YES=1 to skip the pre-install confirmation even when interactive.

set -euo pipefail

readonly SCRIPT_NAME="${0##*/}"
PENDING=()

is_interactive() {
  [[ -t 0 && -t 1 ]]
}

log() {
  printf '[%s] %s\n' "$SCRIPT_NAME" "$*"
}

die() {
  printf '[%s] error: %s\n' "$SCRIPT_NAME" "$*" >&2
  exit 1
}

confirm_yes() {
  local prompt=$1
  if [[ "${SETUP_DEV_YES:-}" == "1" ]]; then
    log "SETUP_DEV_YES=1: auto-confirming (${prompt})"
    return 0
  fi
  if ! is_interactive; then
    log "non-interactive; auto-proceeding (${prompt})"
    return 0
  fi
  local reply
  read -r -p "[$SCRIPT_NAME] ${prompt} [y/N] " reply || true
  case "${reply,,}" in
    y | yes) return 0 ;;
    *) return 1 ;;
  esac
}

pause_finish() {
  if ! is_interactive; then
    return 0
  fi
  read -r -p "[$SCRIPT_NAME] Installation finished. Press Enter when you are done reviewing the output above... " _ || true
}

apt_pkg_ok() {
  local pkg=$1
  dpkg-query -W -f='${Status}' "$pkg" 2>/dev/null | grep -q "install ok installed"
}

needs_base_apt_pkgs() {
  local pkg
  for pkg in ca-certificates curl gnupg; do
    apt_pkg_ok "$pkg" || return 0
  done
  return 1
}

needs_python3() { ! command -v python3 >/dev/null 2>&1; }
needs_node() { ! command -v node >/dev/null 2>&1; }
needs_ng() { ! command -v ng >/dev/null 2>&1; }
needs_uvx() { ! command -v uvx >/dev/null 2>&1; }
needs_docker_engine() { ! command -v docker >/dev/null 2>&1; }
needs_docker_group() { ! id -nG "$USER" | tr ' ' '\n' | grep -qx docker; }
needs_aspire_cli() { ! PATH="${HOME}/.aspire/bin:${PATH}" command -v aspire >/dev/null 2>&1; }
needs_angular_skills() { [[ ! -d "${HOME}/.cursor/skills/angular-developer" ]]; }

build_pending_plan() {
  PENDING=()
  # Use `if ... fi` (not `cmd && action`) so each line always returns 0 even
  # when the dependency is already satisfied. Otherwise, when everything is
  # installed, the last `needs_* && ...` short-circuits to status 1, making the
  # function return 1, which trips `set -e` at the caller and silently kills
  # the script before the "nothing to install" message is printed.
  if needs_base_apt_pkgs; then PENDING+=("Ensure apt packages: ca-certificates, curl, gnupg"); fi
  if needs_python3; then PENDING+=("Install Python 3 (apt: python3, python3-venv)"); fi
  if needs_node; then PENDING+=("Install Node.js LTS (NodeSource + apt package nodejs)"); fi
  if needs_ng; then PENDING+=("Install Angular CLI globally (@angular/cli via npm)"); fi
  if needs_uvx; then PENDING+=("Install uv (astral.sh installer; provides uvx)"); fi
  if needs_docker_engine; then PENDING+=("Install Docker Engine (get.docker.com)"); fi
  if needs_docker_group; then PENDING+=("Add user '${USER}' to group 'docker'"); fi
  if needs_aspire_cli; then PENDING+=("Install Aspire CLI (~/.aspire/bin; updates shell profile PATH)"); fi
  if needs_angular_skills; then PENDING+=("Install Angular agent skills for Cursor (npx skills add angular/skills, global)"); fi
  return 0
}

any_pending() { [[ ${#PENDING[@]} -gt 0 ]]; }

print_pending_plan() {
  local i
  log "planned actions (${#PENDING[@]}):"
  for i in "${!PENDING[@]}"; do
    printf '  %d) %s\n' "$((i + 1))" "${PENDING[$i]}"
  done
}

require_non_root() {
  if [[ "${EUID:-0}" -eq 0 ]]; then
    die "run as your normal user (with sudo available), not as root. Example: bash scripts/$SCRIPT_NAME"
  fi
}

require_ubuntu() {
  [[ -f /etc/os-release ]] || die "/etc/os-release not found"
  # shellcheck source=/dev/null
  . /etc/os-release
  [[ "${ID:-}" == "ubuntu" ]] || die "this script supports Ubuntu only (ID=$ID)"
}

require_sudo() {
  command -v sudo >/dev/null 2>&1 || die "sudo is required"
  sudo -v
}

APT_UPDATED=0
apt_update_once() {
  if [[ "$APT_UPDATED" -eq 0 ]]; then
    export DEBIAN_FRONTEND=noninteractive
    sudo apt-get update -qq
    APT_UPDATED=1
  fi
}

apt_install() {
  apt_update_once
  export DEBIAN_FRONTEND=noninteractive
  sudo apt-get install -y --no-install-recommends "$@"
}

ensure_base_apt_deps() {
  apt_install ca-certificates curl gnupg
}

ensure_python3() {
  if command -v python3 >/dev/null 2>&1; then
    log "python3 already present ($(command -v python3))"
    return 0
  fi
  log "installing python3"
  apt_install python3 python3-venv
}

ensure_node() {
  if command -v node >/dev/null 2>&1; then
    log "node already present ($(command -v node); $(node --version))"
    return 0
  fi
  log "installing Node.js LTS (NodeSource)"
  curl -fsSL https://deb.nodesource.com/setup_lts.x | sudo -E bash -
  sudo apt-get install -y nodejs
  log "node installed ($(command -v node); $(node --version))"
}

ensure_angular_cli() {
  if command -v ng >/dev/null 2>&1; then
    log "angular cli (ng) already present ($(command -v ng))"
    return 0
  fi
  ensure_node
  log "installing @angular/cli globally"
  sudo npm install -g @angular/cli
  log "angular cli installed ($(command -v ng))"
}

ensure_uvx() {
  if command -v uvx >/dev/null 2>&1; then
    log "uvx already present ($(command -v uvx))"
    return 0
  fi
  log "installing uv (provides uvx)"
  curl -LsSf https://astral.sh/uv/install.sh | sh
  # Same-shell PATH for any checks in this run
  export PATH="${HOME}/.local/bin:${PATH}"
  command -v uvx >/dev/null 2>&1 || die "uvx not on PATH after install; open a new shell or add ~/.local/bin to PATH"
  log "uv installed ($(command -v uv); $(command -v uvx))"
}

ensure_docker() {
  if command -v docker >/dev/null 2>&1; then
    log "docker already present ($(command -v docker))"
  else
    log "installing Docker Engine (official convenience script)"
    curl -fsSL https://get.docker.com | sudo sh
    log "docker installed ($(command -v docker))"
  fi

  if id -nG "$USER" | tr ' ' '\n' | grep -qx docker; then
    log "user '$USER' already in group 'docker'"
  else
    log "adding user '$USER' to group 'docker' (log out/in for group to apply to new sessions)"
    sudo usermod -aG docker "$USER"
  fi
}

ensure_aspire_cli() {
  local aspire_dir="${HOME}/.aspire/bin"
  export PATH="${aspire_dir}:${PATH}"

  if command -v aspire >/dev/null 2>&1; then
    log "aspire cli already present ($(command -v aspire))"
    return 0
  fi

  log "installing Aspire CLI (user install under ${aspire_dir})"
  curl -fsSL https://aspire.dev/install.sh | bash -s --
  export PATH="${aspire_dir}:${PATH}"
  command -v aspire >/dev/null 2>&1 || die "aspire not on PATH after install; ensure ${aspire_dir} is in PATH (script may have updated your shell profile)"
  log "aspire cli installed ($(command -v aspire))"
}

ensure_angular_skills() {
  local skills_dir="${HOME}/.cursor/skills"
  if [[ -d "${skills_dir}/angular-developer" ]]; then
    log "angular agent skills already present (${skills_dir}/angular-developer)"
    return 0
  fi
  ensure_node
  log "installing Angular agent skills (cursor, global) via npx skills"
  # --yes on npx skips the "install skills@x.y.z?" prompt; -y on skills skips its own prompts.
  npx --yes skills add https://github.com/angular/skills -g -a cursor -y
  [[ -d "${skills_dir}/angular-developer" ]] \
    || log "warning: angular-developer skill not found under ${skills_dir} after install; verify with 'npx skills list -g'"
}

main() {
  require_non_root
  require_ubuntu
  require_sudo

  log "starting (Ubuntu; user=${USER})"
  build_pending_plan
  if ! any_pending; then
    log "nothing to install; all listed dependencies are already satisfied."
    return 0
  fi

  print_pending_plan
  if ! confirm_yes "Proceed with these actions"; then
    log "aborted before install; no changes were made."
    return 0
  fi

  ensure_base_apt_deps
  ensure_python3
  ensure_node
  ensure_angular_cli
  ensure_uvx
  ensure_docker
  ensure_aspire_cli
  ensure_angular_skills

  log "done. If docker group or PATH changed, open a new terminal or log out and back in."
  pause_finish
}

main "$@"
