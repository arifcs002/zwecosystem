// Reads a product spreadsheet saved as CSV. Pure functions (no browser APIs) so they can be tested in Node.
// Handles what Excel actually produces: a UTF-8 BOM, "quoted, cells", doubled quotes, CRLF line ends, and ';' as
// the delimiter in regions where ',' is the decimal mark.

export function parseCsv(text: string): string[][] {
  if (text.charCodeAt(0) === 0xfeff) text = text.slice(1);
  const delimiter = detectDelimiter(text);
  const rows: string[][] = [];
  let row: string[] = [];
  let cell = '';
  let quoted = false;

  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (quoted) {
      if (c === '"') {
        if (text[i + 1] === '"') { cell += '"'; i++; } else quoted = false;
      } else cell += c;
    } else if (c === '"') quoted = true;
    else if (c === delimiter) { row.push(cell); cell = ''; }
    else if (c === '\n' || c === '\r') {
      if (c === '\r' && text[i + 1] === '\n') i++;
      row.push(cell); cell = '';
      if (row.some(x => x.trim() !== '')) rows.push(row);   // skip blank lines
      row = [];
    } else cell += c;
  }
  row.push(cell);
  if (row.some(x => x.trim() !== '')) rows.push(row);
  return rows.map(r => r.map(x => x.trim()));
}

function detectDelimiter(text: string): string {
  const firstLine = text.split(/\r?\n/, 1)[0] || '';
  let best = ',', bestCount = -1;
  for (const d of [',', ';', '\t']) {
    // count delimiters outside quotes
    let n = 0, q = false;
    for (const c of firstLine) { if (c === '"') q = !q; else if (c === d && !q) n++; }
    if (n > bestCount) { best = d; bestCount = n; }
  }
  return best;
}

export interface ImportRow {
  name?: string; price?: number | null; wholesalePrice?: number | null; category?: string;
  quantity?: number | null; sizes?: string; barcode?: string; description?: string;
  compareAtPrice?: number | null; uom?: string;
}

const ALIASES: Record<keyof ImportRow, string[]> = {
  name: ['name', 'product', 'productname', 'item', 'itemname', 'পণ্য', 'নাম'],
  price: ['price', 'sellingprice', 'sellprice', 'saleprice', 'retailprice', 'মূল্য', 'দাম'],
  wholesalePrice: ['wholesaleprice', 'wholesale', 'buyprice', 'buyingprice', 'costprice', 'cost', 'purchaseprice'],
  category: ['category', 'categoryname', 'ক্যাটাগরি'],
  quantity: ['quantity', 'qty', 'stock', 'stockquantity', 'পরিমাণ'],
  sizes: ['sizes', 'size', 'sizequantities'],
  barcode: ['barcode', 'code', 'ean', 'upc'],
  description: ['description', 'details', 'বিবরণ'],
  compareAtPrice: ['compareatprice', 'compareat', 'mrp', 'oldprice', 'wasprice'],
  uom: ['unit', 'uom', 'unitofmeasure', 'একক']
};

const norm = (h: string) => h.toLowerCase().replace(/[^a-z0-9ঀ-৿]/g, '');

/** Bangla digits and thousands separators / currency signs are accepted in number cells. */
export function toNumber(raw: string | undefined): number | null {
  if (raw == null) return null;
  let s = raw.trim();
  if (s === '') return null;
  s = s.replace(/[০-৯]/g, d => String(d.charCodeAt(0) - 0x09e6)).replace(/[৳,\s]/g, '');
  if (!/^-?\d+(\.\d+)?$/.test(s)) return NaN;
  return Number(s);
}

export interface MappedTable { rows: ImportRow[]; unknownHeaders: string[]; missingRequired: string[]; }

export function mapTable(table: string[][]): MappedTable {
  if (table.length === 0) return { rows: [], unknownHeaders: [], missingRequired: ['name', 'price', 'wholesalePrice', 'category'] };
  const headers = table[0].map(norm);
  const columnOf: Partial<Record<keyof ImportRow, number>> = {};
  const used = new Set<number>();
  (Object.keys(ALIASES) as (keyof ImportRow)[]).forEach(field => {
    const idx = headers.findIndex((h, i) => !used.has(i) && ALIASES[field].includes(h));
    if (idx >= 0) { columnOf[field] = idx; used.add(idx); }
  });
  const missingRequired = (['name', 'price', 'wholesalePrice', 'category'] as (keyof ImportRow)[]).filter(f => columnOf[f] === undefined);
  const unknownHeaders = table[0].filter((_, i) => !used.has(i) && table[0][i].trim() !== '');

  const numeric: (keyof ImportRow)[] = ['price', 'wholesalePrice', 'quantity', 'compareAtPrice'];
  const rows = table.slice(1).map(cells => {
    const row: any = {};
    (Object.keys(columnOf) as (keyof ImportRow)[]).forEach(f => {
      const raw = cells[columnOf[f]!];
      row[f] = numeric.includes(f) ? toNumber(raw) : (raw ?? '');
    });
    return row as ImportRow;
  });
  return { rows, unknownHeaders, missingRequired };
}

export const TEMPLATE_CSV =
  '﻿name,category,price,wholesale_price,quantity,sizes,barcode,description,compare_at_price,unit\r\n' +
  'Leather Loafer,Shoes,2500,1500,,40:5; 41:3; 42:2,,Genuine leather,3000,pair\r\n' +
  'Cotton Socks,Accessories,120,60,30,,,,,pair\r\n';
