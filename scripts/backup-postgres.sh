#!/usr/bin/env bash
set -euo pipefail

: "${DEVORCHESTRATOR_DATABASE_URL:?set DEVORCHESTRATOR_DATABASE_URL}"
BACKUP_DIR="${BACKUP_DIR:-./backups}"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
OUT="${1:-${BACKUP_DIR}/devorchestrator-${STAMP}.dump}"
mkdir -p "$(dirname "$OUT")"

docker run --rm \
  -e DATABASE_URL="$DEVORCHESTRATOR_DATABASE_URL" \
  -v "$(cd "$(dirname "$OUT")" && pwd):/backup" \
  postgres:17-alpine \
  sh -c 'pg_dump --format=custom --no-owner --no-acl "$DATABASE_URL" -f "/backup/'"$(basename "$OUT")"'"'

echo "$OUT"
