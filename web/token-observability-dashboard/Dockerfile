FROM mcr.microsoft.com/devcontainers/javascript-node:1-22-bookworm AS build
WORKDIR /app

COPY web/token-observability-dashboard/package.json web/token-observability-dashboard/package-lock.json ./
RUN npm ci

COPY web/token-observability-dashboard/ ./
RUN npm run build

FROM nginxinc/nginx-unprivileged:stable-alpine AS runtime
USER root
COPY web/token-observability-dashboard/nginx.conf /etc/nginx/conf.d/default.conf
COPY web/token-observability-dashboard/docker-entrypoint.sh /docker-entrypoint.d/90-tokenobs-runtime-config.sh
COPY --from=build /app/dist /usr/share/nginx/html
RUN chmod +x /docker-entrypoint.d/90-tokenobs-runtime-config.sh \
    && printf 'ok\n' > /usr/share/nginx/html/healthz \
    && chown -R 101:0 /usr/share/nginx/html /docker-entrypoint.d/90-tokenobs-runtime-config.sh
EXPOSE 8080
USER 101
