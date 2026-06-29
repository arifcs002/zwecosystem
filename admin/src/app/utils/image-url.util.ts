import { environment } from '../../environments/environment';

const API_BASE = environment.apiUrl.replace('/api', '');

export function resolveImageUrl(url: string | undefined | null): string {
  if (!url) return '';
  // Already absolute URL
  if (url.startsWith('http://') || url.startsWith('https://')) return url;
  // Relative path — prepend backend base URL
  return `${API_BASE}${url.startsWith('/') ? '' : '/'}${url}`;
}
