import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { environment } from '../../../../environments/environment';
import { OperationsService } from '../../../services/operations/operations.service';

interface Item { productId: number; name: string; supplierId: number | null; supplierName: string | null; stock: number; onOrder: number; sold: number; unitCost: number; perDay: number; daysLeft: number; coverDays: number; suggestedQty: number; suggestedCost: number; qty?: number; }
interface Suggestions { windowDays: number; coverDays: number; warnDays: number; total: number; totalCost: number; items: Item[]; }

// What to buy next: products that sell fast and will run out soon, grouped by supplier, ready to become purchase orders.
@Component({
  selector: 'app-reorder',
  imports: [CommonModule, FormsModule],
  template: `
<div class="page-container">
  <div class="page-header">
    <div>
      <h1 class="page-title">🛒 What to Reorder</h1>
      <p class="page-subtitle">Based on what really sold lately, what is on the shelf and what is already on order</p>
    </div>
  </div>

  <div class="alert-error" *ngIf="errorMsg" role="alert">❌ {{ errorMsg }}</div>
  <div class="alert-success" *ngIf="okMsg" role="status">✅ {{ okMsg }}</div>

  <div class="table-card" style="padding:14px;margin-bottom:16px;display:flex;gap:12px;flex-wrap:wrap;align-items:end">
    <label style="font-size:12.5px">Measure sales over the last
      <select class="form-input" [(ngModel)]="windowDays" (ngModelChange)="load()" style="margin-top:4px"><option [ngValue]="14">14 days</option><option [ngValue]="30">30 days</option><option [ngValue]="60">60 days</option></select></label>
    <label style="font-size:12.5px">An order should last
      <select class="form-input" [(ngModel)]="coverDays" (ngModelChange)="load()" style="margin-top:4px"><option [ngValue]="14">14 days</option><option [ngValue]="30">30 days</option><option [ngValue]="45">45 days</option></select></label>
    <label style="font-size:12.5px">Warn when stock lasts less than
      <select class="form-input" [(ngModel)]="warnDays" (ngModelChange)="load()" style="margin-top:4px"><option [ngValue]="7">7 days</option><option [ngValue]="10">10 days</option><option [ngValue]="14">14 days</option><option [ngValue]="21">21 days</option></select></label>
  </div>

  <p *ngIf="data && !data.items.length" style="opacity:.7">Nothing needs reordering right now. 🎉</p>

  <div class="table-card" style="padding:16px;margin-bottom:16px" *ngFor="let g of grouped">
    <div style="display:flex;justify-content:space-between;gap:8px;flex-wrap:wrap;align-items:center;margin-bottom:8px">
      <h3 style="margin:0">{{ g.name }}</h3>
      <button class="btn-primary" *ngIf="g.id" (click)="makeOrder(g)" [disabled]="busy">Create purchase order (৳{{ cost(g.items) | number:'1.0-0' }})</button>
      <span *ngIf="!g.id" style="opacity:.65;font-size:12.5px">Set a supplier on these products to order them from here</span>
    </div>
    <table class="data-table" style="width:100%;min-width:0;font-size:13px">
      <thead><tr><th>Product</th><th style="text-align:right">In stock</th><th style="text-align:right">On order</th><th style="text-align:right">Sells / day</th><th style="text-align:right">Lasts</th><th style="text-align:right">Order</th></tr></thead>
      <tbody><tr *ngFor="let i of g.items">
        <td>{{ i.name }}</td><td style="text-align:right">{{ i.stock }}</td><td style="text-align:right">{{ i.onOrder || '' }}</td>
        <td style="text-align:right">{{ i.perDay }}</td><td style="text-align:right" [style.color]="i.coverDays < 3 ? '#f87171' : ''">{{ i.coverDays }} days</td>
        <td style="text-align:right"><input class="form-input" type="number" min="0" style="width:80px;text-align:right" [(ngModel)]="i.qty"></td>
      </tr></tbody>
    </table>
  </div>
</div>`
})
export class ReorderComponent implements OnInit {
  private http = inject(HttpClient);
  private ops = inject(OperationsService);
  private router = inject(Router);
  windowDays = 30; coverDays = 30; warnDays = 10;
  data: Suggestions | null = null; grouped: { id: number | null; name: string; items: Item[] }[] = []; busy = false; errorMsg = ''; okMsg = '';

  ngOnInit() { this.load(); }
  load() {
    this.http.get<Suggestions>(`${environment.apiUrl}/reorder/suggestions`, { params: { windowDays: this.windowDays, coverDays: this.coverDays, warnDays: this.warnDays } }).subscribe({
      next: d => { d.items.forEach(i => i.qty = i.suggestedQty); this.data = d; this.grouped = this.groups(); },
      error: () => this.errorMsg = 'Could not work out the suggestions.'
    });
  }

  groups() {
    const m = new Map<string, { id: number | null; name: string; items: Item[] }>();
    for (const i of this.data?.items || []) {
      const k = String(i.supplierId ?? 'none');
      if (!m.has(k)) m.set(k, { id: i.supplierId, name: i.supplierName || 'No supplier set', items: [] });
      m.get(k)!.items.push(i);
    }
    return [...m.values()];
  }
  cost(items: Item[]) { return items.reduce((s, i) => s + (i.qty || 0) * i.unitCost, 0); }

  makeOrder(g: { id: number | null; items: Item[] }) {
    const lines = g.items.filter(i => (i.qty || 0) > 0).map(i => ({ productId: i.productId, quantity: i.qty!, unitCost: i.unitCost }));
    if (!g.id || !lines.length) return;
    this.busy = true; this.errorMsg = ''; this.okMsg = '';
    this.ops.createPurchase({ supplierId: g.id, items: lines, note: 'From reorder suggestions' }).subscribe({
      next: r => { this.busy = false; this.okMsg = `Purchase order ${r.poNumber} created.`; this.load(); },
      error: (e: HttpErrorResponse) => { this.busy = false; this.errorMsg = e?.error?.message || 'Could not create the purchase order.'; }
    });
  }
}
