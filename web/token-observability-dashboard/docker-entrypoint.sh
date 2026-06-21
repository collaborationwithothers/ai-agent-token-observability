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

readiness_origin=""
case "$api_base" in
  http://*|https://*)
    readiness_origin="${api_base%/api/v1}"
    ;;
esac

if [ -n "$readiness_origin" ]; then
  cat > /tmp/tokenobs-dashboard-readiness.conf <<EOF
  location = /readyz {
    proxy_ssl_server_name on;
    proxy_set_header Accept "application/json";
    proxy_pass ${readiness_origin}/readyz;
  }
EOF
else
  cat > /tmp/tokenobs-dashboard-readiness.conf <<'EOF'
  location = /readyz {
    default_type application/json;
    return 503 '{"service":"token-observability-dashboard","status":"not_ready","dependencies":[{"name":"product_api","status":"not_configured"}]}';
  }
EOF
fi
