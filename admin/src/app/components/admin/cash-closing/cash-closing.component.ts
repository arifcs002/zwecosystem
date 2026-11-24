import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { CashClosingRow, CashDay, OperationsService } from '../../../services/operations/operations.service';

const bdToday = () => new Date(Date.now() + 6 * 3600 * 1000).toISOString().slice(0, 10);

// End-of-day check: what the records say should be in the drawer, versus the cash actually counted.
@Component({
  selector: 'app-cash-closing',
  imports: [CommonModule, FormsModule],
  template: `
<div class="page-container">
  <div class="page-header">
    <div>
      <h1 class="page-title">🧮 Daily Cash Closing</h1>
      <p class="page-subtitle">Count the drawer, compare with the books, and record any difference</p>
    </div>
    <input class="form-input" type="date" [(ngModel)]="date" [max]="today" (ngModelChange)="load()" aria-label="Day" style="max-width:170px">
  </div>

  <div class="alert-error" *ngIf="errorMsg" role="alert">❌ {{ errorMsg }}</div>
  <div class="alert-success" *ngIf="okMsg" role="status">✅ {{ okMsg }}</div>

  <div class="table-card" style="padding:16px;margin-bottom:16px" *ngIf="day as d">
    <table style="width:100%;font-size:14px;border-collapse:collapse">
      <tr><td>Cash in the drawer at the start <span *ngIf="!d.openingKnown" style="opacity:.6;font-size:12px">(first time: type it below)</span></td><td style="text-align:right">৳{{ d.figures.openingCash | number:'1.0-0' }}</td></tr>
      <tr><td>+ Cash sales</td><td style="text-align:right">৳{{ d.figures.cashSales | number:'1.0-0' }}</td></tr>
      <tr *ngIf="d.figures.exchangeCreditApplied"><td>− Paid with returned goods (exchanges)</td><td style="text-align:right">৳{{ d.figures.exchangeCreditApplied | number:'1.0-0' }}</td></tr>
      <tr><td>+ Cash received on credit accounts (baki)</td><td style="text-align:right">৳{{ d.figures.cashReceivedOnAccount | number:'1.0-0' }}</td></tr>
      <tr><td>− Cash refunded to customers</td><td style="text-align:right">৳{{ d.figures.cashRefunds | number:'1.0-0' }}</td></tr>
      <tr><td>− Expenses</td><td style="text-align:right">৳{{ d.figures.expenses | number:'1.0-0' }}</td></tr>
      <tr><td>− Paid to suppliers in cash</td><td style="text-align:right">৳{{ d.figures.supplierCashPayments | number:'1.0-0' }}</td></tr>
      <tr style="border-top:1px solid rgba(255,255,255,.15);font-weight:800;font-size:17px"><td style="padding-top:8px">Should be in the drawer</td><td style="text-align:right;padding-top:8px">৳{{ d.figures.expectedCash | number:'1.0-0' }}</td></tr>
    </table>
    <p style="opacity:.6;font-size:12.5px;margin:10px 0 0">
      Not counted as cash: mobile-money / card sales ৳{{ d.figures.info.nonCashSales | number:'1.0-0' }}, sales on credit ৳{{ d.figures.info.soldOnCredit | number:'1.0-0' }},
      mobile-money received on accounts ৳{{ d.figures.info.nonCashReceivedOnAccount | number:'1.0-0' }}. All expenses are assumed paid from the drawer. Online/COD orders are not included.
    </p>
  </div>

  <div class="table-card" style="padding:16px;margin-bottom:16px" *ngIf="day && !day.closing">
    <h3 style="margin:0 0 10px">Close this day</h3>
    <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(200px,1fr));gap:10px">
      <label style="font-size:12.5px;opacity:.85">Cash you counted (৳)
        <input class="form-input" type="number" min="0" [(ngModel)]="counted" style="margin-top:4px"></label>
      <label *ngIf="!day.openingKnown" style="font-size:12.5px;opacity:.85">Opening cash (৳)
        <input class="form-input" type="number" min="0" [(ngModel)]="opening" style="margin-top:4px"></label>
    </div>
    <p *ngIf="counted !== null" style="margin:10px 0 0;font-weight:700" [style.color]="diff === 0 ? '#34d399' : '#fbbf24'">
      {{ diff === 0 ? '✓ The drawer matches.' : (diff < 0 ? 'Short by ৳' : 'Over by ৳') + (diff < 0 ? -diff : diff) }}
    </p>
    <input *ngIf="counted !== null && diff !== 0" class="form-input" style="margin-top:8px" maxlength="300" [(ngModel)]="note" placeholder="What do you think caused the difference? (required)">
    <button class="btn-primary" style="margin-top:12px" (click)="close()" [disabled]="busy || counted === null || (diff !== 0 && !note.trim())">{{ busy ? 'Saving…' : 'Close the day' }}</button>
    <p style="opacity:.6;font-size:12px;margin:8px 0 0">A closed day can't be reopened. The counted cash becomes tomorrow's starting cash.</p>
  </div>

  <div class="table-card" style="padding:16px;margin-bottom:16px;border:1px solid rgba(52,211,153,.35)" *ngIf="day?.closing as c">
    <b style="color:#34d399">Closed</b> · counted ৳{{ c.countedCash | number:'1.0-0' }} · expected ৳{{ c.expectedCash | number:'1.0-0' }}
    · <b [style.color]="c.difference === 0 ? '#34d399' : '#fbbf24'">{{ c.difference === 0 ? 'no difference' : (c.difference < 0 ? 'short ৳' + (-c.difference) : 'over ৳' + c.difference) }}</b>
    <div *ngIf="c.note" style="opacity:.75;font-size:13px;margin-top:4px">“{{ c.note }}”</div>
  </div>

  <div class="table-card" style="padding:16px" *ngIf="closings.length">
    <h3 style="margin:0 0 8px">Recent closings</h3>
    <table class="data-table" style="width:100%;min-width:0;font-size:13px">
      <thead><tr><th>Day</th><th style="text-align:right">Expected</th><th style="text-align:right">Counted</th><th style="text-align:right">Difference</th><th>Note</th></tr></thead>
      <tbody><tr *ngFor="let r of closings">
        <td>{{ r.date | date:'d MMM yyyy' }}</td><td style="text-align:right">৳{{ r.expectedCash | number:'1.0-0' }}</td><td style="text-align:right">৳{{ r.countedCash | number:'1.0-0' }}</td>
        <td style="text-align:right" [style.color]="r.difference === 0 ? '' : '#fbbf24'">{{ r.difference === 0 ? '—' : (r.difference | number:'1.0-0') }}</td><td>{{ r.note }}</td>
      </tr></tbody>
    </table>
  </div>
</div>`
})
export class CashClosingComponent implements OnInit {
  private api = inject(OperationsService);
  today = bdToday(); date = bdToday();
  day: CashDay | null = null; closings: CashClosingRow[] = [];
  counted: number | null = null; opening: number | null = 0; note = '';
  busy = false; errorMsg = ''; okMsg = '';

