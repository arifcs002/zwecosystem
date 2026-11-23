# HTTPS and off-server backups — what is ready and what is still needed

Nothing in this file has been run against the production VPS. Do it in a quiet hour and keep the SSH session open.

## 1. Off-server backup copy (ready, switched off)

Every deploy already writes a database dump and an uploads archive to `/data/backups` on the VPS (newest 14 / 7 kept).
That protects against a bad deploy but **not** against losing the server. The deploy now also copies them elsewhere
**when you configure a destination**:

1. Pick a destination you control (Google Drive, Backblaze B2, S3, a second server — anything [rclone](https://rclone.org/) supports).
2. On the VPS: `curl https://rclone.org/install.sh | sudo bash`, then `rclone config` and create a remote, e.g. `offsite`.
3. Test it by hand: `rclone copy /data/backups offsite:auleco-backups --max-age 2d`.
4. Add one line to `/data/apps/zwecosystem/.env`: `BACKUP_REMOTE=offsite:auleco-backups`.

From the next deploy on, the newest backups are copied there. A failed copy only prints a warning.

**Restore** (database): `gunzip -c pre-deploy-YYYYMMDD-HHMMSS.sql.gz | docker exec -i zwecosystem-postgres sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB"'`
(restore into an empty database; try it once on a scratch database *before* you need it).
**Restore** (images): `tar xzf uploads-YYYYMMDD-HHMMSS.tar.gz -C /data/apps`.

Backups only happen on deploy. If the shop changes a lot between deploys, also add a nightly cron that runs the same `pg_dump`.

## 2. HTTPS (needs your decision)

Today the site is plain HTTP on port 80. Logins and shopper details travel unencrypted, and browsers mark it "Not secure".
Shop addresses are `<shop>.auleco.com`, so the certificate has to cover every subdomain. Two workable ways:

### Option A — wildcard certificate with certbot (DNS-01)
* One certificate `*.auleco.com` + `auleco.com`, renewed automatically.
* **Needs:** your DNS provider (e.g. Cloudflare, Namecheap, GoDaddy) and an API token for it, so certbot can prove ownership.
* Then: nginx gets a `listen 443 ssl` server using that certificate, port 80 redirects to 443, and `Strict-Transport-Security` is added.
* Works with the current nginx; no new components.

### Option B — Caddy with automatic per-shop certificates
* Caddy requests a certificate for each shop the first time someone visits it; nothing to renew by hand.
* **Needs:** no DNS API, but port 80 and 443 open and the DNS `*.auleco.com` record pointing at the VPS. Adds a small API endpoint so Caddy only
  issues certificates for shops that really exist. It replaces nginx as the front door, so it is a bigger change.

### Either way, after HTTPS works
1. Change `apiUrl` in `admin/src/environments/environment.ts` from `http://auleco.com/api` to `https://auleco.com/api`.
2. Add `Strict-Transport-Security: max-age=31536000` (only once you are sure HTTPS works everywhere — browsers remember it).
3. Rebuild the mobile app (it bundles `apiUrl`).
4. Tell Facebook/Google about the `https://` shop addresses (Meta Pixel and Search Console treat http and https as different sites).

**What I need from you to do this:** which option, your DNS provider, and a time window.
