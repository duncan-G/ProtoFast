# Register a new Angular client with the unified SSR host

Envoy routing is handled entirely by `proxy.WithClient(...)` in
`apphost/Program.cs` (see `angular-setup.md` 2e) — the proxy's
entrypoint generates the client's listener (dev) or virtual host
(publish) from the `CLIENTS` env var. No Envoy template edits are
needed for a new client.

The unified SSR host (`clients/host/`) is **generic**: it bakes in no
client assets and has no per-client loader map. At runtime it reads the
`CLIENTS` env var, and for each name pulls that client's built assets
from S3 (`clients/<name>/<tag>/`) and imports
`/assets/<name>/server/server.mjs`. So registering a new client is
**not** a code edit to the host — it is a deploy-pipeline + config
change (docs/independent-deployment-plan.md §7).

You do **not** edit `clients/host/server.mjs` or `clients/host/Dockerfile`
anymore. Instead do the three things below.

---

## 1. Add a per-client deploy workflow

Copy an existing client workflow and change the identity. From
`.github/workflows/deploy-client-admin.yml`, create
`.github/workflows/deploy-client-«clientname».yml`:

```yaml
name: deploy-client-«clientname»

on:
  push:
    branches: [main]
    paths:
      - "clients/«clientname»/**"
      - "services/**/Protos/**"
      - ".github/workflows/deploy-client-«clientname».yml"
      - ".github/workflows/_component-deploy.yml"
  workflow_dispatch:
    inputs:
      tag:
        description: "Existing artifact tag to (re)deploy or roll back to. Blank = build current source."
        required: false
        type: string

concurrency:
  group: deploy-client-«clientname»
  cancel-in-progress: false

permissions:
  contents: read
  id-token: write

jobs:
  deploy:
    uses: ./.github/workflows/_component-deploy.yml
    with:
      component: client-«clientname»
      build: client-s3
      target: «clientname»
      kind: client
      hash_paths: "clients/«clientname» :(glob)services/**/Protos/**"
      project: clients/«clientname»
      tag: ${{ inputs.tag }}
    secrets: inherit
```

The workflow runs `ng build` and `aws s3 sync ./dist/«clientname»`
to `s3://<ASSETS_BUCKET>/clients/«clientname»/<tag>/`, then deploys via
SSM (`deploy.sh apply client-«clientname»=<tag>`).

## 2. Register the client in `CLIENTS`

The host (and Envoy) discover clients from the comma-separated `CLIENTS`
env var. Add `«clientname»` to it in the instance seed —
`infra/templates/user_data.sh.tftpl` (the `CLIENTS=` line) and the
`clients = "..."` value passed to the template in `infra/compute.tf`, plus
the matching `CLIENT_«CLIENTNAME»_DOMAIN` wiring for Envoy if the client
answers on its own subdomain.

For an already-running instance you do not have to re-provision: the first
`deploy.sh apply client-«clientname»=<tag>` self-registers the name in
`/opt/protofast/.env`'s `CLIENTS` and writes `CLIENT_«CLIENTNAME»_TAG` to
`versions.env`, then recreates the host so it pulls the new client.

## 3. (Manifest line — automatic)

`versions.env` on the instance gains a `CLIENT_«CLIENTNAME»_TAG` line the
first time the client deploys; there is nothing to hand-edit. The
clients-host compose service reads the whole `versions.env` via
`env_file`, so no compose edit is needed for the new tag variable either.

---

## Verify

```bash
dotnet build apphost
```

If the project was already running, restart with `aspire stop` then
`aspire start` (or `aspire run`). The new client's URL appears on the
envoy resource as the `«clientname»-web` endpoint.

To smoke-test the unified host locally, run the AppHost with the
`https-ssr-host` launch profile (sets `SsrHost__Dev=true`) and browse
the same per-client Envoy URLs. The host imports each client from
`$ASSETS_DIR/<name>/server/server.mjs`, so dev-host mode must point
`ASSETS_DIR` at a directory laid out as `<name>/{server,browser}/` per
client (each client's `ng build` output under `clients/<name>/dist/<name>/`).
