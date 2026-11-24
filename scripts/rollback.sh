#!/usr/bin/env bash
# Put the PREVIOUS release back after a bad deploy. Run on the VPS:
#
#   cd /data/apps/zwecosystem && bash scripts/rollback.sh
#
# Every deploy first tags the images that are running as ":previous". This script makes those the running ones again
# (backend + website). It does NOT touch the database or uploaded images.
#
# Database note: schema changes are additive ("CREATE ... IF NOT EXISTS"), so the older code normally runs fine on the
# newer database. If a release changed or deleted data and the old code cannot cope, restore the pre-deploy backup instead:
#   bash scripts/restore-db.sh /data/backups/pre-deploy-YYYYMMDD-HHMMSS.sql.gz --live

set -eu
cd "$(dirname "$0")/.." 2>/dev/null || true
[ -f docker-compose.yml ] || { echo "Run this from /data/apps/zwecosystem (docker-compose.yml not found)."; exit 1; }

REPO=$(grep -m1 -o 'ghcr.io/[^/]*/[^/]*/backend' docker-compose.yml | sed 's#/backend$##')
[ -n "$REPO" ] || { echo "Could not find the image names in docker-compose.yml."; exit 1; }

for svc in backend admin-dashboard; do
  docker image inspect "$REPO/$svc:previous" >/dev/null 2>&1 || { echo "No ':previous' image for $svc. Nothing to roll back to (first deploy?)."; exit 1; }
done

echo "Rolling back to the previous release..."
for svc in backend admin-dashboard; do
  docker tag "$REPO/$svc:latest" "$REPO/$svc:rolled-back-from" 2>/dev/null || true   # keep the bad one for a post-mortem
  docker tag "$REPO/$svc:previous" "$REPO/$svc:latest"
done
# --no-deps: leave the database container alone. No pull: use the local tags we just set.
docker compose up -d --no-deps --force-recreate backend admin-dashboard

echo "Waiting for the API..."
for i in $(seq 1 24); do
  if curl -fsS -m 5 http://127.0.0.1:5500/api/health >/dev/null 2>&1; then echo "API is healthy again."; ok=1; break; fi
  sleep 5
done
[ "${ok:-0}" = "1" ] || { echo "The API did not come back within 2 minutes. Check: docker logs --tail 100 zwecosystem-api"; exit 1; }

echo "Done. The bad release is still available locally as ':rolled-back-from' for inspection."
echo "Next: fix the problem, push again (the deploy will replace ':latest'), and tell your shops if anything was lost."
