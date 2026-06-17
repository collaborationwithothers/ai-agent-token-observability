FROM mcr.microsoft.com/devcontainers/javascript-node:1-22-bookworm AS build
WORKDIR /app

COPY web/token-observability-dashboard/package.json web/token-observability-dashboard/package-lock.json ./
RUN npm ci

COPY web/token-observability-dashboard/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine AS runtime
WORKDIR /site
COPY --from=build /app/dist .
RUN printf 'ok\n' > /site/healthz
EXPOSE 8080
USER app
ENTRYPOINT ["busybox", "httpd", "-f", "-p", "8080", "-h", "/site"]
