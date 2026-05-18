# Step 0 — Pre-flight

Run every check below before any generator. From the repo root.

## 0a. Confirm you're in the project repo

```bash
test -f README.md && test -d .cursor && echo OK
git config --get remote.origin.url 2>/dev/null
```

Verify this is the correct repo for the project the user named.
If it's not, stop and report.

## 0b. Source the user's Node version manager

The agent's `PATH` doesn't include version-manager installs by
default. Try the common managers in order; the first one that exists
wins:

```bash
# nvm
if [ -s "$HOME/.nvm/nvm.sh" ]; then
  export NVM_DIR="$HOME/.nvm"
  \. "$NVM_DIR/nvm.sh"
  nvm use default >/dev/null 2>&1 || true
fi

# fnm
command -v fnm >/dev/null 2>&1 && eval "$(fnm env --use-on-cd)"

# volta
[ -d "$HOME/.volta/bin" ] && export PATH="$HOME/.volta/bin:$PATH"

# asdf
[ -s "$HOME/.asdf/asdf.sh" ] && \. "$HOME/.asdf/asdf.sh"
```

Verify **both** `node` and `npm` resolve, and that both come from
the same real installation:

```bash
node -v && npm -v && command -v node && command -v npm
```

Acceptance criteria:

- `node -v` reports a recent LTS.
- `npm -v` resolves successfully.
- `command -v node` and `command -v npm` point at the **same** real
  install — a version manager's directory or a system package install
  (`/usr/bin/node`, `/usr/local/bin/node`, `/opt/homebrew/bin/node`).

If `node` resolves but `npm` doesn't, stop and ask the user to
install real Node (the resolved `node` is a vendor IDE's bundled
helper, not a complete Node install).

## 0c. Confirm Aspire CLI is invokable

```bash
command -v aspire || ls "$HOME/.aspire/bin/aspire"
aspire --version
```

If `aspire` isn't on `PATH`, prepend the user-local install for this
session:

```bash
export PATH="$HOME/.aspire/bin:$PATH"
```

## 0d. Confirm the container runtime is reachable

```bash
docker info >/dev/null && echo OK
```

The rest of the skill is runtime-agnostic as long as `docker run`-style
flags (incl. `--add-host`) are accepted (Docker Engine, Docker Desktop,
Podman with the docker CLI shim, Rancher Desktop, OrbStack).

## 0e. Surface other Aspire AppHosts and any orphan DCP containers

```bash
aspire ps 2>&1 | head
docker ps \
  --filter "label=com.microsoft.developer.usvc-dev.persistent=false" \
  --format 'table {{.Names}}\t{{.Ports}}'
```

- If `aspire ps` shows another project's AppHost, pause and ask
  before stopping it.
- If the orphan-container list contains entries bound to host ports
  this skill uses (`8080`, `9901`, `4200`), scan the names with the
  user before running the cleanup in Step 9b.

## 0f. Confirm no orphan processes from a prior run

All ports are Aspire-assigned, so there is no fixed list to check.
Instead, verify that the DCP container cleanup (Step 0e) found no
orphans and that `aspire ps` reports no running AppHost.

## 0g. Pre-commit the env for Aspire's children (principle 3)

When you later run `aspire start`, every child process inherits the
env at that moment. Re-source the version manager at the top of
every later command that invokes Aspire — see Step 8.
