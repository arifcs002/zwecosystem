#!/usr/bin/env bash
# Restore a database backup. Run on the VPS.
#
#   bash scripts/restore-db.sh /data/backups/pre-deploy-20261124-100000.sql.gz            # DRILL (safe, default)
#   bash scripts/restore-db.sh /data/backups/pre-deploy-20261124-100000.sql.gz --live     # REPLACE the live database
#
# DRILL: loads the backup into a throw-away database next to the real one, prints how many rows the main tables have, and
# deletes it. It proves the backup file really works WITHOUT touching live data. Do this once a month.
#
# LIVE: takes a fresh safety dump first, stops the API, replaces the live database with the backup, starts the API and
# waits for it to be healthy. Everything saved after the backup was made is lost. You must type RESTORE to continue.

set -eu
FILE="${1:-}"; MODE="${2:-drill}"
PG=zwecosystem-postgres
[ -n "$FILE" ] && [ -f "$FILE" ] || { echo "usage: $0 <backup.sql.gz> [--live]"; exit 2; }
gzip -t "$FILE" || { echo "This backup file is corrupt (gzip test failed). Pick another one."; exit 1; }
docker ps --format '{{.Names}}' | grep -qx "$PG" || { echo "The postgres container ($PG) is not running."; exit 1; }

psql_in() { docker exec -i "$PG" sh -c 'psql -v ON_ERROR_STOP=1 -q -U "$POSTGRES_USER" -d "$1"' _ "$@"; }
DB=$(docker exec "$PG" sh -c 'printf %s "$POSTGRES_DB"')
TABLES="companies users products orders order_items credit_ledger"

if [ "$MODE" != "--live" ]; then
  SCRATCH="restore_drill_$(date +%s)"
  echo "DRILL: restoring into the scratch database $SCRATCH ..."
  echo "CREATE DATABASE $SCRATCH;" | psql_in postgres
  trap 'echo "DROP DATABASE IF EXISTS $SCRATCH WITH (FORCE);" | psql_in postgres >/dev/null 2>&1 || true' EXIT
  gunzip -c "$FILE" | psql_in "$SCRATCH" >/dev/null
  echo "Backup loaded. Row counts in the restored copy:"
  for t in $TABLES; do
    n=$(echo "SELECT COUNT(*) FROM $t;" | docker exec -i "$PG" sh -c "psql -t -A -U \"\$POSTGRES_USER\" -d $SCRATCH" 2>/dev/null || echo "missing")
    printf '  %-16s %s\n' "$t" "$n"
  done
  echo "OK: this backup restores cleanly. The scratch database is being deleted; live data was not touched."
  exit 0
fi

echo "!!! LIVE RESTORE of $FILE into database '$DB'."
echo "!!! Everything saved after this backup was made will be LOST."
printf 'Type RESTORE to continue: '; read -r answer
[ "$answer" = "RESTORE" ] || { echo "Cancelled."; exit 1; }

cd /data/apps/zwecosystem
SAFETY="/data/backups/before-restore-$(date +%Y%m%d-%H%M%S).sql.gz"
echo "Saving a safety copy of the CURRENT database first: $SAFETY"
docker exec "$PG" sh -c 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" --no-owner' | gzip > "$SAFETY"
gzip -t "$SAFETY" && [ -s "$SAFETY" ] || { echo "Could not make the safety copy. Stopping; nothing was changed."; exit 1; }

echo "Stopping the API..."
docker compose stop backend
echo "DROP DATABASE IF EXISTS \"$DB\" WITH (FORCE);" | psql_in postgres
echo "CREATE DATABASE \"$DB\";" | psql_in postgres
echo "Loading the backup..."
gunzip -c "$FILE" | psql_in "$DB" >/dev/null
echo "Starting the API..."
docker compose start backend

for i in $(seq 1 24); do
  if curl -fsS -m 5 http://127.0.0.1:5500/api/health >/dev/null 2>&1; then echo "Restored. The API is healthy. Safety copy of the replaced data: $SAFETY"; exit 0; fi
  sleep 5
done
echo "The API did not become healthy. The replaced data is in $SAFETY. Check: docker logs --tail 100 zwecosystem-api"
exit 1
