#!/bin/sh
set -eu

api_base="${PRODUCT_API_BASE_URL:-}"

if [ -z "$api_base" ] && [ -n "${PRODUCT_API_PUBLIC_HOSTNAME:-}" ]; then
  api_base="https://${PRODUCT_API_PUBLIC_HOSTNAME}/api/v1"
fi

if [ -z "$api_base" ]; then
  api_base="/api/v1"
fi

escaped_api_base="$(printf '%s' "$api_base" | sed 's/[\\"]/\\&/g')"

cat > /usr/share/nginx/html/runtime-config.js <<EOF
window.__TOKENOBSERVABILITY_CONFIG__ = { "productApiBaseUrl": "${escaped_api_base}" };
EOF
