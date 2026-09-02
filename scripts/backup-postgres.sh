#!/usr/bin/env bash
set -euo pipefail

: "${DEVORCHESTRATOR_PG_URL:?set DEVORCHESTRATOR_PG_URL to a PostgreSQL URI}"
BACKUP_DIR="${BACKUP_DIR:-./backups}"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
OUT="${1:-${BACKUP_DIR}/devorchestrator-${STAMP}.dump}"
mkdir -p "$(dirname "$OUT")"

network_args=()
if [[ -n "${DEVORCHESTRATOR_DOCKER_NETWORK:-}" ]]; then
  network_args=(--network "$DEVORCHESTRATOR_DOCKER_NETWORK")
fi

docker run --rm "${network_args[@]}" \
  -e DATABASE_URL="$DEVORCHESTRATOR_PG_URL" \
  -v "$(cd "$(dirname "$OUT")" && pwd):/backup" \
  postgres:17-alpine \
  sh -c 'pg_dump --format=custom --no-owner --no-acl "$DATABASE_URL" -f "/backup/'"$(basename "$OUT")"'"'

echo "$OUT"
