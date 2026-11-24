import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { OperationsService, PurchaseDetail, PurchaseRow, SupplierAccount, SupplierDetail } from '../../../services/operations/operations.service';
import { Supplier, SupplierService } from '../../../services/supplier/supplier.service';
import { Product, ProductService } from '../../../services/product/product.service';
import { AuthService } from '../../../services/auth/auth.service';

interface NewLine { productId: number; name: string; qty: number; cost: number; }
interface RecvLine { itemId: number; name: string; remaining: number; qty: number; cost: number; }

// Ordering stock from suppliers, receiving it, and keeping track of what the shop owes each supplier.
@Component({
  selector: 'app-purchases',
  imports: [CommonModule, FormsModule],
  template: `
<div class="page-container">
  <div class="page-header">
    <div>
      <h1 class="page-title">🚚 Purchases &amp; Suppliers</h1>
      <p class="page-subtitle">Order stock, receive it, and see what you owe each supplier</p>
    </div>
    <div style="display:flex;gap:8px">
      <button [class]="tab === 'orders' ? 'btn-primary' : 'btn-secondary'" (click)="tab = 'orders'">Orders</button>
      <button [class]="tab === 'accounts' ? 'btn-primary' : 'btn-secondary'" (click)="tab = 'accounts'; loadAccounts()">Supplier accounts</button>
    </div>
  </div>

  <div class="alert-error" *ngIf="errorMsg" role="alert">❌ {{ errorMsg }}</div>
  <div class="alert-success" *ngIf="okMsg" role="status">✅ {{ okMsg }}</div>

  <!-- ═════════ ORDERS ═════════ -->
  <ng-container *ngIf="tab === 'orders'">
    <div style="margin-bottom:12px" *ngIf="!creating && !receiving">
      <button class="btn-primary" (click)="creating = true; errorMsg = ''">+ New purchase order</button>
    </div>

    <div class="table-card" style="padding:16px;margin-bottom:14px" *ngIf="creating">
      <h3 style="margin:0 0 10px">New purchase order</h3>
      <div style="display:flex;gap:10px;flex-wrap:wrap;margin-bottom:10px">
        <select class="form-input" style="max-width:260px" [(ngModel)]="supplierId">
          <option [ngValue]="null">— Choose supplier —</option>
          <option *ngFor="let s of suppliers" [ngValue]="s.id">{{ s.name }}</option>
        </select>
        <input class="form-input" style="flex:1;min-width:200px" [(ngModel)]="note" maxlength="300" placeholder="Note (optional)">
      </div>
      <input class="form-input" [(ngModel)]="search" (ngModelChange)="find()" placeholder="Search a product to add (name or barcode)…">
      <div *ngIf="results.length" style="border:1px solid rgba(255,255,255,.12);border-radius:8px;margin:4px 0 10px;max-height:190px;overflow:auto">
        <div *ngFor="let p of results" (click)="addLine(p)" style="padding:7px 10px;cursor:pointer;font-size:13.5px;border-bottom:1px solid rgba(255,255,255,.06)">{{ p.name }} <span style="opacity:.55">· {{ p.barcode }} · stock {{ p.stockQuantity }}</span></div>
      </div>
      <table class="data-table" style="width:100%;min-width:0;font-size:13.5px" *ngIf="lines.length">
        <thead><tr><th>Product</th><th style="width:100px">Qty</th><th style="width:120px">Cost each (৳)</th><th style="text-align:right">Total</th><th></th></tr></thead>
        <tbody><tr *ngFor="let l of lines">
          <td>{{ l.name }}</td>
          <td><input class="form-input" type="number" min="1" [(ngModel)]="l.qty"></td>
          <td><input class="form-input" type="number" min="0" [(ngModel)]="l.cost"></td>
          <td style="text-align:right">৳{{ (l.qty || 0) * (l.cost || 0) | number:'1.0-0' }}</td>
          <td><button type="button" (click)="removeLine(l)" aria-label="Remove">✕</button></td>
        </tr></tbody>
      </table>
      <div style="margin-top:10px;display:flex;gap:10px;align-items:center;flex-wrap:wrap">
        <b>Order total: ৳{{ orderTotal | number:'1.0-0' }}</b>
        <button class="btn-primary" (click)="create()" [disabled]="busy || !supplierId || !lines.length">{{ busy ? 'Saving…' : 'Create order' }}</button>
        <button class="btn-secondary" (click)="creating = false; lines = []">Cancel</button>
      </div>
      <p style="opacity:.6;font-size:12px;margin:8px 0 0">Stock only goes up when you receive the goods.</p>
    </div>

    <div class="table-card" style="padding:16px;margin-bottom:14px;border:1px solid rgba(139,92,246,.4)" *ngIf="receiving as r">
      <h3 style="margin:0 0 4px">Receive — {{ r.po.poNumber }} · {{ r.po.supplier }}</h3>
      <p style="opacity:.65;font-size:13px;margin:0 0 10px">Enter what actually arrived. You can receive in parts.</p>
      <table class="data-table" style="width:100%;min-width:0;font-size:13.5px">
        <thead><tr><th>Product</th><th style="text-align:right">Still expected</th><th style="width:110px">Received now</th><th style="text-align:right">Value</th></tr></thead>
        <tbody><tr *ngFor="let l of r.lines">
          <td>{{ l.name }}</td><td style="text-align:right">{{ l.remaining }}</td>
          <td><input class="form-input" type="number" min="0" [max]="l.remaining" [(ngModel)]="l.qty"></td>
          <td style="text-align:right">৳{{ (l.qty || 0) * l.cost | number:'1.0-0' }}</td>
        </tr></tbody>
      </table>
      <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:10px;margin-top:10px">
        <label style="font-size:12.5px">Paid to supplier now (৳)<input class="form-input" type="number" min="0" [(ngModel)]="paidNow" style="margin-top:4px"></label>
        <label style="font-size:12.5px">Paid by
          <select class="form-input" [(ngModel)]="payMethod" style="margin-top:4px"><option value="cash">Cash</option><option value="bkash">bKash</option><option value="nagad">Nagad</option><option value="bank">Bank</option><option value="other">Other</option></select></label>
      </div>
      <label style="display:flex;gap:8px;align-items:center;font-size:13px;margin-top:8px"><input type="checkbox" [(ngModel)]="updateCost"> Use these costs as the products' buying price (changes your profit reports)</label>
      <p style="margin:10px 0 0;font-weight:700">Received value: ৳{{ receiveValue | number:'1.0-0' }} · stays owed: ৳{{ receiveValue - (paidNow || 0) | number:'1.0-0' }}</p>
      <div style="margin-top:10px;display:flex;gap:10px">
        <button class="btn-primary" (click)="receive()" [disabled]="busy || receiveValue <= 0 && !anyQty">{{ busy ? 'Saving…' : 'Receive goods' }}</button>
        <button class="btn-secondary" (click)="receiving = null">Close</button>
      </div>
    </div>

    <div class="table-card" style="padding:14px;overflow-x:auto" *ngIf="!creating">
      <p *ngIf="!orders.length" style="opacity:.65">No purchase orders yet.</p>
      <table class="data-table" style="width:100%;min-width:0;font-size:13.5px" *ngIf="orders.length">
        <thead><tr><th>Order</th><th>Supplier</th><th>Status</th><th style="text-align:right">Ordered</th><th style="text-align:right">Received</th><th></th></tr></thead>
        <tbody><tr *ngFor="let o of orders">
          <td><b>{{ o.poNumber }}</b><div style="opacity:.6;font-size:12px">{{ o.createdDate | date:'d MMM yyyy' }}</div></td>
          <td>{{ o.supplier }}</td>
          <td>{{ o.status === 'ORDERED' ? 'Waiting' : o.status === 'PARTIAL' ? 'Part received' : o.status === 'RECEIVED' ? 'Received' : 'Cancelled' }}</td>
          <td style="text-align:right">৳{{ o.orderedValue | number:'1.0-0' }}</td><td style="text-align:right">৳{{ o.receivedValue | number:'1.0-0' }}</td>
          <td style="text-align:right;white-space:nowrap">
            <button class="btn-secondary" *ngIf="o.status === 'ORDERED' || o.status === 'PARTIAL'" (click)="startReceive(o)">Receive</button>
            <button class="btn-secondary" *ngIf="o.status === 'ORDERED'" (click)="cancelOrder(o)" style="margin-left:6px">Cancel</button>
          </td>
        </tr></tbody>
      </table>
    </div>
  </ng-container>

  <!-- ═════════ SUPPLIER ACCOUNTS ═════════ -->
  <ng-container *ngIf="tab === 'accounts'">
    <div class="table-card" style="padding:14px;margin-bottom:12px" *ngIf="summary">
      <span style="opacity:.65;font-size:12.5px">Total you owe suppliers</span>
      <div style="font-size:26px;font-weight:800;color:#fbbf24">৳{{ summary.totalOwed | number:'1.0-0' }}</div>
    </div>
    <div style="display:grid;grid-template-columns:minmax(0,1fr) minmax(0,1fr);gap:14px" class="sup-grid">
      <div class="table-card" style="padding:14px;min-width:0">
        <p *ngIf="!accounts.length" style="opacity:.65">No suppliers yet. Add them under Suppliers.</p>
        <table class="data-table" style="width:100%;min-width:0;font-size:13.5px" *ngIf="accounts.length">
          <thead><tr><th>Supplier</th><th style="text-align:right">You owe</th></tr></thead>
          <tbody><tr *ngFor="let a of accounts" (click)="openAccount(a.id)" style="cursor:pointer" [style.background]="detail?.supplier?.id === a.id ? 'rgba(139,92,246,.12)' : ''">
            <td>{{ a.name }}<div style="opacity:.55;font-size:12px">{{ a.phoneNumber }}</div></td>
            <td style="text-align:right;font-weight:700" [style.color]="a.balance > 0 ? '#fbbf24' : a.balance < 0 ? '#34d399' : ''">{{ a.balance < 0 ? '(advance) ' : '' }}৳{{ (a.balance < 0 ? -a.balance : a.balance) | number:'1.0-0' }}</td>
          </tr></tbody>
        </table>
      </div>
      <div class="table-card" style="padding:14px;min-width:0" *ngIf="detail as d">
        <h3 style="margin:0">{{ d.supplier.name }}</h3>
        <div style="font-size:26px;font-weight:800;margin:6px 0" [style.color]="d.balance > 0 ? '#fbbf24' : '#34d399'">{{ d.balance > 0 ? 'You owe' : d.balance < 0 ? 'Advance' : 'Clear' }} ৳{{ (d.balance < 0 ? -d.balance : d.balance) | number:'1.0-0' }}</div>
        <form (ngSubmit)="pay()" style="display:grid;grid-template-columns:1fr 1fr;gap:8px">
          <input class="form-input" type="number" min="1" [(ngModel)]="payAmount" name="pa" placeholder="Amount paid (৳)">
          <select class="form-input" [(ngModel)]="payMethodS" name="pm"><option value="cash">Cash</option><option value="bkash">bKash</option><option value="nagad">Nagad</option><option value="bank">Bank</option><option value="other">Other</option></select>
          <input class="form-input" style="grid-column:1/-1" [(ngModel)]="payNote" name="pn" maxlength="300" placeholder="Note (optional)">
          <button class="btn-primary" style="grid-column:1/-1" type="submit" [disabled]="busy || !payAmount || payAmount <= 0">Record payment</button>
        </form>
        <div style="font-size:13px;font-weight:700;margin:12px 0 6px">History</div>
        <div *ngFor="let e of d.entries" style="display:flex;justify-content:space-between;gap:8px;padding:6px 0;border-top:1px solid rgba(255,255,255,.07);font-size:13px">
          <span><b>{{ e.entryType === 'PURCHASE' ? 'Goods received' : e.entryType === 'PAYMENT' ? 'Payment' : 'Correction' }}</b>
            <span style="opacity:.6"> · {{ e.createdDate | date:'d MMM yy' }}<span *ngIf="e.method"> · {{ e.method }}</span></span><div *ngIf="e.note" style="opacity:.6;font-size:12px">{{ e.note }}</div></span>
          <span [style.color]="e.amount > 0 ? '#fbbf24' : '#34d399'" style="white-space:nowrap">{{ e.amount > 0 ? '+' : '−' }}৳{{ (e.amount < 0 ? -e.amount : e.amount) | number:'1.0-0' }}</span>
        </div>
      </div>
    </div>
  </ng-container>
</div>`,
  styles: [`@media (max-width: 860px) { .sup-grid { grid-template-columns: 1fr !important; } }`]
})
export class PurchasesComponent implements OnInit {
  private api = inject(OperationsService);
  private supplierService = inject(SupplierService);
  private productService = inject(ProductService);
  auth = inject(AuthService);

