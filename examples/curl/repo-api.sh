#!/usr/bin/env bash
# Generic JSON requests. No automatic retries; importing is not supported.
set -euo pipefail
: "${REPO_API_TOKEN:?Set REPO_API_TOKEN from AuthenticationToken in your BepInEx config}"
REPO_API_BASE="${REPO_API_URL:-http://127.0.0.1:17453}"
if (( $# < 2 || $# > 4 )); then
  printf 'Usage: bash repo-api.sh GET|POST /path [JSON-or-@file] [idempotency-key]\n' >&2
  exit 1
fi
REPO_API_METHOD="$1"
REPO_API_PATH="$2"
if [[ "$REPO_API_PATH" != /* || "$REPO_API_PATH" == //* ]]; then
  printf 'Use an API path starting with one slash.\n' >&2; exit 1
fi
REPO_API_ARGS=(--silent --show-error --fail-with-body --max-time 15
  -H "Authorization: Bearer $REPO_API_TOKEN")
case "$REPO_API_METHOD" in
  GET) ;;
  POST)
    REPO_API_KEY="${4:-}"
    if [[ -z "$REPO_API_KEY" ]]; then
      if [[ -r /proc/sys/kernel/random/uuid ]]; then
        REPO_API_KEY="$(cat /proc/sys/kernel/random/uuid)"
      else
        REPO_API_KEY="$(uuidgen)"
      fi
    fi
    printf 'Idempotency-Key: %s\n' "$REPO_API_KEY" >&2
    REPO_API_BODY="${3:-}"
    if [[ -z "$REPO_API_BODY" ]]; then REPO_API_BODY='{}'; fi
    REPO_API_ARGS+=(-H 'Content-Type: application/json' -H "Idempotency-Key: $REPO_API_KEY"
      --data-binary "$REPO_API_BODY")
    ;;
  *) printf 'Only GET and POST are supported.\n' >&2; exit 1 ;;
esac
curl "${REPO_API_ARGS[@]}" -X "$REPO_API_METHOD" "${REPO_API_BASE%/}$REPO_API_PATH"
printf '\n'
