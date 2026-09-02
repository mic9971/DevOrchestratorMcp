#!/usr/bin/env bash
set -euo pipefail

: "${POSTGRES_PASSWORD:?set POSTGRES_PASSWORD}"
: "${DEVORCHESTRATOR_ARCHITECT_KEY:?set DEVORCHESTRATOR_ARCHITECT_KEY}"
: "${DEVORCHESTRATOR_IMPLEMENTER_KEY:?set DEVORCHESTRATOR_IMPLEMENTER_KEY}"
: "${DEVORCHESTRATOR_AUDITOR_KEY:?set DEVORCHESTRATOR_AUDITOR_KEY}"
: "${GITHUB_WEBHOOK_SECRET:?set GITHUB_WEBHOOK_SECRET}"

export COMPOSE_PROJECT_NAME="${COMPOSE_PROJECT_NAME:-devorchestrator-proof}"
BASE_URL="${DEVORCHESTRATOR_BASE_URL:-http://127.0.0.1:5058}"

cleanup() {
  docker compose -f compose.yaml down -v --remove-orphans >/dev/null 2>&1 || true
}
trap cleanup EXIT

wait_url() {
  local url="$1"
  local attempts="${2:-60}"
  for ((i=1; i<=attempts; i++)); do
    if curl --fail --silent "$url" >/dev/null; then
      return 0
    fi
    sleep 2
  done
  echo "timed out waiting for $url" >&2
  docker compose -f compose.yaml logs dev-orchestrator >&2 || true
  return 1
}

echo "[proof] booting production-like stack"
docker compose -f compose.yaml up --build -d
wait_url "$BASE_URL/healthz"
wait_url "$BASE_URL/readyz"

echo "[proof] verifying operational and control-plane auth"
status_code="$(curl -sS -o /dev/null -w '%{http_code}' "$BASE_URL/metrics")"
[[ "$status_code" == "401" ]]

control_api_code="$(curl -sS -o /dev/null -w '%{http_code}' "$BASE_URL/control/api/dashboard")"
[[ "$control_api_code" == "401" ]]

curl --fail --silent "$BASE_URL/control/index.html" | grep -q 'DevOrchestrator Control Plane'

curl --fail --silent \
  -H "X-DevOrchestrator-Key: $DEVORCHESTRATOR_AUDITOR_KEY" \
  "$BASE_URL/control/api/dashboard" | grep -q '"projects"'

curl --fail --silent \
  -H "X-DevOrchestrator-Key: $DEVORCHESTRATOR_AUDITOR_KEY" \
  "$BASE_URL/ops/status" | grep -q '"status":"ok"'

curl --fail --silent \
  -H "X-DevOrchestrator-Key: $DEVORCHESTRATOR_AUDITOR_KEY" \
  "$BASE_URL/metrics" | grep -q '^devorchestrator_active_workers '

echo "[proof] verifying restart recovery"
docker compose -f compose.yaml restart dev-orchestrator
wait_url "$BASE_URL/readyz"

echo "[proof] running PostgreSQL backup/restore drill"
mkdir -p backups
export DEVORCHESTRATOR_DOCKER_NETWORK="${COMPOSE_PROJECT_NAME}_default"
export DEVORCHESTRATOR_PG_URL="postgresql://devorchestrator:${POSTGRES_PASSWORD}@postgres:5432/devorchestrator"
BACKUP_FILE="$(bash ./scripts/backup-postgres.sh ./backups/phase6-proof.dump)"

docker compose -f compose.yaml exec -T postgres \
  psql -U devorchestrator -d postgres -v ON_ERROR_STOP=1 \
  -c 'DROP DATABASE IF EXISTS devorchestrator_restore;' \
  -c 'CREATE DATABASE devorchestrator_restore;'

export DEVORCHESTRATOR_RESTORE_PG_URL="postgresql://devorchestrator:${POSTGRES_PASSWORD}@postgres:5432/devorchestrator_restore"
bash ./scripts/restore-postgres.sh "$BACKUP_FILE"

migration_count="$(docker compose -f compose.yaml exec -T postgres \
  psql -U devorchestrator -d devorchestrator_restore -tAc \
  'SELECT COUNT(*) FROM "__EFMigrationsHistory";' | tr -d '[:space:]')"

if [[ -z "$migration_count" || "$migration_count" -lt 3 ]]; then
  echo "restore verification failed: migration_count=$migration_count" >&2
  exit 1
fi

echo "[proof] PASS: runtime, control plane, auth, restart, backup and restore verified"
