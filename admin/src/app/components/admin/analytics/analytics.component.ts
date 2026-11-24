import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { environment } from '../../../../environments/environment';

const bdNow = () => new Date(Date.now() + 6 * 3600_000);
const iso = (d: Date) => d.toISOString().slice(0, 10);

interface ProductRow { productId: number; name: string; units: number; returnedUnits: number; revenue: number; cost: number; profit: number; marginPercent: number; }
interface DeadRow { productId: number; name: string; stock: number; costValue: number; lastSold: string | null; daysSinceSale: number; }
interface Group { key: string; orders: number; revenue: number; }
interface Summary { orders: number; revenue: number; averageOrder: number; customers: number; repeatCustomers: number; newCustomers: number; channels: Group[]; payments: Group[]; topCustomers: { phone: string; name: string | null; orders: number; spent: number }[]; }

// Owner-only insight: which products earn money, what stock is stuck, who buys and how they pay.
@Component({
  selector: 'app-analytics',
  imports: [CommonModule, FormsModule],
  template: `
<div class="page-container">
  <div class="page-header">
    <div>
      <h1 class="page-title">🔍 Business Insight</h1>
      <p class="page-subtitle">What earns money, what is stuck on the shelf, and who your customers are</p>
    </div>
  </div>

  <div class="alert-error" *ngIf="errorMsg" role="alert">❌ {{ errorMsg }}</div>

  <div style="display:flex;gap:8px;flex-wrap:wrap;margin-bottom:10px">
    <button *ngFor="let p of presets" (click)="preset(p.key)" [class]="active === p.key ? 'btn-primary' : 'btn-secondary'">{{ p.label }}</button>
  </div>
  <div style="display:flex;gap:10px;flex-wrap:wrap;align-items:end;margin-bottom:16px">
    <label style="font-size:12.5px">From <input class="form-input" type="date" [(ngModel)]="from" (change)="custom()" style="margin-top:4px"></label>
    <label style="font-size:12.5px">To <input class="form-input" type="date" [(ngModel)]="to" (change)="custom()" style="margin-top:4px"></label>
  </div>

  <div *ngIf="sum" style="display:grid;grid-template-columns:repeat(auto-fit,minmax(170px,1fr));gap:10px;margin-bottom:16px">
    <div class="table-card" style="padding:12px"><div style="opacity:.65;font-size:12px">Completed orders</div><div style="font-size:22px;font-weight:800">{{ sum.orders }}</div></div>
    <div class="table-card" style="padding:12px"><div style="opacity:.65;font-size:12px">Sales</div><div style="font-size:22px;font-weight:800">৳{{ sum.revenue | number:'1.0-0' }}</div></div>
    <div class="table-card" style="padding:12px"><div style="opacity:.65;font-size:12px">Average order</div><div style="font-size:22px;font-weight:800">৳{{ sum.averageOrder | number:'1.0-0' }}</div></div>
    <div class="table-card" style="padding:12px"><div style="opacity:.65;font-size:12px">Customers (with a phone)</div><div style="font-size:22px;font-weight:800">{{ sum.customers }}</div>
      <div style="font-size:12px;opacity:.7">{{ sum.newCustomers }} new · {{ sum.repeatCustomers }} came back</div></div>
  </div>

  <div *ngIf="sum" style="display:grid;grid-template-columns:repeat(auto-fit,minmax(260px,1fr));gap:12px;margin-bottom:16px">
    <div class="table-card" style="padding:14px"><b>Where they bought</b>
      <div *ngFor="let c of sum.channels" style="display:flex;justify-content:space-between;padding:4px 0;font-size:13.5px"><span>{{ c.key === 'POS' ? 'In the shop (POS)' : c.key === 'ECOMMERCE' ? 'Online' : c.key }} · {{ c.orders }}</span><span>৳{{ c.revenue | number:'1.0-0' }}</span></div>
      <p *ngIf="!sum.channels.length" style="opacity:.6">No sales in this range.</p></div>
    <div class="table-card" style="padding:14px"><b>How they paid</b>
      <div *ngFor="let c of sum.payments" style="display:flex;justify-content:space-between;padding:4px 0;font-size:13.5px"><span>{{ c.key }} · {{ c.orders }}</span><span>৳{{ c.revenue | number:'1.0-0' }}</span></div>
      <p *ngIf="!sum.payments.length" style="opacity:.6">No sales in this range.</p></div>
    <div class="table-card" style="padding:14px"><b>Best customers</b>
      <div *ngFor="let c of sum.topCustomers" style="display:flex;justify-content:space-between;padding:4px 0;font-size:13.5px"><span>{{ c.name || c.phone }} · {{ c.orders }}</span><span>৳{{ c.spent | number:'1.0-0' }}</span></div>
      <p *ngIf="!sum.topCustomers.length" style="opacity:.6">No customers with a phone number yet.</p></div>
  </div>

  <div class="table-card" style="padding:16px;margin-bottom:16px;overflow-x:auto">
    <div style="display:flex;justify-content:space-between;gap:8px;flex-wrap:wrap;align-items:center;margin-bottom:8px">
      <h3 style="margin:0">Products by profit</h3>
      <button class="btn-secondary" (click)="download('products', true)" [disabled]="busy === 'products'">⬇ Download CSV</button>
    </div>
    <p style="opacity:.6;font-size:12.5px;margin:0 0 8px">Revenue is after discounts and returns. Cost uses each product's current cost price.</p>
    <p *ngIf="!products.length" style="opacity:.65">No product sales in this range.</p>
    <table class="data-table" style="width:100%;min-width:0;font-size:13px" *ngIf="products.length">
      <thead><tr><th>Product</th><th style="text-align:right">Sold</th><th style="text-align:right">Revenue</th><th style="text-align:right">Cost</th><th style="text-align:right">Profit</th><th style="text-align:right">Margin</th></tr></thead>
      <tbody><tr *ngFor="let p of products">
        <td>{{ p.name }}<span *ngIf="p.returnedUnits" style="opacity:.55;font-size:11.5px"> ({{ p.returnedUnits }} returned)</span></td>
        <td style="text-align:right">{{ p.units }}</td><td style="text-align:right">৳{{ p.revenue | number:'1.0-0' }}</td><td style="text-align:right">৳{{ p.cost | number:'1.0-0' }}</td>
        <td style="text-align:right;font-weight:700" [style.color]="p.profit < 0 ? '#f87171' : '#34d399'">৳{{ p.profit | number:'1.0-0' }}</td>
        <td style="text-align:right">{{ p.marginPercent }}%</td>
      </tr></tbody>
    </table>
  </div>

  <div class="table-card" style="padding:16px;overflow-x:auto">
    <div style="display:flex;justify-content:space-between;gap:8px;flex-wrap:wrap;align-items:center;margin-bottom:8px">
      <h3 style="margin:0">Stuck stock — {{ dead ? dead.count + ' products, ৳' + (dead.totalCost | number:'1.0-0') + ' tied up' : '' }}</h3>
      <div style="display:flex;gap:8px;align-items:center">
        <select class="form-input" [(ngModel)]="deadDays" (ngModelChange)="loadDead()"><option [ngValue]="30">no sale for 30 days</option><option [ngValue]="60">60 days</option><option [ngValue]="90">90 days</option><option [ngValue]="180">180 days</option></select>
        <button class="btn-secondary" (click)="download('dead-stock', false)" [disabled]="busy === 'dead-stock'">⬇ CSV</button>
      </div>
    </div>
    <p *ngIf="dead && !dead.items.length" style="opacity:.65">Nothing is stuck. 🎉</p>
    <table class="data-table" style="width:100%;min-width:0;font-size:13px" *ngIf="dead && dead.items.length">
      <thead><tr><th>Product</th><th style="text-align:right">In stock</th><th style="text-align:right">Money tied up (cost)</th><th>Last sold</th></tr></thead>
      <tbody><tr *ngFor="let d of dead.items">
        <td>{{ d.name }}</td><td style="text-align:right">{{ d.stock }}</td><td style="text-align:right">৳{{ d.costValue | number:'1.0-0' }}</td>
        <td>{{ d.lastSold ? (d.lastSold | date:'d MMM yyyy') : 'never' }} <span style="opacity:.55;font-size:11.5px">({{ d.daysSinceSale }} days)</span></td>
      </tr></tbody>
    </table>
    <p style="opacity:.6;font-size:12.5px;margin:10px 0 0">Ideas: put these on an offer (Offers page), bundle them with a fast seller, or stop reordering them.</p>
  </div>
</div>`
})
export class AnalyticsComponent implements OnInit {
  private http = inject(HttpClient);
  private api = environment.apiUrl;
  presets = [{ key: 'month', label: 'This month' }, { key: '30', label: 'Last 30 days' }, { key: '90', label: 'Last 90 days' }, { key: 'year', label: 'This year' }];
  active = 'month';
  from = ''; to = '';
  sum: Summary | null = null; products: ProductRow[] = []; dead: { count: number; totalCost: number; items: DeadRow[] } | null = null;
  deadDays = 60; busy = ''; errorMsg = '';

