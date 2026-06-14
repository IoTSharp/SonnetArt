#!/bin/sh
set -eu

: "${SONNET_ART_AI_UPSTREAM_URL:=https://sonnet.vip}"
: "${SONNET_ART_ACCOUNT_UPSTREAM_URL:=https://sonnet.vip}"
export SONNET_ART_AI_UPSTREAM_URL
export SONNET_ART_ACCOUNT_UPSTREAM_URL

exec caddy run --config /etc/caddy/Caddyfile.template --adapter caddyfile
