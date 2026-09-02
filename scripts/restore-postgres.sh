#!/usr/bin/env bash
set -euo pipefail

: "${DEVORCHESTRATOR_RESTORE_PG_URL:?set DEVORCHESTRATOR_RESTORE_PG_URL to a PostgreSQL URI}"
BACKUP_FILE="${1:?usage: restore-postgres.sh <backup.dump>}"
BACKUP_FILE="$(cd "$(dirname "$BACKUP_FILE")" && pwd)/$(basename "$BACKUP_FILE")"

network_args=()
if [[ -n "${DEVORCHESTRATOR_DOCKER_NETWORK:-}" ]]; then
  network_args=(--network "$DEVORCHESTRATOR_DOCKER_NETWORK")
fi

docker run --rm "${network_args[@]}" \
  -e DATABASE_URL="$DEVORCHESTRATOR_RESTORE_PG_URL" \
  -v "$(dirname "$BACKUP_FILE"):/backup:ro" \
  postgres:17-alpine \
  sh -c 'pg_restore --clean --if-exists --no-owner --no-acl -d "$DATABASE_URL" "/backup/'"$(basename "$BACKUP_FILE")"'"'

echo "restore completed: $(basename "$BACKUP_FILE")"
