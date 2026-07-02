// Client-side mirror of the backend product-code builder — used for live
// previews. Tokens: {PREFIX} {SEQ} {YYYY} {YY} {MM} {DD} {HH} {mm} {ss}
export function buildProductIdPreview(format: string, prefix: string, seq: number, pad: number): string {
  const now = new Date();
  const p2 = (n: number) => String(n).padStart(2, '0');
  return (format || '{PREFIX}{YY}{MM}-{SEQ}')
    .replace('{PREFIX}', prefix || '')
    .replace('{SEQ}', String(seq).padStart(pad || 1, '0'))
    .replace('{YYYY}', String(now.getFullYear()))
    .replace('{YY}', String(now.getFullYear()).slice(-2))
    .replace('{MM}', p2(now.getMonth() + 1))
    .replace('{DD}', p2(now.getDate()))
    .replace('{HH}', p2(now.getHours()))
    .replace('{mm}', p2(now.getMinutes()))
    .replace('{ss}', p2(now.getSeconds()));
}

// Secret price code: `map` is a 10-char string where index i is the character
// shown for digit i (e.g. "ABCDEFGHIJ" → 0=A,1=B,…). Non-digits pass through so
// a decimal like 1499.50 stays readable ("BEJJ.FA").
export function toSecretCode(value: number | string | null | undefined, map: string): string {
  if (value == null || !map || map.length < 10) return '';
  return String(value).split('').map(ch => {
    const d = ch.charCodeAt(0) - 48;
    return d >= 0 && d <= 9 ? map[d] : ch;
  }).join('');
}

export const DEFAULT_SECRET_MAP = 'ABCDEFGHIJ';
