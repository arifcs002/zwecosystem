import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ExpenseService, ProfitReport } from '../../../services/expense/expense.service';

const bdNow = () => new Date(Date.now() + 6 * 3600_000);
const iso = (d: Date) => d.toISOString().slice(0, 10);

@Component({
  selector: 'app-profit-report',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
<div class="page-container">
  <div class="page-header">
    <div>
      <h1 class="page-title">📈 Profit &amp; Loss</h1>
      <p class="page-subtitle">What you actually kept: sales − cost of goods − expenses</p>
    </div>
  </div>

  <div style="display:flex;gap:8px;flex-wrap:wrap;margin-bottom:12px">
    <button *ngFor="let p of presets" (click)="preset(p.key)" [class]="active === p.key ? 'btn-primary' : 'btn-secondary'">{{ p.label }}</button>
  </div>
  <div style="display:flex;gap:10px;flex-wrap:wrap;align-items:end;margin-bottom:16px">
    <div class="form-group" style="margin:0"><label>From</label><input class="form-input" type="date" [(ngModel)]="from" (change)="custom()" /></div>
    <div class="form-group" style="margin:0"><label>To</label><input class="form-input" type="date" [(ngModel)]="to" (change)="custom()" /></div>
  </div>

  <div class="alert-error" *ngIf="errorMsg">❌ {{ errorMsg }}</div>
  <div class="loading" *ngIf="isLoading">Calculating…</div>

  <ng-container *ngIf="report as r">
    <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:12px;margin-bottom:18px">
      <div class="table-card" style="padding:16px"><div style="opacity:.65;font-size:13px">Sales ({{ r.summary.orders }} orders)</div><div style="font-size:24px;font-weight:700">৳{{ r.summary.revenue | number:'1.0-0' }}</div></div>
      <div class="table-card" style="padding:16px"><div style="opacity:.65;font-size:13px">Cost of goods</div><div style="font-size:24px;font-weight:700">৳{{ r.summary.cogs | number:'1.0-0' }}</div></div>
      <div class="table-card" style="padding:16px"><div style="opacity:.65;font-size:13px">Gross profit · {{ r.summary.grossMarginPercent }}%</div><div style="font-size:24px;font-weight:700">৳{{ r.summary.grossProfit | number:'1.0-0' }}</div></div>
      <div class="table-card" style="padding:16px"><div style="opacity:.65;font-size:13px">Expenses</div><div style="font-size:24px;font-weight:700">৳{{ r.summary.expenses | number:'1.0-0' }}</div></div>
      <div class="table-card" style="padding:16px" [style.border-color]="r.summary.netProfit >= 0 ? '#10b981' : '#ef4444'">
        <div style="opacity:.65;font-size:13px">{{ r.summary.netProfit >= 0 ? 'Net profit' : 'Net loss' }}</div>
        <div style="font-size:26px;font-weight:800" [style.color]="r.summary.netProfit >= 0 ? '#34d399' : '#f87171'">{{ r.summary.netProfit < 0 ? '−' : '' }}৳{{ abs(r.summary.netProfit) | number:'1.0-0' }}</div>
      </div>
    </div>
    <p style="opacity:.6;font-size:12.5px;margin:-6px 0 16px">{{ r.note }} Cancelled orders in range: {{ r.summary.cancelledOrders }}. Discounts given: ৳{{ r.summary.discounts | number:'1.0-0' }}.</p>

    <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(320px,1fr));gap:16px">
      <div class="table-card">
        <h3 style="padding:14px 16px 0;margin:0">Expenses by category</h3>
        <table class="data-table"><tbody>
          <tr *ngFor="let c of r.expensesByCategory">
            <td>{{ c.category }}</td>
            <td style="width:45%"><div style="height:8px;border-radius:4px;background:#6366f1" [style.width.%]="barPct(c.total, r.summary.expenses)"></div></td>
            <td style="text-align:right">৳{{ c.total | number:'1.0-0' }}</td>
          </tr>
          <tr *ngIf="r.expensesByCategory.length === 0"><td class="empty-row" colspan="3">No expenses recorded.</td></tr>
        </tbody></table>
      </div>

      <div class="table-card">
        <h3 style="padding:14px 16px 0;margin:0">Day by day</h3>
        <table class="data-table">
          <thead><tr><th>Date</th><th>Orders</th><th>Sales</th><th>Cost</th><th>Expenses</th><th>Profit</th></tr></thead>
          <tbody>
            <tr *ngFor="let d of daysWithActivity(r)">
              <td>{{ d.date }}</td><td>{{ d.orders }}</td><td>৳{{ d.revenue | number:'1.0-0' }}</td><td>৳{{ d.cogs | number:'1.0-0' }}</td><td>৳{{ d.expenses | number:'1.0-0' }}</td>
              <td [style.color]="d.profit >= 0 ? '#34d399' : '#f87171'"><b>{{ d.profit < 0 ? '−' : '' }}৳{{ abs(d.profit) | number:'1.0-0' }}</b></td>
            </tr>
            <tr *ngIf="daysWithActivity(r).length === 0"><td colspan="6" class="empty-row">No sales or expenses in this range.</td></tr>
          </tbody>
        </table>
      </div>
    </div>
  </ng-container>
</div>`
})
export class ProfitReportComponent implements OnInit {
  private svc = inject(ExpenseService);

  presets = [
    { key: 'today', label: 'Today' }, { key: '7d', label: 'Last 7 days' },
    { key: 'month', label: 'This month' }, { key: 'last', label: 'Last month' }
  ];
  active = 'month';
  from = ''; to = '';
  report: ProfitReport | null = null;
  isLoading = false; errorMsg = '';

  abs = Math.abs;
  barPct = (v: number, total: number) => total > 0 ? Math.max(3, Math.round(v / total * 100)) : 0;
  daysWithActivity = (r: ProfitReport) => r.daily.filter(d => d.orders > 0 || d.expenses > 0).reverse();

  ngOnInit() { this.preset('month'); }

  preset(key: string) {
    this.active = key;
    const now = bdNow();
    const y = now.getUTCFullYear(), m = now.getUTCMonth();
    if (key === 'today') { this.from = this.to = iso(now); }
    else if (key === '7d') { this.to = iso(now); this.from = iso(new Date(now.getTime() - 6 * 86400_000)); }
    else if (key === 'month') { this.from = iso(new Date(Date.UTC(y, m, 1))); this.to = iso(now); }
    else { this.from = iso(new Date(Date.UTC(y, m - 1, 1))); this.to = iso(new Date(Date.UTC(y, m, 0))); }
    this.load();
  }

  custom() { this.active = ''; this.load(); }

  load() {
    if (!this.from || !this.to) return;
    this.isLoading = true; this.errorMsg = '';
    this.svc.profit(this.from, this.to).subscribe({
      next: (r) => { this.report = r; this.isLoading = false; },
      error: (e) => { this.isLoading = false; this.report = null; this.errorMsg = e?.error?.message || 'Could not load the report.'; }
    });
  }
}