  tab: 'orders' | 'accounts' = 'orders';
  orders: PurchaseRow[] = []; suppliers: Supplier[] = [];
  creating = false; supplierId: number | null = null; note = ''; search = ''; results: Product[] = []; lines: NewLine[] = [];
  receiving: { po: PurchaseDetail; lines: RecvLine[] } | null = null; paidNow: number | null = null; payMethod = 'cash'; updateCost = false;
  accounts: SupplierAccount[] = []; summary: { totalOwed: number } | null = null; detail: SupplierDetail | null = null;
  payAmount: number | null = null; payMethodS = 'cash'; payNote = '';
  busy = false; errorMsg = ''; okMsg = '';
  private searchTimer: any;

  get orderTotal() { return this.lines.reduce((s, l) => s + (l.qty || 0) * (l.cost || 0), 0); }
  get receiveValue() { return (this.receiving?.lines || []).reduce((s, l) => s + (l.qty || 0) * l.cost, 0); }
  get anyQty() { return (this.receiving?.lines || []).some(l => (l.qty || 0) > 0); }

  ngOnInit() { this.loadOrders(); this.supplierService.getSuppliers().subscribe({ next: s => this.suppliers = s, error: () => { /* the dropdown stays empty */ } }); }
  private fail(e: HttpErrorResponse, fallback: string) { this.busy = false; this.errorMsg = e?.error?.message || fallback; }

