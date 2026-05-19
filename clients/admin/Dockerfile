FROM node:22-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM node:22-alpine
WORKDIR /app
COPY --from=build /app/dist/admin ./dist/admin
COPY --from=build /app/package*.json ./
RUN npm ci --omit=dev
CMD ["node", "dist/admin/server/server.mjs"]
