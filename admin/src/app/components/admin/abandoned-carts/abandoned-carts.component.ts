import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AbandonedCartRow, AbandonedCartService, AbandonedStatus } from '../../../services/abandoned-cart/abandoned-cart.service';
import { ConfirmDialogService } from '../../../services/confirm-dialog/confirm-dialog.service';

const TABS: { value: AbandonedStatus | ''; label: string }[] = [
  { value: 'OPEN', label: 'To follow up' }, { value: 'CONTACTED', label: 'Contacted' },
  { value: 'RECOVERED', label: 'Recovered' }, { value: 'DISMISSED', label: 'Dismissed' }, { value: '', label: 'All' }
];

// Carts where the shopper typed a valid phone number at checkout but never placed the order.
// The owner calls / WhatsApps them; placing an order later auto-marks the cart Recovered.
@Component({
  selector: 'app-abandoned-carts',
  standalone: true,
  imports: [CommonModule],
  template: `
<div class="page-container">
  <div class="page-header">
    <div>
      <h1 class="page-title">🛒 Abandoned Carts</h1>
      <p class="page-subtitle">Shoppers who left their number but didn't order — follow up to win the sale</p>
    </div>
    <button class="btn-secondary" (click)="load()">↻ Refresh</button>
  </div>

  <div class="alert-error" *ngIf="errorMsg">❌ {{ errorMsg }}</div>

  <div style="display:flex;gap:8px;flex-wrap:wrap;margin-bottom:14px">
    <button *ngFor="let t of tabs" (click)="select(t.value)" [class]="status === t.value ? 'btn-primary' : 'btn-secondary'">{{ t.label }}</button>
  </div>

  <div *ngIf="status === 'OPEN' && rows.length" style="margin-bottom:14px;opacity:.85;font-size:14px">
    {{ rows.length }} cart{{ rows.length === 1 ? '' : 's' }} waiting · potential sales <b>৳{{ potential | number:'1.0-0' }}</b>
  </div>

  <div class="table-card">
    <div class="loading" *ngIf="isLoading">Loading…</div>
    <table class="data-table" *ngIf="!isLoading">
      <thead><tr><th>Customer</th><th>Cart</th><th>Value</th><th>Last activity</th><th>Status</th><th>Actions</th></tr></thead>
      <tbody>
        <ng-container *ngFor="let r of rows">
          <tr>
            <td><b>{{ r.customerName || 'Unknown' }}</b><div style="font-size:13px;opacity:.75">{{ r.customerPhone }}</div></td>
            <td>
              <button class="btn-edit" (click)="open = open === r.id ? null : r.id">{{ r.itemCount }} item{{ r.itemCount === 1 ? '' : 's' }} {{ open === r.id ? '▴' : '▾' }}</button>
            </td>
            <td>৳{{ r.subtotal | number:'1.0-0' }}</td>
            <td style="font-size:13px">{{ ago(r.lastActivity) }}</td>
            <td><span class="name-badge">{{ label(r.status) }}</span></td>
            <td class="actions">
              <a class="btn-edit" [href]="'tel:' + r.customerPhone">📞 Call</a>
              <a class="btn-edit" [href]="whatsapp(r)" target="_blank" rel="noopener">💬 WhatsApp</a>
              <button class="btn-edit" *ngIf="r.status === 'OPEN'" (click)="setStatus(r, 'CONTACTED')">✓ Contacted</button>
              <button class="btn-edit" *ngIf="r.status === 'OPEN' || r.status === 'CONTACTED'" (click)="setStatus(r, 'DISMISSED')">✕ Dismiss</button>
              <button class="btn-delete" (click)="remove(r)">🗑️</button>
            </td>
          </tr>
          <tr *ngIf="open === r.id">
            <td colspan="6" style="background:rgba(255,255,255,.03)">
              <div *ngFor="let it of r.items" style="display:flex;justify-content:space-between;gap:12px;padding:4px 0;font-size:14px">
                <span>{{ it.name }} × {{ it.quantity }}</span><span>৳{{ it.price * it.quantity | number:'1.0-0' }}</span>
              </div>
              <div *ngIf="r.shippingAddress" style="font-size:13px;opacity:.7;margin-top:6px">📍 {{ r.shippingAddress }}</div>
            </td>
          </tr>
        </ng-container>
        <tr *ngIf="rows.length === 0"><td colspan="6" class="empty-row">Nothing here. Carts appear once a shopper enters a valid mobile number at checkout.</td></tr>
      </tbody>
    </table>
  </div>
</div>`
})
export class AbandonedCartsComponent implements OnInit {
  private svc = inject(AbandonedCartService);
  private confirmSvc = inject(ConfirmDialogService);

  tabs = TABS;
  status: AbandonedStatus | '' = 'OPEN';
  rows: AbandonedCartRow[] = [];
  isLoading = false;
  errorMsg = '';
  open: number | null = null;

  get potential() { return this.rows.reduce((s, r) => s + r.subtotal, 0); }

  ngOnInit() { this.load(); }

  select(s: AbandonedStatus | '') { this.status = s; this.load(); }

  load() {
    this.isLoading = true; this.errorMsg = '';
    this.svc.getAll(this.status).subscribe({
      next: (r) => { this.rows = r; this.isLoading = false; },
      error: () => { this.isLoading = false; this.errorMsg = 'Could not load abandoned carts.'; }
    });
  }

  label(s: AbandonedStatus) { return ({ OPEN: 'To follow up', CONTACTED: 'Contacted', RECOVERED: 'Recovered', DISMISSED: 'Dismissed' })[s]; }

  ago(iso: string): string {
    const mins = Math.max(0, Math.round((Date.now() - new Date(iso).getTime()) / 60000));
    if (mins < 1) return 'just now';
    if (mins < 60) return `${mins} min ago`;
    const h = Math.round(mins / 60);
    if (h < 24) return `${h} hr ago`;
    return `${Math.round(h / 24)} day${Math.round(h / 24) === 1 ? '' : 's'} ago`;
  }

  // Bangladeshi numbers are stored as 01XXXXXXXXX; wa.me needs the country code.
  whatsapp(r: AbandonedCartRow): string {
    const msg = `Hi${r.customerName ? ' ' + r.customerName : ''}, you left ${r.itemCount} item${r.itemCount === 1 ? '' : 's'} in your cart. Need any help completing your order?`;
    return `https://wa.me/88${r.customerPhone}?text=${encodeURIComponent(msg)}`;
  }

  setStatus(r: AbandonedCartRow, s: AbandonedStatus) {
    this.svc.setStatus(r.id, s).subscribe({ next: () => this.load(), error: () => this.errorMsg = 'Could not update the status.' });
  }

  async remove(r: AbandonedCartRow) {
    const ok = await this.confirmSvc.confirm({ title: 'Remove cart', message: 'Remove this abandoned cart record?', confirmLabel: 'Remove', danger: true });
    if (!ok) return;
    this.svc.remove(r.id).subscribe({ next: () => this.load(), error: () => this.errorMsg = 'Could not remove it.' });
  }
}
