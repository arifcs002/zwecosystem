// Dependency-free Code 39 barcode → SVG string. Code 39 is chosen over Code 128
// because it encodes our alphanumeric product IDs (A-Z 0-9 - . space $ / + %)
// with a simple, checksum-free pattern table — no npm lib, works fully offline.

const PATTERNS: Record<string, string> = {
  '0': 'nnnwwnwnn', '1': 'wnnwnnnnw', '2': 'nnwwnnnnw', '3': 'wnwwnnnnn', '4': 'nnnwwnnnw',
  '5': 'wnnwwnnnn', '6': 'nnwwwnnnn', '7': 'nnnwnnwnw', '8': 'wnnwnnwnn', '9': 'nnwwnnwnn',
  'A': 'wnnnnwnnw', 'B': 'nnwnnwnnw', 'C': 'wnwnnwnnn', 'D': 'nnnnwwnnw', 'E': 'wnnnwwnnn',
  'F': 'nnwnwwnnn', 'G': 'nnnnnwwnw', 'H': 'wnnnnwwnn', 'I': 'nnwnnwwnn', 'J': 'nnnnwwwnn',
  'K': 'wnnnnnnww', 'L': 'nnwnnnnww', 'M': 'wnwnnnnwn', 'N': 'nnnnwnnww', 'O': 'wnnnwnnwn',
  'P': 'nnwnwnnwn', 'Q': 'nnnnnnwww', 'R': 'wnnnnnwwn', 'S': 'nnwnnnwwn', 'T': 'nnnnwnwwn',
  'U': 'wwnnnnnnw', 'V': 'nwwnnnnnw', 'W': 'wwwnnnnnn', 'X': 'nwnnwnnnw', 'Y': 'wwnnwnnnn',
  'Z': 'nwwnwnnnn', '-': 'nwnnnnwnw', '.': 'wwnnnnwnn', ' ': 'nwwnnnwnn', '$': 'nwnwnwnnn',
  '/': 'nwnwnnnwn', '+': 'nwnnnwnwn', '%': 'nnnwnwnwn', '*': 'nwnnwnwnn'
};

export interface Code39Options { height?: number; narrow?: number; }

// Returns an inline SVG string. Bars are black rects on white; the value is not
// drawn as text here (the tag adds its own human-readable line).
export function code39Svg(value: string, opts: Code39Options = {}): string {
  const height = opts.height ?? 44;
  const narrow = opts.narrow ?? 2;
  const wide = narrow * 3;
  const clean = (value || '').toUpperCase().replace(/[^0-9A-Z\-. $/+%]/g, '');
  const chars = ('*' + clean + '*').split('');

  let x = 0;
  const rects: string[] = [];
  chars.forEach((ch, idx) => {
    const pat = PATTERNS[ch];
    if (!pat) return;
    for (let i = 0; i < pat.length; i++) {
      const w = pat[i] === 'w' ? wide : narrow;
      const isBar = i % 2 === 0; // elements alternate bar/space, starting with a bar
      if (isBar) rects.push(`<rect x="${x}" y="0" width="${w}" height="${height}" fill="#000"/>`);
      x += w;
    }
    if (idx < chars.length - 1) x += narrow; // inter-character gap
  });

  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${x} ${height}" width="100%" height="${height}" preserveAspectRatio="none">${rects.join('')}</svg>`;
}
