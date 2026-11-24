// Draws a sale receipt onto a canvas so it can be printed as a bitmap (see escpos.util.ts).
// The browser does the text layout, so Bangla and any other script prints correctly.

export interface ReceiptLine { name: string; quantity: number; unitPrice: number; }

export interface ReceiptData {
  header?: string;
  footer?: string;
  orderNumber: string;
  lines?: ReceiptLine[];
  subtotal: number;
  discount: number;
  total: number;
  paymentMethod?: string;
  amountPaid?: number | null;   // credit sales
  due?: number | null;          // credit sales: still owed on this sale
  balance?: number | null;      // credit sales: customer's total owed after this sale
  cashierName?: string;
  date?: Date;
  offline?: boolean;
}

/** 58 mm paper prints 384 dots across, 80 mm prints 576. */
export const PAPER_WIDTH_DOTS = { mm58: 384, mm80: 576 } as const;

const FONT = '"Noto Sans Bengali", "Hind Siliguri", "Nirmala UI", system-ui, Arial, sans-serif';

const money = (n: number) => '৳' + Math.round(n).toLocaleString('en-US');

function wrap(ctx: CanvasRenderingContext2D, text: string, maxWidth: number): string[] {
  const out: string[] = [];
  for (const para of (text || '').split('\n')) {
    let line = '';
    for (const word of para.split(/\s+/).filter(Boolean)) {
      const test = line ? line + ' ' + word : word;
      if (ctx.measureText(test).width <= maxWidth || !line) line = test;
      else { out.push(line); line = word; }
    }
    out.push(line);
  }
  return out;
}

export function renderReceipt(data: ReceiptData, widthDots: number = PAPER_WIDTH_DOTS.mm58): { width: number; height: number; rgba: Uint8ClampedArray } {
  const pad = 10;
  const inner = widthDots - pad * 2;
  const probe = document.createElement('canvas').getContext('2d')!;

  // Pass 1 builds the drawing list (so the height is known); pass 2 paints it.
  type Op = { kind: 'text'; text: string; size: number; bold?: boolean; align: 'left' | 'center' | 'right' }
    | { kind: 'row'; left: string; right: string; size: number; bold?: boolean }
    | { kind: 'rule' } | { kind: 'gap'; h: number };
  const ops: Op[] = [];
  const text = (t: string, size = 22, bold = false, align: 'left' | 'center' | 'right' = 'left') => {
    probe.font = `${bold ? 'bold ' : ''}${size}px ${FONT}`;
    for (const l of wrap(probe, t, inner)) ops.push({ kind: 'text', text: l, size, bold, align });
  };

  if (data.header) text(data.header, 28, true, 'center');
  ops.push({ kind: 'gap', h: 4 });
  text(`Order: ${data.orderNumber}`, 20, false, 'center');
  text((data.date ?? new Date()).toLocaleString('en-GB', { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' }), 18, false, 'center');
  if (data.cashierName) text(`Cashier: ${data.cashierName}`, 18, false, 'center');
  ops.push({ kind: 'rule' });

  for (const l of data.lines ?? []) {
    text(l.name, 21, true);
    ops.push({ kind: 'row', left: `${l.quantity} x ${money(l.unitPrice)}`, right: money(l.quantity * l.unitPrice), size: 20 });
  }
  if (data.lines?.length) ops.push({ kind: 'rule' });

  ops.push({ kind: 'row', left: 'Subtotal', right: money(data.subtotal), size: 21 });
  if (data.discount > 0) ops.push({ kind: 'row', left: 'Discount', right: '-' + money(data.discount), size: 21 });
  ops.push({ kind: 'row', left: 'TOTAL', right: money(data.total), size: 28, bold: true });
  if (data.paymentMethod === 'CREDIT') {
    ops.push({ kind: 'row', left: 'Paid now', right: money(data.amountPaid ?? 0), size: 21 });
    ops.push({ kind: 'row', left: 'DUE (BAKI)', right: money(data.due ?? 0), size: 26, bold: true });
    if (data.balance != null && data.balance !== data.due) ops.push({ kind: 'row', left: 'Total owed', right: money(data.balance), size: 20 });
  } else if (data.paymentMethod) text(`Paid via ${data.paymentMethod}`, 19, false, 'center');
  ops.push({ kind: 'rule' });
  if (data.footer) text(data.footer, 20, false, 'center');
  ops.push({ kind: 'gap', h: 14 });

  const lineH = (size: number) => Math.round(size * 1.35);
  const height = pad + ops.reduce((h, o) => h + (o.kind === 'text' ? lineH(o.size) : o.kind === 'row' ? lineH(o.size) : o.kind === 'rule' ? 14 : o.h), 0);

  const canvas = document.createElement('canvas');
  canvas.width = widthDots;
  canvas.height = height;
  const ctx = canvas.getContext('2d')!;
  ctx.fillStyle = '#fff';
  ctx.fillRect(0, 0, widthDots, height);
  ctx.fillStyle = '#000';
  ctx.textBaseline = 'top';

  let y = pad;
  for (const o of ops) {
    if (o.kind === 'gap') { y += o.h; continue; }
    if (o.kind === 'rule') { ctx.fillRect(pad, y + 6, inner, 2); y += 14; continue; }
    ctx.font = `${o.bold ? 'bold ' : ''}${o.size}px ${FONT}`;
    if (o.kind === 'text') {
      ctx.textAlign = o.align;
      const x = o.align === 'center' ? widthDots / 2 : o.align === 'right' ? widthDots - pad : pad;
      ctx.fillText(o.text, x, y);
    } else {
      ctx.textAlign = 'left'; ctx.fillText(o.left, pad, y);
      ctx.textAlign = 'right'; ctx.fillText(o.right, widthDots - pad, y);
    }
    y += lineH(o.size);
  }

  const img = ctx.getImageData(0, 0, widthDots, height);
  return { width: widthDots, height, rgba: img.data };
}
