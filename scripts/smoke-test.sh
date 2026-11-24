#!/usr/bin/env bash
# Quick "is the site really working?" check, run right after a deploy (or any time).
#
#   scripts/smoke-test.sh https://auleco.com zairasworld        # full check through nginx
#   scripts/smoke-test.sh http://localhost:5500 --api-only        # only the API (no nginx in front)
#
# Needs only bash + curl (Git Bash on Windows works). Read-only: it never logs in, never writes data.
# Exit code 0 = everything passed, 1 = at least one check failed.

set -u
BASE="${1:-}"; SHOP="${2:-}"; API_ONLY=0
[ -z "$BASE" ] && { echo "usage: $0 <base-url> [shop-subdomain | --api-only]"; exit 2; }
[ "$SHOP" = "--api-only" ] && { API_ONLY=1; SHOP=""; }
BASE="${BASE%/}"
PASS=0; FAIL=0; WARN=0

ok()   { echo "  ok    $1"; PASS=$((PASS+1)); }
bad()  { echo "  FAIL  $1"; FAIL=$((FAIL+1)); }
warn() { echo "  warn  $1"; WARN=$((WARN+1)); }
code() { curl -s -o /dev/null -m 15 -w '%{http_code}' "$@" 2>/dev/null; }
hdrs() { curl -s -o /dev/null -D - -m 15 "$@" 2>/dev/null | tr -d '\r'; }

echo "Smoke test: $BASE ${SHOP:+(shop: $SHOP)}"

# --- 1. the API is up and can reach its database (retry: a fresh deploy needs a few seconds to start) ---
echo "API"
status=000
for i in $(seq 1 12); do
  status=$(code "$BASE/api/health")
  [ "$status" = "200" ] && break
  sleep 5
done
[ "$status" = "200" ] && ok "/api/health answers 200 (database reachable)" || bad "/api/health answered $status after 60s"
body=$(curl -s -m 15 "$BASE/api/health" 2>/dev/null)
echo "$body" | grep -q '"status":"ok"' && ok "health body says ok" || bad "health body is not ok: $body"

[ "$(code "$BASE/api/subscriptionplans/public")" = "200" ] && ok "public price list loads" || bad "public price list does not load"
[ "$(code "$BASE/api/expenses")" = "401" ] && ok "a private endpoint refuses anonymous access (401)" || bad "/api/expenses did not answer 401 without a login"
[ "$(code "$BASE/api/reports/export/sales")" = "401" ] && ok "report exports refuse anonymous access" || bad "report export is open without login"
[ "$(code "$BASE/api/users")" = "401" ] && ok "user list refuses anonymous access" || bad "/api/users is open without login"

# CORS: a random website must not be allowed to call the API from a browser
h=$(hdrs -X OPTIONS -H "Origin: https://evil.example" -H "Access-Control-Request-Method: GET" "$BASE/api/subscriptionplans/public")
echo "$h" | grep -qi "access-control-allow-origin: https://evil.example" && bad "CORS allows an arbitrary origin" || ok "CORS does not allow an arbitrary origin"

# security headers on the API
h=$(hdrs "$BASE/api/health")
echo "$h" | grep -qi "x-content-type-options: nosniff" && ok "API sends X-Content-Type-Options" || bad "API is missing X-Content-Type-Options"
echo "$h" | grep -qi "x-frame-options" && ok "API sends X-Frame-Options" || bad "API is missing X-Frame-Options"

[ "$API_ONLY" = "1" ] && { echo; echo "passed $PASS, failed $FAIL, warnings $WARN (API only)"; [ "$FAIL" -eq 0 ]; exit $?; }

# --- 2. the web front door (nginx + the built app) ---
echo "Web"
[ "$(code "$BASE/")" = "200" ] && ok "home page loads" || bad "home page does not load"
page=$(curl -s -m 15 "$BASE/" 2>/dev/null)
echo "$page" | grep -qi "<app-root" && ok "the app shell is served" || bad "the app shell is missing from the home page"

h=$(hdrs "$BASE/")
echo "$h" | grep -qi "x-frame-options: deny" && ok "page is not frameable (X-Frame-Options)" || bad "X-Frame-Options missing on the page"
echo "$h" | grep -qi "content-security-policy" && ok "Content-Security-Policy header present" || warn "no Content-Security-Policy header"
echo "$h" | grep -qi "cache-control: no-cache" && ok "the page shell is not cached (new releases show at once)" || warn "index.html is not sent with no-cache"

# a build file with a content hash must be cached for a long time and compressed
js=$(echo "$page" | grep -o 'main\.[0-9a-f]\{16\}\.js' | head -1)
if [ -n "$js" ]; then
  h=$(hdrs -H "Accept-Encoding: gzip" "$BASE/$js")
  echo "$h" | grep -qi "content-encoding: gzip" && ok "JavaScript is gzip-compressed" || warn "JavaScript is not compressed"
  echo "$h" | grep -qi "immutable" && ok "hashed JavaScript is cached for a year" || warn "hashed JavaScript is not marked immutable"
else
  warn "could not find the main JavaScript file name"
fi

# crawlers (Facebook/WhatsApp previews) get a server-rendered page; humans get the app
[ "$(code "$BASE/robots.txt")" = "200" ] && ok "robots.txt is served" || bad "robots.txt is missing"
[ "$(code "$BASE/sitemap.xml")" = "200" ] && ok "sitemap.xml is served" || warn "sitemap.xml is not served"
[ "$(code -X POST -H 'Content-Type: application/csp-report' -d '{}' "$BASE/api/csp-report")" = "204" ] && ok "CSP report collector accepts reports" || warn "CSP report collector did not answer 204"

# --- 3. one shop, end to end (public, read-only) ---
if [ -n "$SHOP" ]; then
  echo "Shop $SHOP"
  [ "$(code "$BASE/api/storefront/$SHOP")" = "200" ] && ok "storefront data loads" || bad "storefront data does not load for '$SHOP'"
  [ "$(code "$BASE/api/tenant/$SHOP")" = "200" ] && ok "tenant config loads" || bad "tenant config does not load for '$SHOP'"
  crawl=$(curl -s -m 15 -A "facebookexternalhit/1.1" "$BASE/" 2>/dev/null)
  echo "$crawl" | grep -qi "og:title" && ok "link-preview bots get Open Graph tags" || warn "no Open Graph tags for link-preview bots on the root domain"
fi

# --- 4. HTTPS, when the address is https ---
case "$BASE" in
  https://*)
    echo "HTTPS"
    h=$(hdrs "$BASE/")
    echo "$h" | grep -qi "strict-transport-security" && ok "HSTS header present" || warn "no Strict-Transport-Security header yet"
    plain="http://${BASE#https://}"
    loc=$(hdrs "$plain/" | grep -i '^location:' | head -1)
    echo "$loc" | grep -qi "https://" && ok "plain http redirects to https" || warn "plain http does not redirect to https"
    ;;
  *) warn "address is not https: logins travel unencrypted" ;;
esac

echo
echo "passed $PASS, failed $FAIL, warnings $WARN"
[ "$FAIL" -eq 0 ]
