// Port 5500 (Kestrel) is only reliably reachable from networks the hosting
// firewall happens to allow through — mobile data / other networks get
// "0 Unknown Error" trying to hit it directly. Port 85 (nginx) is the one
// public entrypoint; nginx already proxies /api/ and /uploads/ to the
// backend, so every client (web, and the packaged app) should go through it.
export const environment = {
  production: true,
  apiUrl: 'http://194.5.152.74:85/api',
  siteUrl: 'http://194.5.152.74:85'
};
