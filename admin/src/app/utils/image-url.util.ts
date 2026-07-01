import { environment } from '../../environments/environment';

// Static files (uploads, APKs) are served through nginx on siteUrl (port 85).
// This avoids exposing the backend port (5500) to end users.
const STATIC_BASE = (environment as any).siteUrl ?? environment.apiUrl.replace('/api', '');

export function resolveImageUrl(url: string | undefined | null): string {
  if (!url) return '';
  // Already absolute URL — return as-is (old records stored full http:// URLs)
  if (url.startsWith('http://') || url.startsWith('https://')) return url;
  // Relative path — prepend static file base (port 85 via nginx)
  return `${STATIC_BASE}${url.startsWith('/') ? '' : '/'}${url}`;
}
