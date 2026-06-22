# Unified SSR host runtime image. Build context is THIS directory (clients/host)
# — no client assets are baked in. Each client is built and uploaded to S3 by
# its own workflow; the entrypoint pulls the pinned set on every (re)start
# (see entrypoint.sh + deploy/deploy.sh). The image therefore only needs the
# Express host (server.mjs), its own deps, and the AWS CLI to pull from S3.
#
# Debian slim (not alpine): the AWS CLI v2 has no musl build, and the SSR server
# bundles are self-contained (only Node built-ins are external), so no per-client
# node_modules are needed at runtime.
FROM node:24-slim

# AWS CLI v2 for the S3 sync in entrypoint.sh. TARGETARCH is set by buildx;
# map it to the CLI's aarch64/x86_64 asset names.
ARG TARGETARCH
RUN set -eux; \
    apt-get update; \
    apt-get install -y --no-install-recommends ca-certificates curl unzip; \
    case "${TARGETARCH}" in \
      arm64) cli_arch=aarch64 ;; \
      amd64) cli_arch=x86_64 ;; \
      *) echo "unsupported TARGETARCH=${TARGETARCH}" >&2; exit 1 ;; \
    esac; \
    curl -fsSL "https://awscli.amazonaws.com/awscli-exe-linux-${cli_arch}.zip" -o /tmp/awscliv2.zip; \
    unzip -q /tmp/awscliv2.zip -d /tmp; \
    /tmp/aws/install; \
    rm -rf /tmp/aws /tmp/awscliv2.zip; \
    apt-get purge -y --auto-remove unzip; \
    apt-get clean; \
    rm -rf /var/lib/apt/lists/*

WORKDIR /app
ENV NODE_ENV=production

COPY package*.json ./
RUN npm ci --omit=dev
COPY server.mjs ./
COPY entrypoint.sh /usr/local/bin/entrypoint.sh
RUN chmod +x /usr/local/bin/entrypoint.sh

EXPOSE 4000
ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
