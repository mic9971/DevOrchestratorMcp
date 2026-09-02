#!/usr/bin/env bash
set -euo pipefail

: "${DEVORCHESTRATOR_RESTORE_DATABASE_URL:?set DEVORCHESTRATOR_RESTORE_DATABASE_URL}"
BACKUP_FILE="${1:?usage: restore-postgres.sh <backup.dump>}"
BACKUP_FILE="$(cd "$(dirname "$BACKUP_FILE")" && pwd)/$(basename "$BACKUP_FILE")"

docker run --rm \
  -e DATABASE_URL="$DEVORCHESTRATOR_RESTORE_DATABASE_URL" \
  -v "$(dirname "$BACKUP_FILE"):/backup:ro" \
  postgres:17-alpine \
  sh -c 'pg_restore --clean --if-exists --no-owner --no-acl -d "$DATABASE_URL" "/backup/'"$(basename "$BACKUP_FILE")"'"'

echo "restore completed: $(basename "$BACKUP_FILE")"