  loadOrders() { this.api.purchases().subscribe({ next: o => this.orders = o, error: (e) => this.fail(e, 'Could not load orders.') }); }

  find() {
    clearTimeout(this.searchTimer);
    const q = this.search.trim();
    if (q.length < 2) { this.results = []; return; }
    this.searchTimer = setTimeout(() => this.productService.getProducts(q).subscribe({ next: r => this.results = r.slice(0, 12), error: () => this.results = [] }), 250);
  }
  addLine(p: Product) {
    if (!this.lines.some(l => l.productId === p.id)) this.lines.push({ productId: p.id, name: p.name, qty: 1, cost: p.wholesalePrice ?? 0 });
    this.search = ''; this.results = [];
  }
  removeLine(l: NewLine) { this.lines = this.lines.filter(x => x !== l); }

  create() {
    if (!this.supplierId || this.busy) return;
    if (this.lines.some(l => !(l.qty >= 1) || l.cost < 0 || l.cost == null)) { this.errorMsg = 'Check the quantities and costs.'; return; }
    this.busy = true; this.errorMsg = ''; this.okMsg = '';
    this.api.createPurchase({ supplierId: this.supplierId, note: this.note.trim() || undefined, items: this.lines.map(l => ({ productId: l.productId, quantity: l.qty, unitCost: l.cost })) }).subscribe({
      next: r => { this.busy = false; this.okMsg = `Order ${r.poNumber} created.`; this.creating = false; this.lines = []; this.note = ''; this.loadOrders(); },
      error: e => this.fail(e, 'Could not create the order.')
    });
  }

