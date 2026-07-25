// Port 5500 (Kestrel) is only reliably reachable from networks the hosting
// firewall happens to allow through — mobile data / other networks get
// "0 Unknown Error" trying to hit it directly. Port 85/443 (nginx) is the one
// public entrypoint; nginx already proxies /api/ and /uploads/ to the
// backend, so every client (web, and the packaged app) should go through it.
//
// A single fixed absolute origin works for every company subdomain too
// (zairasworld.auleco.com calling auleco.com/api) since the backend's CORS
// policy is AllowAnyOrigin — no per-subdomain API host needed.
//
// http (not https): the server has no SSL certificate yet. The native app
// is also deliberately pinned to plain HTTP (see capacitor.config.ts) to
// avoid WebView mixed-content blocks against a http-only backend — flipping
// this to https requires provisioning a wildcard TLS cert on the server AND
// updating capacitor.config.ts's androidScheme together, not in isolation.
//
// :85 (not the default :80): port 80 on the server is already occupied by
// something else outside this project's docker-compose — claiming it
// crashed the whole admin-dashboard container. Until that's freed up (or
// a host-level reverse proxy is set up to forward 80 -> 85), every URL
// needs the explicit :85.
export const environment = {
  production: true,
  apiUrl: 'http://auleco.com:85/api',
  siteUrl: 'http://auleco.com:85',
  rootDomain: 'auleco.com:85'
};