  get expected() { return (this.day?.figures.expectedCash ?? 0) + (this.day && !this.day.openingKnown ? (this.opening || 0) : 0); }
  get diff() { return Math.round(((this.counted ?? 0) - this.expected) * 100) / 100; }

  ngOnInit() { this.load(); this.loadClosings(); }

  load(keepMessage = false) {
    this.errorMsg = ''; if (!keepMessage) this.okMsg = ''; this.counted = null; this.note = '';
    this.api.cashDay(this.date).subscribe({
      next: d => { this.day = d; },
      error: (e: HttpErrorResponse) => { this.day = null; this.errorMsg = e?.error?.message || 'Could not load this day.'; }
    });
  }
  private loadClosings() { this.api.cashClosings().subscribe({ next: r => this.closings = r, error: () => { /* list just stays empty */ } }); }

  close() {
    if (this.busy || this.counted === null) return;
    this.busy = true; this.errorMsg = '';
    this.api.closeCash({ date: this.date, countedCash: this.counted, openingCash: this.day && !this.day.openingKnown ? (this.opening || 0) : undefined, note: this.note.trim() || undefined }).subscribe({
      next: () => { this.busy = false; this.okMsg = 'The day is closed.'; this.load(true); this.loadClosings(); },
      error: (e: HttpErrorResponse) => { this.busy = false; this.errorMsg = e?.error?.message || 'Could not close the day.'; }
    });
  }
}
