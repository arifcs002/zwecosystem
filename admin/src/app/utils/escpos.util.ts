// ESC/POS helpers for thermal receipt printers. Pure functions (no browser APIs) so they can be tested in Node.
//
// Receipts are printed as a bitmap rather than as text: ESC/POS text modes have no Bangla glyphs, whereas a
// picture of the receipt (drawn by the browser, which has Bangla fonts) prints the same on every printer.

export const ESC = 0x1b;
export const GS = 0x1d;

/** ESC @ — reset the printer to its defaults. */
export const initPrinter = (): number[] => [ESC, 0x40];
/** ESC d n — print and feed n lines. */
export const feed = (lines: number): number[] => [ESC, 0x64, Math.max(0, Math.min(255, lines | 0))];
/** GS V B 0 — feed to the cutting position and make a partial cut (printers without a cutter ignore it). */
export const cutPaper = (): number[] => [GS, 0x56, 0x42, 0x00];

export interface Mono { width: number; height: number; bytesPerRow: number; data: Uint8Array; }

/**
 * RGBA pixels -> 1-bit bitmap (bit set = black dot), 8 pixels per byte, most significant bit first.
 * Transparent pixels are treated as white paper.
 */
export function toMonochrome(rgba: ArrayLike<number>, width: number, height: number, threshold = 160): Mono {
  if (rgba.length < width * height * 4) throw new Error('Pixel data is smaller than width x height.');
  const bytesPerRow = Math.ceil(width / 8);
  const data = new Uint8Array(bytesPerRow * height);
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      const i = (y * width + x) * 4;
      const a = rgba[i + 3] / 255;
      const lum = (0.299 * rgba[i] + 0.587 * rgba[i + 1] + 0.114 * rgba[i + 2]) * a + 255 * (1 - a);
      if (lum < threshold) data[y * bytesPerRow + (x >> 3)] |= 0x80 >> (x & 7);
    }
  }
  return { width, height, bytesPerRow, data };
}

/**
 * GS v 0 raster commands for a bitmap, split into horizontal bands. Cheap printers have small buffers and
 * can garble or hang on one very tall image, so each band is a separate command.
 */
export function rasterCommands(mono: Mono, bandRows = 96): number[][] {
  const cmds: number[][] = [];
  for (let y = 0; y < mono.height; y += bandRows) {
    const rows = Math.min(bandRows, mono.height - y);
    const header = [GS, 0x76, 0x30, 0x00, mono.bytesPerRow & 0xff, (mono.bytesPerRow >> 8) & 0xff, rows & 0xff, (rows >> 8) & 0xff];
    const body = mono.data.subarray(y * mono.bytesPerRow, (y + rows) * mono.bytesPerRow);
    cmds.push([...header, ...body]);
  }
  return cmds;
}

/** A complete print job: reset, the bitmap, a few blank lines to clear the tear bar, cut. */
export function buildReceiptJob(mono: Mono, opts: { feedLines?: number; cut?: boolean } = {}): Uint8Array {
  const parts: number[] = [...initPrinter()];
  for (const c of rasterCommands(mono)) parts.push(...c);
  parts.push(...feed(opts.feedLines ?? 4));
  if (opts.cut !== false) parts.push(...cutPaper());
  return Uint8Array.from(parts);
}

/** Splits bytes into BLE-sized writes (default 100 bytes: safe under the usual 20–512 byte MTU limits). */
export function chunk(bytes: Uint8Array, size = 100): Uint8Array[] {
  const out: Uint8Array[] = [];
  for (let i = 0; i < bytes.length; i += size) out.push(bytes.subarray(i, Math.min(i + size, bytes.length)));
  return out;
}
