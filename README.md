# «projectname»

## Requirements

- Tooling:
  - Python 3
  - Node.js LTS (includes `npm` / `npx`)
  - Angular CLI
  - `uv` / `uvx`
  - Docker Engine
  - Aspire CLI (user-local under `~/.aspire/bin`)
- Skills
  - Angular agent skills `npx skills add https://github.com/angular/skills`

## Install

An idempotent setup script is provided to install any missing tooling. It is currently only tested on Ubuntu 24; on other distros/OSes you'll need to install the tools above manually.

```bash
bash scripts/setup-dev-dependencies.sh
```
