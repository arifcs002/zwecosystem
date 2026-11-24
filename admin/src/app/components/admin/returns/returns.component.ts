import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ReturnableLine, ReturnableOrder, ReturnResult, ReturnsService } from '../../../services/returns/returns.service';
import { PosProduct, PosService } from '../../../services/pos/pos.service';

interface Row extends ReturnableLine { qty: number; restock: boolean; }
interface NewItem { product: PosProduct; qty: number; }

// Customer brings something back: find the sale by its receipt number, pick what comes back, then give the
// money back, take it off what the customer owes, or swap it for other goods.
@Component({
  selector: 'app-returns',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
<div class="page-container">
  <div class="page-header">
    <div>
      <h1 class="page-title">↩️ Returns &amp; Exchanges</h1>
      <p class="page-subtitle">Take goods back, refund, or swap — stock and reports update automatically</p>
    </div>
  </div>

  <div class="table-card" style="padding:14px;margin-bottom:14px">
    <form (ngSubmit)="find()" style="display:flex;gap:10px;flex-wrap:wrap">
      <input class="form-input" style="flex:1;min-width:220px" [(ngModel)]="orderNumber" name="on" placeholder="Order number from the receipt, e.g. POS-6393…" autocomplete="off">
      <button class="btn-primary" type="submit" [disabled]="busy || orderNumber.trim().length < 3">Find sale</button>
    </form>
    <p *ngIf="errorMsg" role="alert" style="color:#f87171;margin:10px 0 0;font-size:13.5px">❌ {{ errorMsg }}</p>
  </div>

  <div class="table-card" *ngIf="result" role="status" style="padding:16px;border:1px solid rgba(52,211,153,.4)">
    <h3 style="margin:0 0 6px;color:#34d399">✅ Done — {{ result.returnNumber }}</h3>
    <div>Value of goods taken back: <b>৳{{ result.refundAmount | number:'1.0-0' }}</b> ({{ result.restocked }} item{{ result.restocked === 1 ? '' : 's' }} put back in stock)</div>
    <div *ngIf="result.cashToGiveBack > 0" style="font-size:20px;margin-top:8px">💵 Give the customer <b>৳{{ result.cashToGiveBack | number:'1.0-0' }}</b> in cash</div>
    <div *ngIf="result.customerPays > 0" style="font-size:20px;margin-top:8px">💵 Collect <b>৳{{ result.customerPays | number:'1.0-0' }}</b> from the customer</div>
    <div *ngIf="result.kind === 'EXCHANGE' && result.cashToGiveBack === 0 && result.customerPays === 0" style="margin-top:8px">Even swap — no money to exchange.</div>
    <button class="btn-secondary" style="margin-top:12px" (click)="reset()">Start another</button>
  </div>

  <div class="table-card" *ngIf="data && !result" style="padding:16px">
    <div style="display:flex;justify-content:space-between;flex-wrap:wrap;gap:6px;margin-bottom:10px">
      <div><b>{{ data.order.orderNumber }}</b> · ৳{{ data.order.total | number:'1.0-0' }} · {{ data.order.paymentMethod }}
        <span *ngIf="data.order.customerName"> · {{ data.order.customerName }}</span><span *ngIf="data.order.customerPhone"> ({{ data.order.customerPhone }})</span></div>
      <span style="opacity:.6;font-size:13px">{{ data.order.status }}</span>
    </div>
    <p *ngIf="data.order.status !== 'COMPLETED'" style="color:#fbbf24;font-size:13.5px">Only completed sales can have items returned.</p>

    <table class="data-table" style="width:100%;min-width:0;font-size:13.5px">
      <thead><tr><th>Item</th><th style="text-align:right">Sold</th><th style="text-align:right">Back already</th><th>Return qty</th><th>Back on shelf?</th><th style="text-align:right">Refund</th></tr></thead>
      <tbody>
        <tr *ngFor="let r of rows">
          <td>{{ r.name }}</td>
          <td style="text-align:right">{{ r.sold }}</td>
          <td style="text-align:right">{{ r.returned }}</td>
          <td><input class="form-input" style="width:80px" type="number" min="0" [max]="r.remaining" [(ngModel)]="r.qty" [disabled]="r.remaining === 0" [name]="'q' + r.orderItemId"></td>
          <td><label style="display:flex;gap:6px;align-items:center;font-size:12.5px"><input type="checkbox" [(ngModel)]="r.restock" [name]="'s' + r.orderItemId" [disabled]="r.remaining === 0"> resellable</label></td>
          <td style="text-align:right">৳{{ (r.qty || 0) * r.refundPerUnit | number:'1.0-0' }}</td>
        </tr>
      </tbody>
    </table>

    <div style="margin:14px 0 6px;font-weight:700">What does the customer want?</div>
    <div style="display:flex;gap:18px;flex-wrap:wrap;font-size:14px">
      <label><input type="radio" name="how" [(ngModel)]="how" value="CASH"> Money back (cash)</label>
      <label *ngIf="data.order.customerPhone"><input type="radio" name="how" [(ngModel)]="how" value="ADJUST_DUE"> Take it off what they owe</label>
      <label><input type="radio" name="how" [(ngModel)]="how" value="EXCHANGE"> Swap for other goods</label>
    </div>

    <div *ngIf="how === 'EXCHANGE'" style="margin-top:10px;padding:10px;border-radius:10px;background:rgba(139,92,246,.08)">
      <form (ngSubmit)="addNew()" style="display:flex;gap:8px;flex-wrap:wrap">
        <input class="form-input" style="flex:1;min-width:200px" [(ngModel)]="newCode" name="nc" placeholder="Scan or type the NEW item's barcode, then Enter">
        <button class="btn-secondary" type="submit" [disabled]="!newCode.trim()">Add</button>
      </form>
      <div *ngFor="let n of newItems" style="display:flex;gap:10px;align-items:center;margin-top:8px;font-size:13.5px">
        <span style="flex:1">{{ n.product.name }} — ৳{{ n.product.price | number:'1.0-0' }} <span style="opacity:.6">(stock {{ n.product.stockQuantity }})</span></span>
        <input class="form-input" style="width:80px" type="number" min="1" [max]="n.product.stockQuantity" [(ngModel)]="n.qty" [name]="'n' + n.product.id">
        <button type="button" (click)="removeNew(n)" aria-label="Remove">✕</button>
      </div>
    </div>

    <input class="form-input" style="margin-top:12px" [(ngModel)]="reason" name="reason" maxlength="300" placeholder="Reason (optional): wrong size, damaged, changed mind…">

    <div style="margin-top:14px;padding:12px;border-radius:10px;background:rgba(255,255,255,.04)">
      <div>Value of goods coming back: <b>৳{{ refundTotal | number:'1.0-0' }}</b></div>
      <ng-container *ngIf="how === 'EXCHANGE'">
        <div>New goods: <b>৳{{ newTotal | number:'1.0-0' }}</b></div>
        <div *ngIf="newTotal > refundTotal">Customer pays <b>৳{{ newTotal - refundTotal | number:'1.0-0' }}</b></div>
        <div *ngIf="newTotal < refundTotal">Give back <b>৳{{ refundTotal - newTotal | number:'1.0-0' }}</b> in cash</div>
      </ng-container>
    </div>

    <button class="btn-primary" style="margin-top:12px" (click)="submit()" [disabled]="busy || !canSubmit">{{ busy ? 'Saving…' : 'Confirm' }}</button>
  </div>
</div>`
})
export class ReturnsComponent {
  private api = inject(ReturnsService);
  private pos = inject(PosService);

  orderNumber = ''; reason = ''; newCode = '';
  how: 'CASH' | 'ADJUST_DUE' | 'EXCHANGE' = 'CASH';
  data: ReturnableOrder | null = null;
  rows: Row[] = [];
  newItems: NewItem[] = [];
  result: ReturnResult | null = null;
  busy = false; errorMsg = '';

  get refundTotal() { return this.rows.reduce((s, r) => s + (r.qty || 0) * r.refundPerUnit, 0); }
  get newTotal() { return this.newItems.reduce((s, n) => s + n.product.price * (n.qty || 0), 0); }
  get canSubmit() {
    if (!this.data || this.data.order.status !== 'COMPLETED') return false;
    if (!this.rows.some(r => (r.qty || 0) > 0)) return false;
    if (this.rows.some(r => (r.qty || 0) < 0 || (r.qty || 0) > r.remaining)) return false;
    return this.how !== 'EXCHANGE' || (this.newItems.length > 0 && this.newItems.every(n => n.qty >= 1 && n.qty <= n.product.stockQuantity));
  }

  find() {
    this.errorMsg = ''; this.result = null; this.busy = true;
    this.api.find(this.orderNumber.trim()).subscribe({
      next: d => {
        this.busy = false; this.data = d; this.how = 'CASH'; this.newItems = []; this.reason = '';
        this.rows = d.items.map(i => ({ ...i, qty: 0, restock: true }));
        // A credit sale that is still unpaid should come off the account, not be paid out in cash.
        if (d.order.paymentMethod === 'CREDIT' && d.order.customerPhone) this.how = 'ADJUST_DUE';
      },
      error: (e: HttpErrorResponse) => { this.busy = false; this.data = null; this.errorMsg = e?.error?.message || 'Could not find that sale.'; }
    });
  }

  addNew() {
    const code = this.newCode.trim(); if (!code) return;
    this.pos.lookup(code).subscribe({
      next: p => {
        const ex = this.newItems.find(n => n.product.id === p.id);
        if (ex) ex.qty = Math.min(ex.qty + 1, p.stockQuantity); else this.newItems.push({ product: p, qty: 1 });
        this.newCode = ''; this.errorMsg = '';
      },
      error: () => { this.errorMsg = `No product matches "${code}".`; }
    });
  }
  removeNew(n: NewItem) { this.newItems = this.newItems.filter(x => x !== n); }

  submit() {
    if (!this.data || !this.canSubmit || this.busy) return;
    this.busy = true; this.errorMsg = '';
    const exchange = this.how === 'EXCHANGE';
    this.api.create({
      orderId: this.data.order.id,
      items: this.rows.filter(r => (r.qty || 0) > 0).map(r => ({ orderItemId: r.orderItemId, quantity: r.qty, restock: r.restock })),
      refundMethod: this.how === 'ADJUST_DUE' ? 'ADJUST_DUE' : 'CASH',
      reason: this.reason.trim() || undefined,
      exchangeItems: exchange ? this.newItems.map(n => ({ productId: n.product.id, quantity: n.qty })) : undefined
    }).subscribe({
      next: r => { this.busy = false; this.result = r; },
      error: (e: HttpErrorResponse) => { this.busy = false; this.errorMsg = e?.error?.message || 'Could not save the return.'; }
    });
  }

  reset() { this.data = null; this.rows = []; this.newItems = []; this.result = null; this.orderNumber = ''; this.errorMsg = ''; }
}
