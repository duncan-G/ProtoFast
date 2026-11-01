# The clients image: the four Angular SSR apps and the Node host that serves them
# (host/server.mjs). The clients component runs it with CLIENTS=admin,protofast,theplot, and
# clients-admin-theplot with CLIENTS=admin-theplot. ProtoStrap's deploy workflow builds it for
# linux/arm64.
#
# The build stages run on the builder's own platform ($BUILDPLATFORM, set by BuildKit): what
# they make, JavaScript bundles and pure-JavaScript packages, is the same on every platform, so
# an arm64 build doesn't run npm or Angular under emulation. Each app is its own npm project and
# its own stage, so BuildKit builds them side by side. The gRPC code each app generates from the
# protos under services/ is committed (src/lib/gen), so the build context is clients/ alone.

FROM --platform=$BUILDPLATFORM node:24@sha256:e7add2a490bb17d97b485b8641bd88455fff7839ade0f5eba493c3508124d413 AS admin
WORKDIR /src/admin
COPY admin/package.json admin/package-lock.json ./
RUN npm ci --no-audit --no-fund
# src/admin-kit is a symlink to ../../admin-kit/src.
COPY admin-kit /src/admin-kit
COPY admin ./
RUN npm run build

FROM --platform=$BUILDPLATFORM node:24@sha256:e7add2a490bb17d97b485b8641bd88455fff7839ade0f5eba493c3508124d413 AS admin-theplot
WORKDIR /src/admin-theplot
COPY admin-theplot/package.json admin-theplot/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY admin-kit /src/admin-kit
COPY admin-theplot ./
RUN npm run build

FROM --platform=$BUILDPLATFORM node:24@sha256:e7add2a490bb17d97b485b8641bd88455fff7839ade0f5eba493c3508124d413 AS protofast
WORKDIR /src/protofast
COPY protofast/package.json protofast/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY protofast ./
RUN npm run build

FROM --platform=$BUILDPLATFORM node:24@sha256:e7add2a490bb17d97b485b8641bd88455fff7839ade0f5eba493c3508124d413 AS theplot
WORKDIR /src/theplot
COPY theplot/package.json theplot/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY theplot ./
RUN npm run build

# The host's own packages, OpenTelemetry's, which it loads when OTEL_EXPORTER_OTLP_ENDPOINT is
# set. The apps' server bundles are self-contained: they need no node_modules of their own.
FROM --platform=$BUILDPLATFORM node:24@sha256:e7add2a490bb17d97b485b8641bd88455fff7839ade0f5eba493c3508124d413 AS host
WORKDIR /src/host
COPY host/package.json host/package-lock.json ./
RUN npm ci --omit=dev --no-audit --no-fund

FROM node:24-slim@sha256:c86ecead9e55a5210ac1727de38a4ca840bf03dcaf34dc9749addbf383b1920c
ENV NODE_ENV=production
WORKDIR /app
COPY --from=host /src/host/node_modules ./host/node_modules
# Each app's bundles where its `npm run build` leaves them, so the host finds them at
# ../<name>/dist/<name>/ here as it does in clients/.
COPY --from=admin /src/admin/dist/admin ./admin/dist/admin
COPY --from=admin-theplot /src/admin-theplot/dist/admin-theplot ./admin-theplot/dist/admin-theplot
COPY --from=protofast /src/protofast/dist/protofast ./protofast/dist/protofast
COPY --from=theplot /src/theplot/dist/theplot ./theplot/dist/theplot
COPY host/server.mjs host/host.mjs host/telemetry.mjs ./host/
# The files stay root's; the server runs as node, which can read them but not change them.
USER node
EXPOSE 4000
CMD ["node", "host/server.mjs"]
