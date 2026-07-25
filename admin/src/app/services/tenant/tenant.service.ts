import { Injectable } from '@angular/core';
import { ActivatedRoute } from '@angular/router';

// Root domains this app can be reached on. Any hostname that is exactly one
// of these (or "www."/"admin."/"api." + one of these) is the platform's own
// domain — not a company subdomain. Anything else under these roots
// (e.g. "zairasworld.auleco.com") is treated as that company's store slug.
//
// "localhost" is included so subdomain routing can be exercised in local dev
// without any DNS setup — browsers already resolve "*.localhost" to 127.0.0.1.
const ROOT_DOMAINS = ['auleco.com', 'localhost'];
const RESERVED_SUBDOMAINS = ['www', 'admin', 'api'];

@Injectable({ providedIn: 'root' })
export class TenantService {
  // Resolves the company slug from the current browser hostname, or null if
  // the app is being served from the platform's own domain (or an IP/host
  // that isn't one of ROOT_DOMAINS, e.g. hitting the server by raw IP —
  // that case falls back to the classic /:companySlug/... path routes).
  getSubdomainFromHost(): string | null {
    const host = window.location.hostname.toLowerCase();
    for (const root of ROOT_DOMAINS) {
      if (host === root) return null;
      if (host.endsWith('.' + root)) {
        const sub = host.slice(0, -(root.length + 1));
        if (!sub.includes('.') && !RESERVED_SUBDOMAINS.includes(sub)) {
          return sub;
        }
        return null;
      }
    }
    return null;
  }

  hasSubdomain(): boolean {
    return this.getSubdomainFromHost() !== null;
  }

  // True when the current host is one of ROOT_DOMAINS (bare or a subdomain
  // of it) — i.e. subdomain-based navigation is meaningful here. False for
  // raw IP access (e.g. the server's public IP without DNS), where the app
  // falls back to the legacy /:companySlug/... path routes.
  canUseSubdomains(): boolean {
    const host = window.location.hostname.toLowerCase();
    return ROOT_DOMAINS.some(root => host === root || host.endsWith('.' + root));
  }

  // Company slug for the current context: hostname subdomain takes priority,
  // falling back to the ":companySlug" route param (IP access / local dev
  // without a subdomain).
  getSlug(route: ActivatedRoute): string {
    return this.getSubdomainFromHost() || route.snapshot.paramMap.get('companySlug') || '';
  }

  // Builds router.navigate() segments for a tenant-scoped path. On a real
  // subdomain the slug is never part of the URL; otherwise it's prepended,
  // matching the legacy /:companySlug/... routes.
  routeSegments(slug: string, ...parts: (string | number)[]): any[] {
    return this.hasSubdomain() ? ['/', ...parts] : ['/', slug, ...parts];
  }

  // Full external URL to a company's own subdomain (used when the current
  // page is NOT already on that subdomain, e.g. the store-code lookup page
  // on the main domain needs to hard-navigate the browser there).
  externalTenantUrl(slug: string, path: string): string {
    const host = window.location.hostname.toLowerCase();
    const root = ROOT_DOMAINS.find(r => host === r || host.endsWith('.' + r)) || 'auleco.com';
    const protocol = window.location.protocol;
    const port = window.location.port ? `:${window.location.port}` : '';
    return `${protocol}//${slug}.${root}${port}/${path.replace(/^\/+/, '')}`;
  }
}
