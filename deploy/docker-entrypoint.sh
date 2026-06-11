#!/bin/sh
set -eu

: "${SONNET_ART_AI_UPSTREAM_URL:?set SONNET_ART_AI_UPSTREAM_URL}"
: "${SONNET_ART_ACCOUNT_UPSTREAM_URL:?set SONNET_ART_ACCOUNT_UPSTREAM_URL}"

exec caddy run --config /etc/caddy/Caddyfile.template --adapter caddyfile
