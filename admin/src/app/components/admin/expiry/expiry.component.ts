import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { environment } from '../../../../environments/environment';
import { Product, ProductService } from '../../../services/product/product.service';

interface Lot { id: number; productId: number; productName: string; batchNo: string | null; expiryDate: string; quantity: number; valueAtCost: number; daysLeft: number; }
interface Expiring { expired: Lot[]; soon: Lot[]; expiredValue: number; soonValue: number; }

// Lots with expiry dates: what has expired, what expires soon, and a way to throw away what is left.
@Component({
  selector: 'app-expiry',
  imports: [CommonModule, FormsModule],
  template: `
<div class="page-container">
  <div class="page-header">
    <div>
      <h1 class="page-title">⏳ Expiry Dates</h1>
      <p class="page-subtitle">Lots of stock with an expiry date. Expired units can't be sold.</p>
    </div>
    <label style="font-size:12.5px">Show lots expiring within
      <select class="form-input" [(ngModel)]="days" (ngModelChange)="load()" style="margin-top:4px">
        <option [ngValue]="14">14 days</option><option [ngValue]="30">30 days</option><option [ngValue]="60">60 days</option><option [ngValue]="90">90 days</option>
      </select></label>
  </div>

  <div class="alert-error" *ngIf="errorMsg" role="alert">❌ {{ errorMsg }}</div>
  <div class="alert-success" *ngIf="okMsg" role="status">✅ {{ okMsg }}</div>

  <div class="table-card" style="padding:16px;margin-bottom:16px">
    <h3 style="margin:0 0 8px">Add a lot</h3>
    <div style="display:flex;gap:8px;flex-wrap:wrap;align-items:end">
      <label style="font-size:12.5px;flex:1;min-width:200px">Product
        <select class="form-input" [(ngModel)]="n.productId" style="margin-top:4px;width:100%">
          <option [ngValue]="null">Choose…</option>
          <option *ngFor="let p of products" [ngValue]="p.id">{{ p.name }} (in stock: {{ p.stock ?? p.stockQuantity }})</option>
        </select></label>
      <label style="font-size:12.5px">Lot / batch no. <input class="form-input" maxlength="40" [(ngModel)]="n.batchNo" style="margin-top:4px;width:130px"></label>
      <label style="font-size:12.5px">Expiry date <input class="form-input" type="date" [(ngModel)]="n.expiryDate" style="margin-top:4px"></label>
      <label style="font-size:12.5px">Units <input class="form-input" type="number" min="1" [(ngModel)]="n.quantity" style="margin-top:4px;width:90px"></label>
      <button class="btn-primary" (click)="add()" [disabled]="busy || !n.productId || !n.expiryDate || !n.quantity">Add lot</button>
    </div>
    <p style="opacity:.6;font-size:12.5px;margin:8px 0 0">A lot says which part of the product's stock expires on that date. The units in lots can't be more than the stock you have. Sales use the lot that expires first.</p>
  </div>

  <div class="table-card" style="padding:16px;margin-bottom:16px;border:1px solid rgba(248,113,113,.4)" *ngIf="data && data.expired.length">
    <h3 style="margin:0 0 8px;color:#f87171">Expired — {{ data.expired.length }} lots (cost ৳{{ data.expiredValue | number:'1.0-0' }})</h3>
    <ng-container *ngTemplateOutlet="tbl; context: { rows: data.expired, writeOff: true }"></ng-container>
  </div>
  <div class="table-card" style="padding:16px">
    <h3 style="margin:0 0 8px">Expiring in the next {{ days }} days{{ data ? ' — ' + data.soon.length + ' lots (cost ৳' + (data.soonValue | number:'1.0-0') + ')' : '' }}</h3>
    <p *ngIf="data && !data.soon.length" style="opacity:.65">Nothing is about to expire.</p>
    <ng-container *ngIf="data && data.soon.length"><ng-container *ngTemplateOutlet="tbl; context: { rows: data.soon, writeOff: false }"></ng-container></ng-container>
  </div>

  <ng-template #tbl let-rows="rows" let-writeOff="writeOff">
    <table class="data-table" style="width:100%;min-width:0;font-size:13.5px">
      <thead><tr><th>Product</th><th>Lot</th><th>Expires</th><th style="text-align:right">Units</th><th style="text-align:right">Cost</th><th></th></tr></thead>
      <tbody><tr *ngFor="let l of rows">
        <td>{{ l.productName }}</td><td>{{ l.batchNo }}</td>
        <td>{{ l.expiryDate | date:'d MMM yyyy' }} <span style="opacity:.6;font-size:12px">({{ l.daysLeft < 0 ? -l.daysLeft + ' days ago' : l.daysLeft === 0 ? 'today' : 'in ' + l.daysLeft + ' days' }})</span></td>
        <td style="text-align:right">{{ l.quantity }}</td><td style="text-align:right">৳{{ l.valueAtCost | number:'1.0-0' }}</td>
        <td style="text-align:right"><button class="btn-secondary" (click)="writeOffLot(l)" [disabled]="busy">Write off</button></td>
      </tr></tbody>
    </table>
  </ng-template>
</div>`
})
export class ExpiryComponent implements OnInit {
  private http = inject(HttpClient);
  private productApi = inject(ProductService);
  private api = `${environment.apiUrl}/batches`;
  days = 30; data: Expiring | null = null; products: (Product & { stock?: number; stockQuantity?: number })[] = [];
  n: { productId: number | null; batchNo: string; expiryDate: string; quantity: number | null } = { productId: null, batchNo: '', expiryDate: '', quantity: null };
  busy = false; errorMsg = ''; okMsg = '';

  ngOnInit() { this.load(); this.productApi.getProducts().subscribe({ next: p => this.products = p as any, error: () => { /* stays empty */ } }); }
  load() { this.http.get<Expiring>(`${this.api}/expiring`, { params: { days: this.days } }).subscribe({ next: d => this.data = d, error: () => this.errorMsg = 'Could not load the lots.' }); }

  add() {
    this.busy = true; this.errorMsg = ''; this.okMsg = '';
    this.http.post(this.api, { productId: this.n.productId, batchNo: this.n.batchNo.trim() || null, expiryDate: this.n.expiryDate, quantity: this.n.quantity }).subscribe({
      next: () => { this.busy = false; this.okMsg = 'Lot added.'; this.n = { productId: null, batchNo: '', expiryDate: '', quantity: null }; this.load(); },
      error: (e: HttpErrorResponse) => { this.busy = false; this.errorMsg = e?.error?.message || 'Could not add the lot.'; }
    });
  }

  writeOffLot(l: Lot) {
    if (!confirm(`Write off ${l.quantity} units of ${l.productName}? Your stock goes down by ${l.quantity}. This cannot be undone.`)) return;
    this.busy = true; this.errorMsg = ''; this.okMsg = '';
    this.http.post<{ writtenOff: number }>(`${this.api}/${l.id}/writeoff`, { note: l.daysLeft < 0 ? 'Expired' : 'Written off' }).subscribe({
      next: r => { this.busy = false; this.okMsg = `${r.writtenOff} units written off.`; this.load(); },
      error: (e: HttpErrorResponse) => { this.busy = false; this.errorMsg = e?.error?.message || 'Could not write off.'; }
    });
  }
}
