# Unified SSR host image. Build context is the REPO ROOT (so each client's
# `npm run build` can run buf codegen against ../../services/*/Protos).
#
# Per client there are two stages:
#   «client»-build — full install + ng build (SSR output in dist/)
#   «client»-deps  — production-only node_modules for the server bundle
# The final stage lays each client out as /app/«client»/{dist,node_modules}
# so the bundle's ESM imports resolve against its own dependency tree.
# The add-angular-client skill adds a stage pair + COPY lines per client.

# --- admin: build ---
FROM node:24-alpine AS admin-build
WORKDIR /repo/clients/admin
COPY clients/admin/package*.json ./
RUN npm ci
COPY services /repo/services
COPY clients/admin/ ./
RUN npm run build

# --- admin: prod deps ---
FROM node:24-alpine AS admin-deps
WORKDIR /deps
COPY clients/admin/package*.json ./
RUN npm ci --omit=dev

# --- protofast: build ---
FROM node:24-alpine AS protofast-build
WORKDIR /repo/clients/protofast
COPY clients/protofast/package*.json ./
RUN npm ci
COPY services /repo/services
COPY clients/protofast/ ./
RUN npm run build

# --- protofast: prod deps ---
FROM node:24-alpine AS protofast-deps
WORKDIR /deps
COPY clients/protofast/package*.json ./
RUN npm ci --omit=dev

# --- unified host ---
FROM node:24-alpine
WORKDIR /app
ENV NODE_ENV=production
COPY clients/host/package*.json ./
RUN npm ci --omit=dev
COPY clients/host/server.mjs ./

COPY --from=admin-build /repo/clients/admin/dist ./admin/dist
COPY --from=admin-deps /deps/node_modules ./admin/node_modules

COPY --from=protofast-build /repo/clients/protofast/dist ./protofast/dist
COPY --from=protofast-deps /deps/node_modules ./protofast/node_modules

EXPOSE 4000
CMD ["node", "server.mjs"]
