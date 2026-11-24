#!/usr/bin/env bash
# Nightly database + uploads backup, with an optional copy to Google Drive (or any rclone remote).
# Deploys already make a backup, but a shop that sells all week between deploys needs one every night.
#
# Install (as the deploy user on the VPS, once):
#   crontab -e
#   30 2 * * * /data/apps/zwecosystem/scripts/nightly-backup.sh >> /data/backups/nightly.log 2>&1
#
# The off-server copy uses BACKUP_REMOTE from /data/apps/zwecosystem/.env, e.g. BACKUP_REMOTE=gdrive:auleco-backups
# (see docs/LAUNCH.md for the five-minute Google Drive setup).

set -eu
PG=zwecosystem-postgres
DIR=/data/backups
STAMP=$(date +%Y%m%d-%H%M%S)
mkdir -p "$DIR"

docker ps --format '{{.Names}}' | grep -qx "$PG" || { echo "$(date) postgres container not running - no backup made"; exit 1; }

DB_FILE="$DIR/nightly-$STAMP.sql.gz"
docker exec "$PG" sh -c 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" --no-owner' | gzip > "$DB_FILE"
if [ ! -s "$DB_FILE" ] || ! gzip -t "$DB_FILE"; then echo "$(date) DB backup failed or empty"; rm -f "$DB_FILE"; exit 1; fi
echo "$(date) DB backup OK: $DB_FILE ($(du -h "$DB_FILE" | cut -f1))"

if [ -n "$(ls -A /data/apps/uploads 2>/dev/null)" ]; then
  tar czf "$DIR/uploads-$STAMP.tar.gz" -C /data/apps uploads
  echo "$(date) uploads archived"
fi

# keep the newest 14 nightly DB backups and 7 upload archives on the server
ls -1t "$DIR"/nightly-*.sql.gz 2>/dev/null | tail -n +15 | xargs -r rm -f
ls -1t "$DIR"/uploads-*.tar.gz 2>/dev/null | tail -n +8 | xargs -r rm -f

REMOTE=$(grep -E '^BACKUP_REMOTE=' /data/apps/zwecosystem/.env 2>/dev/null | head -n1 | cut -d= -f2- || true)
if [ -n "$REMOTE" ]; then
  if command -v rclone >/dev/null 2>&1; then
    if rclone copy "$DIR" "$REMOTE" --include "nightly-*.sql.gz" --include "pre-deploy-*.sql.gz" --include "uploads-*.tar.gz" --max-age 2d; then
      echo "$(date) copied off-server -> $REMOTE"
      # keep 30 days of backups in the cloud so it does not fill up
      rclone delete "$REMOTE" --min-age 30d --include "*.sql.gz" --include "*.tar.gz" || true
    else
      echo "$(date) WARNING: off-server copy FAILED (the local backup is safe)"
    fi
  else
    echo "$(date) WARNING: BACKUP_REMOTE is set but rclone is not installed"
  fi
else
  echo "$(date) note: BACKUP_REMOTE is not set - backups stay on this server only"
fi