  ngOnInit() { this.preset('month'); this.loadDead(); }

  preset(key: string) {
    this.active = key;
    const now = bdNow();
    this.to = iso(now);
    if (key === 'month') this.from = iso(now).slice(0, 8) + '01';
    else if (key === 'year') this.from = iso(now).slice(0, 4) + '-01-01';
    else this.from = iso(new Date(now.getTime() - Number(key) * 86400_000));
    this.load();
  }
  custom() { this.active = ''; this.load(); }

  load() {
    this.errorMsg = '';
    const params = { from: this.from, to: this.to };
    this.http.get<Summary>(`${this.api}/analytics/summary`, { params }).subscribe({ next: s => this.sum = s, error: (e: HttpErrorResponse) => { this.sum = null; this.errorMsg = e?.error?.message || 'Could not load the summary.'; } });
    this.http.get<ProductRow[]>(`${this.api}/analytics/products`, { params: { ...params, limit: 50 } }).subscribe({ next: p => this.products = p, error: () => this.products = [] });
  }
  loadDead() { this.http.get<any>(`${this.api}/analytics/dead-stock`, { params: { days: this.deadDays } }).subscribe({ next: d => this.dead = d, error: () => this.dead = null }); }

  download(kind: string, ranged: boolean) {
    this.busy = kind; this.errorMsg = '';
    const params: Record<string, string> = ranged ? { from: this.from, to: this.to } : { days: String(this.deadDays) };
    this.http.get(`${this.api}/reports/export/${kind}`, { params, observe: 'response', responseType: 'blob' }).subscribe({
      next: res => {
        this.busy = '';
        const url = URL.createObjectURL(res.body as Blob);
        const a = document.createElement('a'); a.href = url; a.download = `${kind}.csv`; a.click();
        setTimeout(() => URL.revokeObjectURL(url), 2000);
      },
      error: () => { this.busy = ''; this.errorMsg = 'Could not prepare the file.'; }
    });
  }
}