  startReceive(o: PurchaseRow) {
    this.errorMsg = ''; this.okMsg = '';
    this.api.purchase(o.id).subscribe({
      next: po => { this.receiving = { po, lines: po.items.filter(i => i.remaining > 0).map(i => ({ itemId: i.id, name: i.name, remaining: i.remaining, qty: i.remaining, cost: i.unitCost })) }; this.paidNow = null; this.updateCost = false; },
      error: e => this.fail(e, 'Could not open the order.')
    });
  }
  receive() {
    if (!this.receiving || this.busy) return;
    const lines = this.receiving.lines.filter(l => (l.qty || 0) > 0).map(l => ({ itemId: l.itemId, quantity: l.qty }));
    if (!lines.length) { this.errorMsg = 'Enter how many of each item arrived.'; return; }
    this.busy = true; this.errorMsg = '';
    this.api.receivePurchase(this.receiving.po.id, { lines, paidNow: this.paidNow || 0, method: this.payMethod, updateCost: this.updateCost }).subscribe({
      next: r => { this.busy = false; this.okMsg = `Received ৳${Math.round(r.receivedValue)} of goods. You now owe this supplier ৳${Math.round(r.owedToSupplier)}.`; this.receiving = null; this.loadOrders(); },
      error: e => this.fail(e, 'Could not receive the goods.')
    });
  }
  cancelOrder(o: PurchaseRow) {
    if (!confirm(`Cancel order ${o.poNumber}?`)) return;
    this.api.cancelPurchase(o.id).subscribe({ next: () => { this.okMsg = 'Order cancelled.'; this.loadOrders(); }, error: e => this.fail(e, 'Could not cancel.') });
  }

  loadAccounts() {
    this.errorMsg = '';
    this.api.supplierAccounts().subscribe({ next: a => this.accounts = a, error: e => this.fail(e, 'Could not load supplier accounts.') });
    this.api.supplierSummary().subscribe({ next: s => this.summary = s, error: () => { /* card stays hidden */ } });
  }
  openAccount(id: number) { this.errorMsg = ''; this.api.supplierDetail(id).subscribe({ next: d => this.detail = d, error: e => this.fail(e, 'Could not open the account.') }); }
  pay() {
    if (!this.detail || !this.payAmount || this.busy) return;
    this.busy = true; this.errorMsg = ''; this.okMsg = '';
    this.api.paySupplier(this.detail.supplier.id, this.payAmount, this.payMethodS, this.payNote || undefined).subscribe({
      next: r => { this.busy = false; this.okMsg = `Payment saved. You now owe ৳${Math.round(r.balance)}.`; this.payAmount = null; this.payNote = ''; this.openAccount(this.detail!.supplier.id); this.loadAccounts(); },
      error: e => this.fail(e, 'Could not save the payment.')
    });
  }
}
